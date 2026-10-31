using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Interface.Textures.TextureWraps;
using HousingStudio.Core;
using HousingStudio.Game;

namespace HousingStudio.Session;

/// <summary>What's known of your home on the site: its address, your designs at it, and verifying one.</summary>
public sealed class HomeLook
{
    public HomeLook((PlaceKind, uint, ulong, int?) key, Side side)
    {
        Key = key;
        Side = side;
    }

    public (PlaceKind, uint, ulong, int?) Key { get; }

    public Side Side { get; }

    /// <summary>When you got here (a garden's plot changes as you walk: it's asked about once you stay).</summary>
    public DateTime Since { get; } = DateTime.UtcNow;

    /// <summary>The site's address of this home, once it's been read.</summary>
    public SiteHome? Address { get; set; }

    /// <summary>Why the address couldn't be read (after a few tries).</summary>
    public string? AddressWhy { get; set; }

    public int AddressTries { get; set; }

    public DateTime AddressAgain { get; set; }

    public bool Asking { get; set; }

    /// <summary>Your designs whose address is this home, once the site said (most recently changed first).</summary>
    public List<MyDesign>? Designs { get; set; }

    /// <summary>Why the site couldn't be asked.</summary>
    public string? Why { get; set; }

    public bool Verifying { get; set; }

    /// <summary>How verifying went, when it didn't.</summary>
    public string? VerifyNote { get; set; }

    /// <summary>The design this home is verified as (the most recently changed, if several are).</summary>
    public MyDesign? Verified => Designs?.FirstOrDefault(d => d.Verified);
}

/// <summary>A visit to a home's interior: whether it's a design on the site, and the panel's state.</summary>
public sealed class Visit
{
    public Visit((uint, ulong) key) => Key = key;

    public (uint, ulong) Key { get; }

    public DateTime EnteredAt { get; } = DateTime.UtcNow;

    public SiteHome? Address { get; set; }

    public string? AddressWhy { get; set; }

    public int AddressTries { get; set; }

    public DateTime AddressAgain { get; set; }

    public bool Asking { get; set; }

    /// <summary>Asked once this visit (again only when the token changes).</summary>
    public bool Asked { get; set; }

    /// <summary>The published designs verified as this home (three at most, the newest verified first).</summary>
    public List<HereDesign>? Designs { get; set; }

    public string? Why { get; set; }

    /// <summary>Which of them the panel shows.</summary>
    public int Shown { get; set; }

    /// <summary>Closed with its ×: not shown again this visit.</summary>
    public bool Dismissed { get; set; }

    /// <summary>The likes, saves and stamps under way ("design|act").</summary>
    public HashSet<string> Acting { get; } = new();

    /// <summary>How the last like, save or stamp went, when it didn't.</summary>
    public string? ActNote { get; set; }

    /// <summary>The shown design's cover, small, once loaded (the visit's to let go of).</summary>
    public IDalamudTextureWrap? Cover { get; set; }

    public string? CoverFor { get; set; }

    public HereDesign? Current => Designs is { Count: > 0 } d ? d[Math.Clamp(Shown, 0, d.Count - 1)] : null;
}

/// <summary>
/// Verified homes and visitors. In a home you may decorate (inside, or your garden), its address is
/// read once a visit and the site asked which of your designs it is (<c>hs_plugin_home</c>): This
/// house says so, offers Verify as … (<c>hs_plugin_verify</c>, inside only), or Post it. In any
/// home's interior (no permission needed), the site is asked once a visit which published designs
/// are verified as it (<c>hs_plugin_here</c>, with the token when linked, else as anyone); the
/// visitor panel (UI/VisitorPanel.cs) shows one, with like, save and stamp (<c>hs_plugin_act</c>).
/// </summary>
public sealed partial class GameLink
{
    /// <summary>A home's address is read again after a second, at most this often, while the game settles.</summary>
    public const int AddressTriesMost = 10;

    /// <summary>A visit's address waits this long after you're in (the house id is set as you arrive).</summary>
    private static readonly TimeSpan VisitSettle = TimeSpan.FromSeconds(2);

    /// <summary>The home you may decorate that you're in or on (its garden), or null.</summary>
    public HomeLook? Home { get; private set; }

    /// <summary>The home's interior you're visiting (any home), or null.</summary>
    public Visit? Here { get; private set; }

    private static (PlaceKind, uint, ulong, int?)? HomeKey(HousingContext ctx)
    {
        if (!ctx.HasPermission) return null;
        if (ctx.Kind == PlaceKind.Interior) return (ctx.Kind, ctx.TerritoryId, ctx.HouseId, (int?)null);
        if (ctx.Kind == PlaceKind.Ward && ctx.Plot != null) return (ctx.Kind, ctx.TerritoryId, 0UL, ctx.Plot);
        return null;
    }

    private void TickHome(HousingContext ctx, bool settled)
    {
        if (HomeKey(ctx) is not { } key || ctx.Side is not { } side)
        {
            Home = null;
            return;
        }
        if (Home == null || Home.Key != key) Home = new HomeLook(key, side);
        var h = Home;
        if (!Usable || !settled || ctx.Busy || runActive() || DateTime.UtcNow - h.Since < VisitSettle) return;
        if (h.Address == null)
        {
            if (h.AddressTries >= AddressTriesMost || DateTime.UtcNow < h.AddressAgain) return;
            h.AddressTries++;
            h.AddressAgain = DateTime.UtcNow + TimeSpan.FromSeconds(1);
            string why;
            h.Address = ctx.Kind == PlaceKind.Interior ? HomeReader.Interior(ctx, out why) : HomeReader.Garden(ctx, out why);
            h.AddressWhy = h.Address == null ? why : null;
            if (h.Address == null) return;
        }
        if (h.Designs == null && !h.Asking && h.Why == null) AskHome(h);
    }

    private void AskHome(HomeLook h)
    {
        if (h.Address is not { } home) return;
        h.Asking = true;
        var token = config.SiteToken;
        Work("looking for this home on the site", c => SiteLink.HomeAsync(http, token, home, c), r =>
        {
            h.Asking = false;
            if (r.Ok)
            {
                h.Designs = r.Value ?? new List<MyDesign>();
                h.Why = null;
                return;
            }
            if (r.TokenRefused) Refused(token, r.Why);
            h.Why = r.Why;
        }, Fault<List<MyDesign>>);
    }

    /// <summary>This house's "Try again": the address read again, the site asked again.</summary>
    public void RetryHome()
    {
        if (Home is not { } h) return;
        h.Why = null;
        h.Designs = null;
        if (h.Address == null)
        {
            h.AddressTries = 0;
            h.AddressWhy = null;
        }
    }

    /// <summary>Why Verify can't go now, or null.</summary>
    public string? CantVerify() =>
        !Usable ? "Link your Housing Studio account first"
        : runActive() ? "Not while a run is going"
        : Home is not { Side: Side.Interior, Address: not null } ? "Verify a home from inside it"
        : Home.Verifying ? "Verifying…"
        : null;

    /// <summary>Verify as …: tells the site this home is that design (its address must be this home's).</summary>
    public void Verify(MyDesign design)
    {
        if (CantVerify() != null || Home is not { Address: { } home } h) return;
        h.Verifying = true;
        h.VerifyNote = null;
        var token = config.SiteToken;
        Work("verifying this home", c => SiteLink.VerifyAsync(http, token, design.Id, home, true, c), r =>
        {
            h.Verifying = false;
            if (r.Ok)
            {
                log.Done($"Verified this home ({home.Describe()}) as “{design.Name}” on Housing Studio.");
                // Asked again, so This house says it from the site.
                h.Designs = null;
                h.Why = null;
                return;
            }
            if (r.TokenRefused) Refused(token, r.Why);
            h.VerifyNote = r.Why;
        }, Fault<MyDesign>);
    }

    // ---------------------------------------------------------------------------------------------
    // Visitors.

    private void TickVisit(HousingContext ctx, bool settled)
    {
        if (ctx.Kind != PlaceKind.Interior)
        {
            EndVisit();
            return;
        }
        var key = (ctx.TerritoryId, ctx.HouseId);
        if (Here == null || Here.Key != key)
        {
            EndVisit();
            Here = new Visit(key);
        }
        var v = Here;
        if (!config.ShowHomesWhenVisiting || v.Asked || v.Asking) return;
        if (!settled || ctx.Busy || DateTime.UtcNow - v.EnteredAt < VisitSettle) return;
        if (v.Address == null)
        {
            if (v.AddressTries >= AddressTriesMost || DateTime.UtcNow < v.AddressAgain) return;
            v.AddressTries++;
            v.AddressAgain = DateTime.UtcNow + TimeSpan.FromSeconds(1);
            v.Address = HomeReader.Interior(ctx, out var why);
            v.AddressWhy = v.Address == null ? why : null;
            if (v.Address == null)
            {
                if (v.AddressTries == AddressTriesMost) Services.Log.Debug($"Housing Studio: couldn't tell this home's address for the visitor panel: {why}");
                return;
            }
        }
        AskHere(v);
    }

    private void AskHere(Visit v)
    {
        if (v.Address is not { } home) return;
        v.Asking = true;
        var token = Usable ? config.SiteToken : string.Empty;
        Work("asking the site about this home", c => SiteLink.HereAsync(http, token, home, c), r =>
        {
            v.Asking = false;
            if (Here != v) return;
            // Linked or unlinked meanwhile: asked again with the token in hand now.
            if (token != (Usable ? config.SiteToken : string.Empty) && !r.TokenRefused) return;
            if (r.TokenRefused && token.Length > 0)
            {
                // Asked again as anyone next frame.
                Refused(token, r.Why);
                return;
            }
            v.Asked = true;
            if (!r.Ok)
            {
                v.Why = r.Why;
                Services.Log.Warning($"Housing Studio couldn't say whether this home is on the site: {r.Why}");
                return;
            }
            v.Designs = r.Value ?? new List<HereDesign>();
            v.Shown = 0;
            if (v.Current is { } d)
            {
                // Said once a visit, for someone else's design (your own home says so in the window).
                var other = v.Designs.FirstOrDefault(x => !x.Own);
                if (other != null && config.ShowHomesWhenVisiting)
                    Tell($"This home is on Housing Studio: {other.Name} by {other.Maker.Name}.", notification: false);
                LoadCover(v, d);
            }
        }, Fault<List<HereDesign>>);
    }

    /// <summary>The panel's ‹ ›: shows another of this home's designs.</summary>
    public void ShowDesign(int index)
    {
        if (Here is not { Designs.Count: > 0 } v) return;
        v.Shown = Math.Clamp(index, 0, v.Designs.Count - 1);
        v.ActNote = null;
        if (v.Current is { } d) LoadCover(v, d);
    }

    /// <summary>The panel's ×: gone for this visit.</summary>
    public void Dismiss()
    {
        if (Here is { } v) v.Dismissed = true;
    }

    private void EndVisit()
    {
        if (Here is not { } v) return;
        Release(v.Cover);
        v.Cover = null;
        v.CoverFor = null;
        Here = null;
    }

    /// <summary>Done (or under way) for this design?</summary>
    public static bool IsOn(HereDesign d, string act) => act switch
    {
        "like" => d.Social.Liked,
        "save" => d.Social.IsFavourite,
        "stamp" => d.Stamps.MineToday,
        _ => false,
    };

    /// <summary>Like, save or stamp the shown design (or take it back): your own token, the home you're in for a stamp.</summary>
    public void Act(HereDesign design, string act)
    {
        if (Here is not { } v || !Usable || design.Own) return;
        var busy = $"{design.Id}|{act}";
        if (!v.Acting.Add(busy)) return;
        v.ActNote = null;
        var on = !IsOn(design, act);
        var home = act == "stamp" ? v.Address : null;
        var token = config.SiteToken;
        Work($"the visitor panel's {act}", c => SiteLink.ActAsync(http, token, design.Id, act, on, home, c), r =>
        {
            v.Acting.Remove(busy);
            if (Here != v || v.Designs == null) return;
            if (r.Ok && r.Value is { } now)
            {
                v.Designs = v.Designs.Select(d => d.Id == design.Id ? d with { Social = now.Social, Stamps = now.Stamps } : d).ToList();
                if (act == "stamp" && on) log.Done($"Stamped “{design.Name}” from inside the home.");
                return;
            }
            if (r.TokenRefused) Refused(token, r.Why);
            v.ActNote = r.Why;
        }, Fault<Reactions>);
    }

    /// <summary>The shown design's cover (its small copy), loaded on a worker once; nothing when it can't be (it's only a picture).</summary>
    private void LoadCover(Visit v, HereDesign d)
    {
        if (d.Cover is not { } path || v.CoverFor == d.Id) return;
        Release(v.Cover);
        v.Cover = null;
        v.CoverFor = d.Id;
        var url = SiteLink.PhotoUrl(path, small: true);
        Work("loading a design's cover", c => Cover(url, c), wrap =>
        {
            if (wrap == null) return;
            if (Here != v || v.CoverFor != d.Id)
            {
                Release(wrap);
                return;
            }
            Release(v.Cover);
            v.Cover = wrap;
        }, _ => null);
    }

    /// <summary>The most a cover's small copy may weigh.</summary>
    private const int CoverBytes = 2 * 1024 * 1024;

    private async Task<IDalamudTextureWrap?> Cover(string url, CancellationToken cancel)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.OK || response.Content.Headers.ContentLength is > CoverBytes) return null;
            var bytes = await response.Content.ReadAsByteArrayAsync(timeout.Token).ConfigureAwait(false);
            if (bytes.Length is 0 or > CoverBytes) return null;
            return Own(await Services.Textures.CreateFromImageAsync(bytes, "Housing Studio cover", timeout.Token).ConfigureAwait(false));
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancel.IsCancellationRequested)
        {
            // A WebP the PC can't read, no connection: no picture, nothing else.
            Services.Log.Debug(ex, "Housing Studio couldn't load a design's cover");
            return null;
        }
    }
}
