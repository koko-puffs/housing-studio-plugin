using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using Dalamud.Bindings.ImGui;
using HousingStudio.Automation;
using HousingStudio.Core;
using HousingStudio.Game;
using HousingStudio.UI.Model;
using static HousingStudio.UI.Theme;

namespace HousingStudio.UI;

/// <summary>Settings' tabs (the 0.6 flow): what Start does, getting what's missing, the Beta features, and help.</summary>
public enum SettingsTab
{
    Placing,
    Getting,
    Beta,
    Help,
}

/// <summary>
/// Settings (the 0.6 flow, docs/mockups/flow.html), from the header: four tabs as small pills under the
/// title, Placing (what Start does, the pace, markers, and More options), Getting (market prices, the
/// errands' limits, the Housing Studio account and what it brings from the site), Beta (the experimental features' switches) and Help
/// (step by step, the recorder, the place words, the windows and clicks, trusting the errands' new
/// clicks). Each setting is one row: its name, one line, the control on the right; the longer words
/// are its tooltip. The version and What's new at the foot.
/// </summary>
public sealed partial class MainWindow
{
    private const string ReleasesUrl = "https://github.com/koko-puffs/housing-studio-plugin/releases";

    /// <summary>The tab shown, kept while the window is open (Placing when it opens again).</summary>
    private SettingsTab settingsTab;

    private bool moreOpen;
    private bool callsOpen;
    private string? placeWords;

    /// <summary>The settings under More options (shown on its row).</summary>
    private const int MoreCount = 12;

    private const int GettingRows = 7;
    private const int BetaRows = 5;

    private static readonly TabSpec[] SettingsTabs =
    {
        new("Placing"),
        new("Getting", GettingRows.ToString()),
        new("Beta", BetaRows.ToString()),
        new("Help"),
    };

    private static readonly string[] SettingsBodies = { "##settings-placing", "##settings-getting", "##settings-beta", "##settings-help" };

    /// <summary>Opens Settings on Placing (Dalamud's cog, /hstudio settings); during a run, the run.</summary>
    public void ShowSettings() => ShowSettings(SettingsTab.Placing);

    /// <summary>Opens Settings on a tab (the errands' "Limits · Change" opens Getting); during a run, the run.</summary>
    public void ShowSettings(SettingsTab tab)
    {
        settingsTab = tab;
        sub = runner.Active || Plugin.FixtureRun.Active ? Sub.None : Sub.Settings;
        IsOpen = true;
    }

    /// <summary>In the window, without opening it: Settings on a tab.</summary>
    private void OpenSettings(SettingsTab tab)
    {
        settingsTab = tab;
        sub = Sub.Settings;
    }

    public override void OnClose()
    {
        settingsTab = SettingsTab.Placing;
        moreOpen = false;
    }

    private void DrawSettings()
    {
        Frame.BeginHeader(20);
        var at = Kit.At;
        var w = Kit.Width;
        Title(new Vector2(at.X, at.Y + (S(40) - S(32 * 1.1f)) / 2), "Settings", null, w - S(56));
        if (Kit.IconButton("##back", new Vector2(at.X + w - S(40), at.Y), Icon.ArrowLeft, session.Layout != null ? $"Back to {session.Title}" : "Back"))
            sub = Sub.None;
        Kit.Reserve(at, w, S(40));
        Kit.Gap(14);
        var tab = (int)settingsTab;
        if (tab < 0 || tab >= SettingsTabs.Length) tab = 0;
        settingsTab = (SettingsTab)Kit.Tabs("##settings-tab", SettingsTabs, tab);
        Frame.EndHeader();

        Frame.BeginBody(SettingsBodies[(int)settingsTab], null, 16, 22);
        switch (settingsTab)
        {
            case SettingsTab.Getting:
                DrawGettingTab();
                break;
            case SettingsTab.Beta:
                DrawBetaTab();
                break;
            case SettingsTab.Help:
                DrawHelpTab();
                break;
            default:
                DrawPlacingTab();
                break;
        }

        Frame.BeginBottom("settings-bottom");
        if (versionRuns == null)
        {
            var v = typeof(MainWindow).Assembly.GetName().Version;
            versionRuns = new[]
            {
                new Run(v == null ? "Housing Studio · " : $"Housing Studio {v.Major}.{v.Minor}.{v.Build} · ", Face.Sans, 13, Palette.Dim),
                new Run("What's new", Face.Sans, 13, Palette.Ivory, Link: "news"),
            };
        }
        if (Txt.Para(versionRuns, 19.5f, center: true) == "news") Dalamud.Utility.Util.OpenLink(ReleasesUrl);
        Frame.EndBottom();
        Frame.EndBody();
    }

    private Run[]? versionRuns;

    private static readonly Run[] SetNote = new Run[1];

    /// <summary>
    /// A setting's row (11px 14px inside, the faint line between): its name (with an icon before it
    /// and the Beta tag after it, when given) and one line under it, the control on the right; the
    /// longer words are the tooltip over the words. <paramref name="below"/> puts the control under
    /// the words instead, for wide ones. <paramref name="disabled"/> dims the words (the control is
    /// the caller's).
    /// </summary>
    private static void SetRow(int index, string name, string? note, float controlW, float controlH, Action<Vector2> control, bool below = false,
        string? tip = null, bool beta = false, bool disabled = false, Icon? icon = null)
    {
        var at = Kit.At;
        var w = Kit.Width;
        if (index > 0) Kit.Divider(at.X, at.X + w, at.Y - 0.5f);
        var x = at.X + S(14);
        var wordsW = below || controlW <= 0 ? w - S(28) : w - S(28) - controlW - S(12);
        var nameH = Txt.Natural(Face.Sans, 14);
        if (note != null) SetNote[0] = new Run(note, Face.Sans, 12, Palette.Dim);
        var noteH = note != null ? Txt.Flow(Vector2.Zero, wordsW, SetNote, 18, out _, draw: false) : 0;
        var wordsH = nameH + (note != null ? S(1) + noteH : 0);
        var h = below ? S(11) * 2 + wordsH + (controlH > 0 ? S(10) + controlH : 0) : S(11) * 2 + MathF.Max(wordsH, controlH);
        var dl = Kit.Dl;
        var top = below ? at.Y + S(11) : at.Y + (h - wordsH) / 2;
        var old = Palette.Opacity;
        if (disabled) Palette.Opacity = old * 0.5f;
        var nx = x;
        if (icon is { } ic)
        {
            Icons.Draw(dl, ic, new Vector2(nx, top + (nameH - S(14)) / 2), S(14), Palette.Brass);
            nx += S(14 + 6);
        }
        var nameW = x + wordsW - nx - (beta ? S(6) + Kit.BetaTagWidth : 0);
        nx += Txt.Line(dl, new Vector2(nx, top), Txt.Fit(name, Face.Sans, 14, nameW), Face.Sans, 14, Palette.Ivory);
        if (beta) Kit.BetaTag(new Vector2(nx + S(6), top + (nameH - Kit.BetaTagHeight) / 2));
        if (note != null)
        {
            SetNote[0] = new Run(note, Face.Sans, 12, Palette.Dim);
            Txt.Flow(new Vector2(x, top + nameH + S(1)), wordsW, SetNote, 18, out _);
        }
        Palette.Opacity = old;
        if (tip != null && !Kit.Blocked && ImGui.IsWindowHovered() && ImGui.IsMouseHoveringRect(new Vector2(x, top), new Vector2(x + wordsW, top + wordsH)))
            Kit.Tooltip(tip);
        if (below) control(new Vector2(x, top + wordsH + S(10)));
        else if (controlW > 0) control(new Vector2(at.X + w - S(14) - controlW, at.Y + (h - controlH) / 2));
        Kit.Reserve(at, w, h);
    }

    /// <summary>A setting with a switch; true when it's flipped.</summary>
    private static bool SwitchRow(int index, string id, string name, string? note, bool on, string? tip = null, bool disabled = false, bool beta = false)
    {
        var flipped = false;
        SetRow(index, name, note, S(40), S(24), p => flipped = Kit.Switch(id, p, on, disabled), false, tip, beta, disabled);
        return flipped;
    }

    /// <summary>A setting with a − value + stepper; returns −1, 0 or +1.</summary>
    private static int StepperRow(int index, string id, string name, string? note, string value, bool canDown, bool canUp, string? tip = null)
    {
        var delta = 0;
        SetRow(index, name, note, Kit.StepperWidth(value), S(40), p => delta = Kit.Stepper(id, p, value, canDown, canUp), false, tip);
        return delta;
    }

    /// <summary>A setting with a small button on the right; true when it's pressed.</summary>
    private static bool ButtonRow(int index, string id, string name, string? note, string label, Btn style = Btn.Glass, bool disabled = false,
        string? buttonTip = null, string? tip = null)
    {
        var pressed = false;
        SetRow(index, name, note, Kit.ButtonWidth(label, BtnSize.Tiny, null, style), Kit.ButtonHeight(BtnSize.Tiny),
            p => pressed = Kit.Button(id, p, label, style, BtnSize.Tiny, null, 0, disabled, buttonTip), false, tip);
        return pressed;
    }

    // ---------------------------------------------------------------------------------------------
    // Placing.

    private void DrawPlacingTab()
    {
        Kit.BeginPanel("##set-placing", 0, 0, 0);
        var i = 0;
        var both = config.StoreroomAutomatic && config.BagsAutomatic;
        var placeNote = both || (!config.StoreroomAutomatic && !config.BagsAutomatic)
            ? "From the storeroom and your bags"
            : config.StoreroomAutomatic ? "From the storeroom; your bags' you put down" : "From your bags; the storeroom's you put down";
        if (SwitchRow(i++, "##place-for-me", "Place pieces for me", placeNote, both,
                "Picks each piece from the storeroom or your bags and places it. Off: you put each one down, anywhere, and it's moved to its spot. More options sets the storeroom and bags apart."))
        {
            config.StoreroomAutomatic = config.BagsAutomatic = !both;
            config.Save();
        }
        if (SwitchRow(i++, "##dye", "Dye after placing", "Uses dyes from your bags", config.DyeAfterPlacing,
                "Each piece whose dye isn't the design's is dyed once it's placed. Dyeing uses up the dye, as it does by hand."))
        {
            config.DyeAfterPlacing = !config.DyeAfterPlacing;
            config.Save();
        }
        if (SwitchRow(i++, "##backup", "Keep a backup", "So a run can be undone", config.BackupBeforePlacing,
                "What's placed is saved as a .build before each run, so Done's Put them back can bring it back. A swap always keeps one."))
        {
            config.BackupBeforePlacing = !config.BackupBeforePlacing;
            config.Save();
        }
        var pace = (int)config.Pace;
        var room = Kit.Width - S(28) - S(12) - S(130);
        var paceW = Kit.SegmentedWidth(PaceSetting.Labels, room);
        var paceBelow = paceW > room;
        if (paceBelow) paceW = Kit.SegmentedWidth(PaceSetting.Labels, Kit.Width - S(28));
        SetRow(i++, "Pace", PaceSetting.Short(config.Pace), paceW, S(40), p =>
        {
            var picked = Kit.Segmented("##pace", p, PaceSetting.Labels, pace, 0, paceW);
            if (picked != pace)
            {
                config.Pace = (Pace)picked;
                config.Save();
            }
        }, paceBelow, PaceSetting.Note(config.Pace));
        if (SwitchRow(i++, "##markers", "Show where pieces go", "Markers in the world", config.ShowMarkers,
                "Markers in your home where each piece goes, while a design is loaded. More options sets their range and labels."))
        {
            config.ShowMarkers = !config.ShowMarkers;
            config.Save();
        }

        // More options ›: the rest, under it in the same panel.
        var at = Kit.At;
        var count = MoreCount.ToString();
        var chevron = moreOpen ? Icon.ChevronDown : Icon.ChevronRight;
        var cw = Txt.Measure(count, Face.Mono, 12).X;
        SetRow(i++, "More options", "Place anywhere, floors, markers…", cw + S(6 + 14), S(20), p =>
        {
            Txt.Line(Kit.Dl, p, count, Face.Mono, 12, Palette.Ash, 20);
            Icons.Draw(Kit.Dl, chevron, new Vector2(p.X + cw + S(6), p.Y + S(3)), S(14), Palette.Ash);
        });
        var rowH = ImGui.GetCursorScreenPos().Y - at.Y;
        var (clicked, hovered, _) = Kit.Hit("##more-row", at, new Vector2(Kit.Width, rowH));
        if (hovered) Kit.RowFill(at, at + new Vector2(Kit.Width, rowH), Palette.RowHover, false, !moreOpen);
        Kit.FocusRing(at + new Vector2(S(4)), at + new Vector2(Kit.Width, rowH) - new Vector2(S(4)), S(10));
        if (clicked) moreOpen = !moreOpen;
        if (moreOpen) DrawMoreOptions(i);
        Kit.EndPanel();
    }

    private void DrawMoreOptions(int i)
    {
        if (SwitchRow(i++, "##anywhere", "Place anywhere", "Floating, sunk and overlapping pieces too", config.PlaceAnywhere,
                "Lets floating, sunk and overlapping pieces be set (the MakePlace plugin's option). The game's own check is off only while a run lasts."))
        {
            config.PlaceAnywhere = !config.PlaceAnywhere;
            config.Save();
        }

        var jitter = (int)MathF.Round(config.Jitter * 100);
        var d = StepperRow(i++, "##jitter", "Pace variation", "How much the pause between pieces varies", $"±{jitter}%", jitter > 0, jitter < 50,
            "So it never keeps an exact beat.");
        if (d != 0)
        {
            config.Jitter = Math.Clamp(jitter + d * 5, 0, 50) / 100f;
            config.Save();
        }

        var failures = config.MaxFailuresInARow;
        d = StepperRow(i++, "##failures", "When to stop", "After this many pieces in a row don't work", failures.ToString(), failures > 1, failures < 10);
        if (d != 0)
        {
            config.MaxFailuresInARow = Math.Clamp(failures + d, 1, 10);
            config.Save();
        }

        if (SwitchRow(i++, "##chat", "Say what's needed in chat", "When the run pauses, stops or waits for you", config.ChatMessages))
        {
            config.ChatMessages = !config.ChatMessages;
            config.Save();
        }

        var reach = (int)MathF.Round(config.NearMeRadius);
        d = StepperRow(i++, "##near-reach", "How far Near me reaches", "While Near me is on", $"{reach} yalms", reach > 2, reach < 40,
            "Pieces lists, and Start places, only what's this close to you while Near me (in Pieces) is on.");
        if (d != 0)
        {
            config.NearMeRadius = Math.Clamp(reach + d, 2, 40);
            config.Save();
        }

        if (SwitchRow(i++, "##storeroom-auto", "Place from the storeroom for me", "Off: you put them down", config.StoreroomAutomatic,
                "Picks each in the furnishing list's Storeroom tab and chooses Place. Off: you put them down. Gardens: always you."))
        {
            config.StoreroomAutomatic = !config.StoreroomAutomatic;
            config.Save();
        }
        if (SwitchRow(i++, "##bags-auto", "Place from your bags for me", "Off: you put them down", config.BagsAutomatic,
                "Opens the item's menu and chooses Place. Off: you put them down."))
        {
            config.BagsAutomatic = !config.BagsAutomatic;
            config.Save();
        }

        var floorsW = FloorLabels.Sum(f => Kit.ButtonWidth(f, BtnSize.Tiny)) + S(8) * 2;
        var floorsBelow = floorsW > Kit.Width - S(28) - S(12) - S(130);
        SetRow(i++, "Floors to place", "Start and Pieces use only these", floorsW, Kit.ButtonHeight(BtnSize.Tiny), p =>
        {
            var x = p.X;
            for (var f = 0; f < FloorLabels.Length; f++)
            {
                var label = FloorLabels[f];
                var on = f switch { 0 => config.ShowBasement, 1 => config.ShowGround, _ => config.ShowUpper };
                if (Kit.Button($"##floor-pick{f}", new Vector2(x, p.Y), label, on ? Btn.Picked : Btn.Glass, BtnSize.Tiny))
                {
                    switch (f)
                    {
                        case 0: config.ShowBasement = !on; break;
                        case 1: config.ShowGround = !on; break;
                        default: config.ShowUpper = !on; break;
                    }
                    config.Save();
                }
                x += Kit.ButtonWidth(label, BtnSize.Tiny) + S(8);
            }
        }, floorsBelow, "Start places, and Pieces lists, only the floors picked here.");

        if (SwitchRow(i++, "##labels", "Name a marker", "When the mouse is over it", config.ShowLabels))
        {
            config.ShowLabels = !config.ShowLabels;
            config.Save();
        }
        if (SwitchRow(i++, "##placed-markers", "Mark pieces in place too", "Not only the ones still to do", config.ShowPlacedMarkers))
        {
            config.ShowPlacedMarkers = !config.ShowPlacedMarkers;
            config.Save();
        }
        var range = (int)MathF.Round(config.MarkerRange);
        d = StepperRow(i++, "##marker-range", "How far markers show", "Further ones are hidden", $"{range} yalms", range > 5, range < 120);
        if (d != 0)
        {
            config.MarkerRange = Math.Clamp(range + d * 5, 5, 120);
            config.Save();
        }

        var cant = session.Context.Side == null ? "Go to your home first"
            : !session.Context.HasPermission ? "You can save a home you're allowed to decorate" : null;
        if (ButtonRow(i++, "##save-build", "Save what's placed as a build", "As Start's Save as a build", "Save a build", Btn.Glass, cant != null, cant,
                "A .build you can load here, open in MakePlace, or upload to Housing Studio."))
            SaveDialog();

        // Good to know: not a setting, but it belongs with them.
        SetRow(i, "Good to know", "Placing binds pieces to you; plugins like this are against the game's terms", 0, 0, _ => { }, false,
            "Placing binds a piece to you, as placing it by hand does, and dyeing uses up the dye. Plugins that act in the game for you are against Square Enix's terms of service; " +
            "this one only works on your own furniture in a home you may decorate, through the game's own windows, at the pace you set. The choice to use it is yours.");
    }

    private static readonly string[] FloorLabels = { "Basement", "Ground floor", "Upper floor" };

    // ---------------------------------------------------------------------------------------------
    // Getting: market prices, the errands' limits, the Housing Studio account.

    private static readonly string[] HubLabels = { "Limsa Lominsa", "Ul'dah" };

    private void DrawGettingTab()
    {
        Kit.BeginPanel("##set-getting", 0, 0, 0);
        var i = 0;
        if (SwitchRow(i++, "##market-prices", "Market prices", "Asks Universalis for your data centre", config.LookUpMarketPrices,
                "For what no vendor sells, the shopping list asks universalis.app over the internet what your data centre's market board wants."))
        {
            config.LookUpMarketPrices = !config.LookUpMarketPrices;
            config.Save();
        }
        const string errands = " Used by Get everything for me (Beta).";
        var d = StepperRow(i++, "##errand-ceiling", "Market ceiling", "The most errands spend there", ErrandWords.Gil(config.ErrandsMarketCeiling),
            config.ErrandsMarketCeiling > 0, config.ErrandsMarketCeiling < ErrandWords.Ceilings[^1],
            "The most the errands may spend on the market board in all; 0 buys nothing there." + errands);
        if (d != 0)
        {
            config.ErrandsMarketCeiling = ErrandWords.StepCeiling(config.ErrandsMarketCeiling, d);
            config.Save();
        }
        if (SwitchRow(i++, "##errand-worlds", "Other worlds", "Buy across your data centre", config.ErrandsOtherWorlds,
                "The other worlds of your data centre (Lifestream visits them). Off: your home world only." + errands))
        {
            config.ErrandsOtherWorlds = !config.ErrandsOtherWorlds;
            config.Save();
        }
        // The city for the market board, the summoning bells and world visits.
        var hubNow = config.ErrandsHub == 1 ? 1 : 0;
        SetRow(i++, "Market city", "For the market board, the bells and world visits", Kit.SegmentedWidth(HubLabels), S(40), p =>
        {
            var picked = Kit.Segmented("##settings-errand-hub", p, HubLabels, hubNow, 0, Kit.Width - S(40));
            if (picked != hubNow)
            {
                config.ErrandsHub = picked;
                config.Save();
            }
        }, below: true);
        if (SwitchRow(i++, "##errand-ride", "Ride and sprint", "Mount up where the zone allows, else sprint", config.ErrandsRide,
                "For walks of 35 yalms or more, the mount roulette where the zone lets you ride (not a city's streets or inside); Sprint otherwise, again whenever it's ready. Off: walks." + errands))
        {
            config.ErrandsRide = !config.ErrandsRide;
            config.Save();
        }
        var spare = config.ErrandsSpareSlots;
        d = StepperRow(i++, "##errand-spare", "Spare bag slots", "Each trip leaves these empty", spare.ToString(), spare > 0, spare < 20,
            "Bag slots the errands leave free, for a stack that doesn't join one already there or a quest item." + errands);
        if (d != 0)
        {
            config.ErrandsSpareSlots = Math.Clamp(spare + d, 0, 20);
            config.Save();
        }
        // Which home "go home" is: here too, as the errands can't open their screen while it's unknown.
        var here = ErrandGame.HomeHere(session.Context);
        var homeNow = config.ErrandsHome is >= 0 and <= 2 ? config.ErrandsHome : here != null ? (int)here.Kind : 0;
        SetRow(i++, "Home is", here is { Owned: true } && config.ErrandsHome < 0 ? "As the game says for this home" : "Where \"go home\" takes you (Lifestream)",
            Kit.SegmentedWidth(HomeLabels), S(40), p =>
            {
                var picked = Kit.Segmented("##settings-errand-home", p, HomeLabels, homeNow, 0, Kit.Width - S(40));
                if (picked != homeNow)
                {
                    config.ErrandsHome = picked;
                    config.Save();
                }
            }, below: true);
        DrawAccountRow(i++);
        DrawGameLinkRows(i);
        Kit.EndPanel();
    }

    // ---------------------------------------------------------------------------------------------
    // Beta: the experimental features. One switch lets them be used; each has its own under it. A
    // feature checks Configuration.Experimental and its own switch both.

    private void DrawBetaTab()
    {
        Kit.BeginPanel("##set-beta", 0, 0, 0);
        var i = 0;
        if (SwitchRow(i++, "##experimental", "Experimental features", "Lets the Beta features below be used", config.Experimental,
                "Features not yet tried in the game, or that act further on your behalf. Keep the backup on, and send the log if anything goes wrong."))
        {
            config.Experimental = !config.Experimental;
            config.Save();
        }
        var off = !config.Experimental;
        if (SwitchRow(i++, "##errands", "Get everything for me", "Retainers, vendors, the board, then home", config.ErrandsForYou,
                "Shopping and Ready get \"Get everything for me\": it fetches what's missing from your retainers, vendors and the market board (vnavmesh and Lifestream walk and travel), then places it. You see the gil first.",
                off, true))
        {
            config.ErrandsForYou = !config.ErrandsForYou;
            config.Save();
        }
        if (SwitchRow(i++, "##get-first", "Get things first from Ready", "Ready's button gets, then places", config.GetFirst,
                "Ready's Get stage, \"Get them for me first\": the brass button opens the errands, which get what's missing and then place it.",
                !Plugin.Errands.Enabled, true))
        {
            config.GetFirst = !config.GetFirst;
            config.Save();
        }
        if (SwitchRow(i++, "##set-fixtures", "Set the fixtures for me", "From your bags, step by step", config.SetFixturesForYou,
                "Ready's Finish stage and Done get \"Set them\": the game's fixtures window is opened and each fixture put in from your bags, step by step. You press its apply.",
                off, true))
        {
            config.SetFixturesForYou = !config.SetFixturesForYou;
            config.Save();
        }
        if (SwitchRow(i++, "##fixtures-after", "Set fixtures after placing", "When placing from Ready finishes", config.FixturesAfterPlacing,
                "Ready's Finish stage, \"Set them for me after placing\": when a placing run started from Ready (or the errands' placing) finishes and fixtures are left, setting them starts by itself.",
                !Plugin.FixtureRun.Enabled, true))
        {
            config.FixturesAfterPlacing = !config.FixturesAfterPlacing;
            config.Save();
        }
        if (SwitchRow(i++, "##putback-changes", "Put back what changed", "Since your last run", config.PutBackChanges,
                "After Start's check for changes: moves the pieces moved since your last run back, and places the ones taken away. Pieces added since stay.",
                off, true))
        {
            config.PutBackChanges = !config.PutBackChanges;
            config.Save();
        }
        Kit.EndPanel();
    }

    // ---------------------------------------------------------------------------------------------
    // Help: something not working?

    private (Dalamud.Game.ClientLanguage Language, string? Words) placeBuiltIn;
    private System.Collections.Generic.List<UiCall>? calls;
    private int unverifiedCalls;

    private void DrawHelpTab()
    {
        Kit.BeginPanel("##set-help", 0, 0, 0);
        var i = 0;
        if (SwitchRow(i++, "##step", "Step by step", "Hold before every click", config.StepByStep,
                "Holds before every click and shows what it's about to do, with the window and values, until you press Continue (or /hstudio continue). Use it for your first run."))
        {
            config.StepByStep = !config.StepByStep;
            config.Save();
        }
        if (SwitchRow(i++, "##move-preview", "The old preview method", "Move the preview before Place", config.MovePreviewBeforePlace,
                "Moves the game's preview of a storeroom piece to its spot before choosing Place. Off: the piece is placed where the game puts it, then moved to its spot."))
        {
            config.MovePreviewBeforePlace = !config.MovePreviewBeforePlace;
            config.Save();
        }

        // The recorder.
        var recording = recorder.Recording;
        var recNote = recording ? $"Recording · {Words.Count(recorder.LineCount, "line")} so far" : recorder.SetupError ?? "For a bug report";
        const string recTip = "While on, it writes down every game window that opens or closes, every click's values and the housing state. "
                              + "Turn it on, place one piece from your bags and one from the storeroom, dye one piece, by hand; turn it off, copy, and send the text along.";
        if (ButtonRow(i++, recording ? "##rec-stop" : "##rec-start", "Record what I do", recNote, recording ? "Stop" : "Start", recording ? Btn.Danger : Btn.Glass,
                false, null, recTip))
        {
            if (recording) OnFramework(recorder.Stop);
            else OnFramework(recorder.Start);
        }
        if (ButtonRow(i++, "##rec-copy", "Copy the recording and log", "To send along with what went wrong", "Copy", Btn.Glass,
                recorder.LineCount == 0 && log.Lines.Count == 0))
        {
            ImGui.SetClipboardText(Diagnostics());
            Notify("Recording and log copied");
        }
        if (recording && ButtonRow(i++, "##rec-dump", "Write down the windows' values", "Every value they hold now", "Write down", Btn.Glass, false, null,
                "Adds every value the furnishing list, the dye window and the menus hold right now (open the Storeroom tab first)."))
            OnFramework(recorder.DumpWindows);

        placeWords ??= config.PlaceMenuWords;
        var lang = Services.ClientState.ClientLanguage;
        if (placeBuiltIn.Words == null || placeBuiltIn.Language != lang)
            placeBuiltIn = (lang, string.Join(", ", UiTable.PlaceLabels(lang)));
        var width = Kit.Width - S(28);
        SetRow(i++, "The \"Place\" words", $"Yours: {placeBuiltIn.Words}", width, S(40), p =>
            {
                var text = placeWords;
                if (Kit.Field("##placewords", p, MathF.Min(width, S(360)), ref text, "e.g. Place"))
                {
                    placeWords = text;
                    config.PlaceMenuWords = text.Trim();
                }
                // Saved once you're done typing, not every key (the whole config is written each time).
                if (ImGui.IsItemDeactivatedAfterEdit()) config.Save();
            }, below: true,
            tip: $"The \"Place\" entry in an item's menu: {(Game.GameWords.PlaceFromGame(lang) ? "the game's own, in your client's language" : "built in for your client")}. "
                 + "If your menu says something else, type it here (several, comma-separated).");

        if (SwitchRow(i++, "##errand-trust", "Trust the new clicks", "Stop holding the ones not yet seen working", config.ErrandsTrustGuesses,
                "Get everything for me (Beta): off, each click not yet seen working (buying, a retainer's menu) waits for Continue, so you can watch it. On once you have."))
        {
            config.ErrandsTrustGuesses = !config.ErrandsTrustGuesses;
            config.Save();
        }

        // The windows and clicks the run uses, each with where its values came from.
        if (calls == null)
        {
            calls = UiTable.All().ToList();
            unverifiedCalls = calls.Count(c => !c.Verified);
        }
        var at = Kit.At;
        Kit.Divider(at.X, at.X + Kit.Width, at.Y - 0.5f);
        var count = calls.Count.ToString();
        Disclosure("##calls-row", ref callsOpen, "Windows and clicks used",
            unverifiedCalls > 0 ? $"{unverifiedCalls} not yet seen in a recording, in brass" : "Every one seen in a recording",
            11, Txt.Measure(count, Face.Mono, 12).X, Txt.Natural(Face.Mono, 12),
            (p, _) => Txt.Draw(Kit.Dl, p, count, Face.Mono, 12, Palette.Dim), false, Palette.Dim);
        if (callsOpen)
        {
            Kit.PushBox(Kit.Left + S(20), Kit.Width - S(40));
            foreach (var call in calls)
            {
                Txt.Para(new[]
                {
                    new Run(call.Describe(), Face.Mono, 12, call.Verified ? Palette.Text : Palette.BrassSoft),
                    new Run("  · " + call.Source, Face.Sans, 12, Palette.Dim),
                }, 12 * 1.6f);
                Kit.Gap(6);
            }
            Kit.PopBox();
            Kit.Gap(10);
        }
        Kit.EndPanel();
    }

    private string Diagnostics()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Housing Studio {typeof(MainWindow).Assembly.GetName().Version} · client {Services.ClientState.ClientLanguage}");
        sb.AppendLine($"Where: {session.Context}");
        sb.AppendLine($"Now: {UiRecorder.DescribeState()}");
        sb.AppendLine($"Settings: storeroom auto {config.StoreroomAutomatic}, bags auto {config.BagsAutomatic}, dyes {config.DyeAfterPlacing}, step by step {config.StepByStep}, " +
                      $"move preview {config.MovePreviewBeforePlace}, place words \"{config.PlaceMenuWords}\", pace {config.Pace} ±{config.Jitter:P0}");
        var (known, rows) = runner.StoreroomRowsKnown;
        sb.AppendLine($"Storeroom list rows known: {known} of {rows}");
        sb.AppendLine();
        sb.AppendLine("== Activity log ==");
        sb.AppendLine(log.AsText());
        sb.AppendLine("== Recording ==");
        sb.AppendLine(recorder.AsText());
        return sb.ToString();
    }
}
