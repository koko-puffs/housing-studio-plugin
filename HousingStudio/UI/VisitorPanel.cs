using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using HousingStudio.Core;
using HousingStudio.Session;
using static HousingStudio.UI.Theme;

namespace HousingStudio.UI;

/// <summary>
/// A small panel of its own (not the main window), in the design system's look: glass over ink with
/// the line edge and a 16px radius, no title bar, sized to what it holds, 18px inside. It doesn't
/// take focus when it appears, doesn't close on Esc, makes no sound, and isn't drawn in combat or
/// cutscenes (Dalamud hides it with the HUD and in Group Pose). Shown while <see cref="Wanted"/> says so.
/// </summary>
public abstract class SmallPanel : Window
{
    protected const float Width = 320;
    protected const float Pad = 18;

    protected readonly Fonts Fonts;
    private readonly Vector2 place;

    /// <param name="place">Where it first appears, as a fraction of the screen (its top-right corner).</param>
    protected SmallPanel(string name, Fonts fonts, Vector2 place)
        : base(name, ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse
                     | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNav | ImGuiWindowFlags.NoDocking)
    {
        Fonts = fonts;
        this.place = place;
        RespectCloseHotkey = false;
        ShowCloseButton = false;
        DisableWindowSounds = true;
        AllowPinning = false;
        AllowClickthrough = false;
        PositionCondition = ImGuiCond.FirstUseEver;
    }

    /// <summary>Whether it should show now (read every frame).</summary>
    protected abstract bool Wanted();

    protected abstract void Body();

    public override void PreOpenCheck() => IsOpen = Wanted();

    public override bool DrawConditions()
    {
        var c = Services.Condition;
        return !c[ConditionFlag.InCombat] && !c[ConditionFlag.WatchingCutscene] && !c[ConditionFlag.WatchingCutscene78]
               && !c[ConditionFlag.OccupiedInCutSceneEvent] && !c[ConditionFlag.BetweenAreas] && !c[ConditionFlag.BetweenAreas51];
    }

    private int colors;
    private int vars;

    public override void PreDraw()
    {
        // First time ever: on the right, out of the way (then where it was left; ImGui keeps it).
        var vp = ImGuiHelpers.MainViewport;
        Position = new Vector2(vp.Pos.X + vp.Size.X * place.X - S(Width), vp.Pos.Y + vp.Size.Y * place.Y);
        ImGui.PushStyleColor(ImGuiCol.WindowBg, Palette.GlassSolid);
        ImGui.PushStyleColor(ImGuiCol.Border, Palette.Line);
        ImGui.PushStyleColor(ImGuiCol.Text, Palette.Ivory);
        colors = 3;
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, S(Palette.RadiusXl));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 1f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        vars = 3;
    }

    public override void PostDraw()
    {
        if (vars > 0) ImGui.PopStyleVar(vars);
        if (colors > 0) ImGui.PopStyleColor(colors);
        vars = colors = 0;
    }

    public override void Draw()
    {
        Kit.ResetFrame();
        Theme.PushInside();
        var origin = ImGui.GetCursorScreenPos();
        Kit.PushBox(origin.X + S(Pad), S(Width - Pad * 2));
        try
        {
            using (Fonts.Push(Face.Sans, 14))
            {
                Kit.Reserve(origin, S(Width), S(Pad));
                Body();
                Kit.Gap(Pad);
            }
        }
        finally
        {
            Kit.PopBox();
            Theme.Pop();
        }
        // The full width, padding on the right included, for the auto size.
        var end = ImGui.GetCursorScreenPos();
        Kit.Reserve(new Vector2(origin.X, end.Y), S(Width), 0);
    }

    /// <summary>The panel's head: its eyebrow, and a × on the right; true when the × is pressed.</summary>
    protected static bool Head(string eyebrow, string closeTip)
    {
        var at = Kit.At;
        var w = Kit.Width;
        var size = S(28);
        var h = MathF.Max(size, Txt.Natural(Face.Mono, 12));
        Kit.Eyebrow(new Vector2(at.X, at.Y + (h - Txt.Natural(Face.Mono, 12)) / 2), eyebrow, Palette.Brass, w - size - S(8));
        var pressed = Kit.IconButton("##panel-close", new Vector2(at.X + w - size, at.Y), Icon.X, closeTip, 28, Btn.Quiet);
        Kit.Reserve(at, w, h);
        return pressed;
    }

    /// <summary>A picture across the panel, cut to <paramref name="maxHeight"/> from its middle, with rounded corners.</summary>
    protected static void Picture(IDalamudTextureWrap wrap, float maxHeight)
    {
        if (wrap.Width <= 0 || wrap.Height <= 0) return;
        var at = Kit.At;
        var w = Kit.Width;
        var aspect = (float)wrap.Width / wrap.Height;
        var h = MathF.Min(w / aspect, S(maxHeight));
        var boxAspect = w / h;
        Vector2 uv0 = Vector2.Zero, uv1 = Vector2.One;
        if (aspect > boxAspect)
        {
            var keep = boxAspect / aspect;
            uv0.X = (1 - keep) / 2;
            uv1.X = 1 - uv0.X;
        }
        else if (aspect < boxAspect)
        {
            var keep = aspect / boxAspect;
            uv0.Y = (1 - keep) / 2;
            uv1.Y = 1 - uv0.Y;
        }
        var dl = Kit.Dl;
        dl.AddRectFilled(at, at + new Vector2(w, h), Palette.U32(Palette.Photo), S(Palette.RadiusMd));
        dl.AddImageRounded(wrap.Handle, at, at + new Vector2(w, h), uv0, uv1, Palette.U32(Vector4.One), S(Palette.RadiusMd));
        dl.AddRect(at + new Vector2(0.5f), at + new Vector2(w, h) - new Vector2(0.5f), Palette.U32(Palette.LineFaint), S(Palette.RadiusMd), ImDrawFlags.None, 1f);
        Kit.Reserve(at, w, h);
    }

    /// <summary>A line of runs (wrapping) at the cursor; returns the link pressed.</summary>
    protected static string? Line(params Run[] runs) => Txt.Para(runs);
}

/// <summary>
/// The visitor panel (Settings → Getting, "Show homes on Housing Studio when I visit"): in a home's
/// interior that's a published design verified there (Session/GameLink.Homes.cs), "This home is on
/// Housing Studio", the design's cover, name, "by" its maker, Verified in game; Like, Save and Stamp
/// (stamps only while the site has them on; "Your design" for your own; not linked, a line to
/// Settings instead); Open its page; ‹ › between several; ×, gone for this visit. It goes when you
/// leave.
/// </summary>
public sealed class VisitorPanel : SmallPanel
{
    private readonly Configuration config;
    private readonly GameLink link;
    private readonly Action<SettingsTab> openSettings;

    public VisitorPanel(Configuration config, GameLink link, Fonts fonts, Action<SettingsTab> openSettings)
        : base("Housing Studio · this home###HousingStudioVisitor", fonts, new Vector2(0.97f, 0.22f))
    {
        this.config = config;
        this.link = link;
        this.openSettings = openSettings;
    }

    protected override bool Wanted() =>
        config.ShowHomesWhenVisiting && link.Here is { Dismissed: false, Current: not null };

    protected override void Body()
    {
        if (link.Here is not { Current: { } d } v) return;
        if (Head("This home is on Housing Studio", "Hide it for this visit")) link.Dismiss();
        Kit.Gap(12);
        if (v.Cover is { } cover && v.CoverFor == d.Id)
        {
            Picture(cover, 150);
            Kit.Gap(12);
        }

        var at = Kit.At;
        var w = Kit.Width;
        var nameH = Txt.Natural(Face.Serif, 22);
        Txt.Line(Kit.Dl, at, Txt.Fit(d.Name, Face.Serif, 22, w), Face.Serif, 22, Palette.Ivory);
        Kit.Reserve(at, w, nameH);
        Kit.Gap(2);
        Line(Run.Note($"by {d.Maker.Name}", Palette.Ash));
        Kit.Gap(4);
        var tick = Kit.At;
        Icons.Draw(Kit.Dl, Icon.Check, tick + new Vector2(0, S(2)), S(14), Palette.Success);
        Txt.Line(Kit.Dl, tick + new Vector2(S(14 + 6), 0), "Verified in game", Face.Sans, 13, Palette.Success, 19.5f);
        Kit.Reserve(tick, w, S(19.5f));

        Kit.Gap(14);
        if (d.Own)
        {
            Line(Run.Note("Your design", Palette.Dim));
            Kit.Gap(10);
        }
        else if (!link.Usable)
        {
            var words = link.TokenRefused ? "Housing Studio didn't accept your token: link again to like, save or stamp" : "Link your account to like, save or stamp";
            if (Line(Run.Note(words, Palette.BrassSoft) with { Link = "settings" }) == "settings") openSettings(SettingsTab.Help);
            Kit.Gap(10);
        }
        else
        {
            var row = new Kit.Wrap(8);
            var bh = Kit.ButtonHeight(BtnSize.Tiny);
            ActButton(ref row, bh, v, d, "like", Icon.Heart, d.Social.Liked ? "Liked" : "Like", d.Social.Likes,
                d.Social.Liked ? "Take your like back" : "Like it on Housing Studio");
            ActButton(ref row, bh, v, d, "save", Icon.Bookmark, d.Social.IsFavourite ? "Saved" : "Save", d.Social.Favourites,
                d.Social.IsFavourite ? "Take it out of your favourites" : "Keep it in your favourites on Housing Studio");
            if (d.Stamps.On)
                ActButton(ref row, bh, v, d, "stamp", Icon.Stamp, d.Stamps.MineToday ? "Stamped" : "Stamp", d.Stamps.Total,
                    d.Stamps.MineToday ? "Take today's stamp back" : "Stamp its postcard, from inside the home (one a day)");
            row.End();
            Kit.Gap(10);
        }
        if (v.ActNote is { } note)
        {
            Line(Run.Note(note, Palette.Danger));
            Kit.Gap(10);
        }

        var bottom = new Kit.Wrap(8);
        var tiny = Kit.ButtonHeight(BtnSize.Tiny);
        const string open = "Open its page";
        if (Kit.Button("##visit-open", bottom.Next(Kit.ButtonWidth(open, BtnSize.Tiny, Icon.ExternalLink), tiny), open, Btn.Glass, BtnSize.Tiny, Icon.ExternalLink, 0, false,
                "Its page on Housing Studio, in your browser"))
            Dalamud.Utility.Util.OpenLink(SiteLink.DesignUrl(d.Id));
        if (v.Designs is { Count: > 1 } all)
        {
            var count = $"{v.Shown + 1} of {all.Count}";
            var countW = Txt.Measure(count, Face.Mono, 12).X;
            var pager = bottom.Next(S(30) * 2 + S(8) * 2 + countW, tiny);
            if (Kit.IconButton("##visit-prev", pager, Icon.ChevronLeft, "The one before", 30, Btn.Quiet, v.Shown == 0)) link.ShowDesign(v.Shown - 1);
            Txt.Line(Kit.Dl, pager + new Vector2(S(30 + 8), 0), count, Face.Mono, 12, Palette.Dim, 30);
            if (Kit.IconButton("##visit-next", pager + new Vector2(S(30 + 8 + 8) + countW, 0), Icon.ChevronRight, "The next one", 30, Btn.Quiet, v.Shown >= all.Count - 1))
                link.ShowDesign(v.Shown + 1);
        }
        bottom.End();
    }

    private void ActButton(ref Kit.Wrap row, float bh, Visit v, HereDesign d, string act, Icon icon, string word, int count, string tip)
    {
        var busy = v.Acting.Contains($"{d.Id}|{act}");
        var label = count > 0 ? $"{word} · {count}" : word;
        var on = GameLink.IsOn(d, act);
        if (Kit.Button($"##visit-{act}", row.Next(Kit.ButtonWidth(label, BtnSize.Tiny, icon, on ? Btn.Picked : Btn.Glass), bh), label,
                on ? Btn.Picked : Btn.Glass, BtnSize.Tiny, icon, 0, busy, tip))
            link.Act(d, act);
    }
}
