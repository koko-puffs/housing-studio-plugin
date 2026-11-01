using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Interface.ImGuiNotification;
using Dalamud.Interface.Textures.TextureWraps;
using HousingStudio.Core;
using HousingStudio.Game;

namespace HousingStudio.Session;

/// <summary>
/// The plugin and the site, closer (0.7.0), with the token from Settings → Help:
/// <list type="bullet">
/// <item>the inbox (GameLink.Inbox.cs): builds and designs sent from the site with Send to game,
/// checked about once a minute, opened as a file in the plugin's folder;</item>
/// <item>verified homes (GameLink.Homes.cs): which of your designs is the home you're in, and
/// verifying one; and visitors: a home you visit that's a published, verified design, with like,
/// save and stamp;</item>
/// <item>shots from the game (GameLink.Shots.cs): a picture of your verified home with where the
/// camera was, sent to its design.</item>
/// </list>
/// Every call to the site goes from a worker (Core/SiteLink.Calls.cs, never throwing, with its own
/// wait); its answer comes back to the framework thread through <see cref="finished"/>, so all
/// this class's state is the framework thread's (the window draws on that thread too). The game
/// is read on the framework thread only. Nothing starts while a run goes (the visitor panel's like,
/// save and stamp excepted), while zoning, logged out, or after the plugin is unloaded. The token
/// is never logged.
/// </summary>
public sealed partial class GameLink : IDisposable
{
    private readonly Configuration config;
    private readonly ActivityLog log;
    private readonly PlotTable plots;
    private readonly Func<bool> runActive;

    /// <summary>One client for every call; each call has its own wait (SiteLink's timeouts).</summary>
    private readonly HttpClient http = new() { Timeout = Timeout.InfiniteTimeSpan };

    private readonly CancellationTokenSource stop = new();

    /// <summary>What workers finished, run on the framework thread at the next tick.</summary>
    private readonly ConcurrentQueue<Action> finished = new();

    /// <summary>Every texture this made and hasn't let go of, so an unload frees them whatever is under way.</summary>
    private readonly HashSet<IDalamudTextureWrap> textures = new();

    private volatile bool disposed;
    private DateTime failedAt;

    /// <summary>The token the site refused (revoked, or never made): nothing more is asked with it until it changes.</summary>
    private string? refusedToken;

    private string tokenSeen;

    public GameLink(Configuration config, ActivityLog log, PlotTable plots, Func<bool> runActive)
    {
        this.config = config;
        this.log = log;
        this.plots = plots;
        this.runActive = runActive;
        tokenSeen = config.SiteToken;
        // Soon after the plugin loads (and logging in sets it again).
        nextPoll = DateTime.UtcNow + TimeSpan.FromSeconds(10);
    }

    /// <summary>A token is pasted in Settings (it may still be refused).</summary>
    public bool Linked => SiteLink.LooksRight(config.SiteToken);

    /// <summary>The site said it doesn't know the token in hand: link again.</summary>
    public bool TokenRefused => Linked && refusedToken == config.SiteToken;

    /// <summary>Linked with a token the site hasn't refused.</summary>
    public bool Usable => Linked && !TokenRefused;

    public bool RunActive => runActive();

    /// <summary>Logged in, not between zones, not logging out: the game can be read and asked about.</summary>
    private static bool Settled =>
        Services.ClientState.IsLoggedIn && Services.PlayerState.IsLoaded
        && !Services.Condition[ConditionFlag.BetweenAreas] && !Services.Condition[ConditionFlag.BetweenAreas51]
        && !Services.Condition[ConditionFlag.LoggingOut];

    /// <summary>Every frame, after the runners. Never throws into the frame (that would stop a run): what fails is logged.</summary>
    public void Tick(HousingContext ctx)
    {
        if (disposed) return;
        try
        {
            TickNow(ctx);
        }
        catch (Exception ex)
        {
            if (DateTime.UtcNow - failedAt > TimeSpan.FromSeconds(30)) Services.Log.Error(ex, "Housing Studio's site link failed this frame");
            failedAt = DateTime.UtcNow;
        }
    }

    private void TickNow(HousingContext ctx)
    {
        while (finished.TryDequeue(out var then))
        {
            try
            {
                then();
            }
            catch (Exception ex)
            {
                Services.Log.Error(ex, "Housing Studio couldn't take the site's answer");
            }
        }
        if (config.SiteToken != tokenSeen)
        {
            tokenSeen = config.SiteToken;
            TokenChanged();
        }
        var settled = Settled;
        TickInbox(ctx, settled);
        TickHome(ctx, settled);
        TickVisit(ctx, settled);
        TickShot(ctx);
    }

    /// <summary>
    /// Runs <paramref name="work"/> on a worker and hands its result to <paramref name="then"/> on the
    /// framework thread; a fault becomes <paramref name="fault"/>'s result (logged). After an unload
    /// nothing comes back, and a texture made meanwhile is let go of.
    /// </summary>
    private void Work<T>(string what, Func<CancellationToken, Task<T>> work, Action<T> then, Func<Exception, T> fault)
    {
        if (disposed) return;
        var cancel = stop.Token;
        _ = Task.Run(async () =>
        {
            T result;
            try
            {
                result = await work(cancel).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                if (cancel.IsCancellationRequested) return;
                // The exception holds no token: it's only ever in a request's body.
                Services.Log.Error(ex, $"Housing Studio: {what} failed");
                result = fault(ex);
            }
            if (cancel.IsCancellationRequested)
            {
                if (result is IDalamudTextureWrap wrap) Release(wrap);
                return;
            }
            finished.Enqueue(() => then(result));
        });
    }

    /// <summary>A reply for a fault in the plugin itself.</summary>
    private static SiteReply<T> Fault<T>(Exception _) => new(default, "Something went wrong (see /xllog).", "fault");

    /// <summary>Notes the site refused this token: the inbox stops, and the window says to link again.</summary>
    private void Refused(string token, string? why)
    {
        if (token != config.SiteToken || refusedToken == token) return;
        refusedToken = token;
        log.Warn($"Housing Studio didn't accept the plugin's token: {why ?? "it doesn't know it."}");
    }

    /// <summary>Linked again, or unlinked: what was asked with the old token is asked again with the new (or as anyone).</summary>
    private void TokenChanged()
    {
        if (refusedToken != config.SiteToken) refusedToken = null;
        inbox = new List<InboxItem>();
        inboxNote = null;
        inboxAsking = false;
        pollFailures = 0;
        nextPoll = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        Home = null;
        if (Here is { } v)
        {
            v.Asked = false;
            v.Designs = null;
            v.Why = null;
            v.Dismissed = false;
            v.ActNote = null;
        }
        Discard();
    }

    /// <summary>Logged in: the inbox is checked soon.</summary>
    public void LoggedIn() => nextPoll = DateTime.UtcNow + TimeSpan.FromSeconds(5);

    /// <summary>Logged out: the home, the visit and a shot not yet sent go; the inbox stays (it's the token's, not the character's).</summary>
    public void LoggedOut()
    {
        EndVisit();
        Home = null;
        Discard();
    }

    /// <summary>"Housing Studio" in chat, and a Dalamud notification.</summary>
    private static void Tell(string text, bool notification)
    {
        Services.Chat.Print(text, "Housing Studio");
        if (!notification) return;
        try
        {
            Services.Notifications.AddNotification(new Notification
            {
                Title = "Housing Studio",
                Content = text,
                Type = NotificationType.Info,
            });
        }
        catch (Exception ex)
        {
            Services.Log.Debug(ex, "Couldn't show a notification");
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Textures: each one this makes is kept here until it's let go of.

    private T Own<T>(T wrap) where T : IDalamudTextureWrap
    {
        lock (textures)
        {
            if (disposed)
            {
                wrap.Dispose();
                throw new OperationCanceledException("The plugin was unloaded.");
            }
            textures.Add(wrap);
        }
        return wrap;
    }

    private void Release(IDalamudTextureWrap? wrap)
    {
        if (wrap == null) return;
        lock (textures) textures.Remove(wrap);
        try
        {
            wrap.Dispose();
        }
        catch (Exception ex)
        {
            Services.Log.Debug(ex, "Couldn't let go of a texture");
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        // Workers see this and stop; nothing they finish comes back.
        stop.Cancel();
        if (grab is { IsCompletedSuccessfully: true } g) Release(g.Result);
        grab = null;
        List<IDalamudTextureWrap> left;
        lock (textures)
        {
            left = new List<IDalamudTextureWrap>(textures);
            textures.Clear();
        }
        foreach (var w in left)
        {
            try
            {
                w.Dispose();
            }
            catch (Exception ex)
            {
                Services.Log.Debug(ex, "Couldn't let go of a texture");
            }
        }
        Taken = null;
        Here = null;
        Home = null;
        while (finished.TryDequeue(out _))
        {
        }
        http.Dispose();
    }
}
