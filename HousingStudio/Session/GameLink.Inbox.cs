using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HousingStudio.Core;
using HousingStudio.Game;

namespace HousingStudio.Session;

/// <summary>
/// Send to game's inbox: what's waiting (<c>hs_plugin_inbox_list</c>, about once a minute while
/// linked and logged in, soon after logging in, less often after a failure, never with a refused
/// token), what's new said in chat and a notification, and Start's "From the site": Open fetches
/// one (<c>hs_plugin_inbox_get</c>), writes its layout to the plugin's FromSite folder and loads it
/// as choosing a file does; × takes it off (<c>hs_plugin_inbox_remove</c>).
/// </summary>
public sealed partial class GameLink
{
    private static readonly TimeSpan PollEvery = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan PollAtMost = TimeSpan.FromMinutes(15);

    private List<InboxItem> inbox = new();
    private bool inboxAsking;
    private DateTime nextPoll;
    private int pollFailures;
    private (string Text, bool Bad)? inboxNote;
    private readonly HashSet<string> inboxBusy = new();

    /// <summary>What's waiting, newest first (as last asked).</summary>
    public IReadOnlyList<InboxItem> Inbox => inbox;

    /// <summary>How the last Open or × went, in words (bad: it didn't).</summary>
    public (string Text, bool Bad)? InboxNote => inboxNote;

    /// <summary>The item being opened, or null.</summary>
    public string? Opening { get; private set; }

    /// <summary>Being opened or taken off.</summary>
    public bool InboxBusy(string id) => inboxBusy.Contains(id);

    /// <summary>The plugin's folder for things sent from the site.</summary>
    public static string FromSiteFolder => Path.Combine(Services.PluginInterface.ConfigDirectory.FullName, "FromSite");

    /// <summary>Why Open can't go now, or null.</summary>
    public string? CantOpen() =>
        !Usable ? "Link your Housing Studio account in Settings → Help first"
        : runActive() ? "Not while a run is going"
        : Opening != null ? "Opening one already"
        : null;

    private void TickInbox(HousingContext ctx, bool settled)
    {
        if (!config.CheckInbox || !Usable || inboxAsking) return;
        if (!settled || ctx.Busy || runActive() || DateTime.UtcNow < nextPoll) return;
        inboxAsking = true;
        var token = config.SiteToken;
        Work("checking what's sent from the site", c => SiteLink.InboxAsync(http, token, c), r => Polled(token, r), Fault<List<InboxItem>>);
    }

    private void Polled(string token, SiteReply<List<InboxItem>> r)
    {
        inboxAsking = false;
        if (token != config.SiteToken) return;
        if (r.Ok)
        {
            pollFailures = 0;
            nextPoll = DateTime.UtcNow + PollEvery;
            inbox = r.Value ?? new List<InboxItem>();
            var seen = config.InboxSeenAt;
            var fresh = inbox.Where(i => i.CreatedAt.UtcDateTime > seen).ToList();
            if (fresh.Count == 0) return;
            config.InboxSeenAt = DateTime.SpecifyKind(fresh.Max(i => i.CreatedAt.UtcDateTime), DateTimeKind.Utc);
            config.Save();
            if (SiteLink.Arrived(fresh) is { } words)
            {
                log.Info(words);
                Tell(words, notification: true);
            }
            return;
        }
        if (r.TokenRefused)
        {
            Refused(token, r.Why);
            return;
        }
        // Less often after each failure (2, 4, 8 minutes … 15 at most), back to once a minute on the next answer.
        pollFailures++;
        var wait = TimeSpan.FromTicks(Math.Min(PollAtMost.Ticks, PollEvery.Ticks << Math.Min(pollFailures, 4)));
        nextPoll = DateTime.UtcNow + wait;
        if (pollFailures == 3) log.Warn($"Couldn't check what's sent from Housing Studio: {r.Why} Trying again every few minutes.");
        else Services.Log.Debug($"Housing Studio: checking the inbox failed ({r.Hint}): {r.Why}");
    }

    /// <summary>
    /// Open: fetches it, writes its layout to FromSite/&lt;its file name&gt; (an older copy of the same
    /// name is replaced) on a worker, then <paramref name="load"/> loads it on the framework thread as
    /// choosing that file does.
    /// </summary>
    public void Open(InboxItem item, Action<string> load)
    {
        if (CantOpen() != null || inboxBusy.Contains(item.Id)) return;
        var token = config.SiteToken;
        var folder = FromSiteFolder;
        Opening = item.Id;
        inboxBusy.Add(item.Id);
        inboxNote = ($"Opening “{item.Name}”…", false);
        Work("opening something sent from the site", c => Fetch(token, item.Id, folder, c), r => Fetched(token, item, r, load),
            ex => (Fault<InboxEntry>(ex), (string?)null));
    }

    private async Task<(SiteReply<InboxEntry> Reply, string? Path)> Fetch(string token, string id, string folder, CancellationToken cancel)
    {
        var r = await SiteLink.InboxGetAsync(http, token, id, cancel).ConfigureAwait(false);
        if (!r.Ok || r.Value is not { } entry) return (r, null);
        var path = Path.Combine(folder, entry.FileName);
        var temp = path + ".saving";
        try
        {
            Directory.CreateDirectory(folder);
            await File.WriteAllTextAsync(temp, entry.LayoutJson, cancel).ConfigureAwait(false);
            File.Move(temp, path, overwrite: true);
            return (r, path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            try
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
            {
                // Left for the next one to replace.
            }
            return (new SiteReply<InboxEntry>(null, $"Couldn't write it to the plugin's folder: {ex.Message}", "disk"), null);
        }
    }

    private void Fetched(string token, InboxItem item, (SiteReply<InboxEntry> Reply, string? Path) result, Action<string> load)
    {
        Opening = null;
        inboxBusy.Remove(item.Id);
        var (r, path) = result;
        if (path != null && r.Value is { } entry)
        {
            inbox = inbox.Select(i => i.Id == item.Id ? i with { FetchedAt = DateTimeOffset.UtcNow } : i).ToList();
            inboxNote = null;
            log.Done($"Opened “{entry.Name}” from Housing Studio ({path}).");
            if (runActive())
            {
                // A run started while it came: it waits in Recent.
                config.AddRecent(path);
                inboxNote = ($"“{entry.Name}” is in Recent: a run started while it came.", false);
                return;
            }
            load(path);
            return;
        }
        if (r.TokenRefused) Refused(token, r.Why);
        if (r.Gone) inbox = inbox.Where(i => i.Id != item.Id).ToList();
        inboxNote = ($"Couldn't open “{item.Name}”. {r.Why}", true);
    }

    /// <summary>×: takes it off the inbox (here and on the site).</summary>
    public void Remove(InboxItem item)
    {
        if (!Usable || inboxBusy.Contains(item.Id)) return;
        var token = config.SiteToken;
        inboxBusy.Add(item.Id);
        Work("taking something off the inbox", c => SiteLink.InboxRemoveAsync(http, token, item.Id, c), r =>
        {
            inboxBusy.Remove(item.Id);
            if (r.Ok || r.Gone)
            {
                inbox = inbox.Where(i => i.Id != item.Id).ToList();
                if (inboxNote is { Bad: true }) inboxNote = null;
                return;
            }
            if (r.TokenRefused) Refused(token, r.Why);
            inboxNote = ($"Couldn't take “{item.Name}” off. {r.Why}", true);
        }, Fault<bool>);
    }

    /// <summary>Forgets the last Open or ×'s words.</summary>
    public void ClearInboxNote() => inboxNote = null;
}
