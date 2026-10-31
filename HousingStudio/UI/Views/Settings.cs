using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using Dalamud.Bindings.ImGui;
using HousingStudio.Automation;
using HousingStudio.Core;
using HousingStudio.UI.Model;
using static HousingStudio.UI.Theme;

namespace HousingStudio.UI;

/// <summary>
/// Settings (mockup 6), from the header: what Start does, markers in the world, More options (every
/// other setting), and Something not working? (step by step, the recorder, the place words, the
/// windows and clicks); the version and What's new at the foot.
/// </summary>
public sealed partial class MainWindow
{
    private const string ReleasesUrl = "https://github.com/koko-puffs/housing-studio-plugin/releases";

    private bool moreOpen;
    private bool troubleOpen;
    private bool experimentalOpen;
    private bool callsOpen;
    private string? placeWords;

    /// <summary>The number of settings under More options (shown on its row).</summary>
    private const int MoreCount = 13;

    private void DrawSettings()
    {
        Frame.BeginHeader(20);
        var at = Kit.At;
        var w = Kit.Width;
        if (BackButton(at, session.Layout != null ? $"Back to {session.Title}" : "Back")) sub = Sub.None;
        Title(new Vector2(at.X + S(40) + S(10), at.Y + (S(40) - S(26 * 1.1f)) / 2), "Settings", null, w - S(50), 26);
        Kit.Reserve(at, w, S(40));
        Frame.EndHeader();

        Frame.BeginBody("##settings", null, 20, 24);

        Kit.EyebrowRow("Start does");
        Kit.Gap(10);
        Kit.BeginPanel("##start-does", 0, 0, 0);
        var both = config.StoreroomAutomatic && config.BagsAutomatic;
        var placeNote = both || (!config.StoreroomAutomatic && !config.BagsAutomatic)
            ? "From your storeroom and bags. Off: you put each one down and it's moved to its spot."
            : config.StoreroomAutomatic
                ? "From your storeroom; the ones in your bags you put down (More options)."
                : "From your bags; the ones in your storeroom you put down (More options).";
        if (SwitchRow(0, "##place-for-me", "Place pieces for me", placeNote, both))
        {
            config.StoreroomAutomatic = config.BagsAutomatic = !both;
            config.Save();
        }
        if (SwitchRow(1, "##dye", "Dye pieces after placing", "Uses dyes from your bags", config.DyeAfterPlacing))
        {
            config.DyeAfterPlacing = !config.DyeAfterPlacing;
            config.Save();
        }
        if (SwitchRow(2, "##backup", "Save a backup first", "So you can put back what was there", config.BackupBeforePlacing))
        {
            config.BackupBeforePlacing = !config.BackupBeforePlacing;
            config.Save();
        }
        var pace = (int)config.Pace;
        SetRow(3, "Pace", PaceSetting.Note(config.Pace), Kit.SegmentedWidth(PaceSetting.Labels), S(40), p =>
        {
            var picked = Kit.Segmented("##pace", p, PaceSetting.Labels, pace);
            if (picked != pace)
            {
                config.Pace = (Pace)picked;
                config.Save();
            }
        });
        Kit.EndPanel();

        Kit.Gap(18);
        Kit.EyebrowRow("In the world");
        Kit.Gap(10);
        Kit.BeginPanel("##world", 0, 0, 0);
        if (SwitchRow(0, "##markers", "Show where pieces go", "Markers in your home while a design is loaded", config.ShowMarkers))
        {
            config.ShowMarkers = !config.ShowMarkers;
            config.Save();
        }
        Kit.EndPanel();

        Kit.Gap(18);
        Kit.BeginPanel("##more", 0, 0, 0);
        var count = MoreCount.ToString();
        Disclosure("##more-row", ref moreOpen, "More options", "Place anywhere, pace variation, when to stop, chat messages",
            16, Txt.Measure(count, Face.Mono, 12).X, Txt.Natural(Face.Mono, 12),
            (p, _) => Txt.Draw(Kit.Dl, p, count, Face.Mono, 12, Palette.Dim), false, Palette.Dim);
        if (moreOpen) DrawMoreOptions();
        Kit.EndPanel();

        Kit.Gap(18);
        Kit.BeginPanel("##experimental", 0, 0, 0);
        Disclosure("##experimental-row", ref experimentalOpen, "Experimental", ExperimentalSummary(),
            16, 0, 0, null, false, Palette.Dim);
        if (experimentalOpen) DrawExperimental();
        Kit.EndPanel();

        Kit.Gap(18);
        Kit.BeginPanel("##trouble", 0, 0, 0);
        Disclosure("##trouble-row", ref troubleOpen, "Something not working?", "Step by step, recording what you do, sending a report",
            16, 0, 0, null, false, Palette.Dim);
        if (troubleOpen) DrawTrouble();
        Kit.EndPanel();

        Frame.BeginBottom("settings-bottom");
        var row = Kit.At;
        var v = typeof(MainWindow).Assembly.GetName().Version;
        var version = v == null ? "Housing Studio" : $"Housing Studio {v.Major}.{v.Minor}.{v.Build}";
        Txt.Line(Kit.Dl, row, version, Face.Sans, 13, Palette.Ash, 19.5f);
        const string news = "What's new";
        var nw = Txt.Measure(news, Face.Sans, 13).X;
        if (Txt.Flow(new Vector2(row.X + Kit.Width - nw, row.Y), nw + S(1), new[] { new Run(news, Face.Sans, 13, Palette.BrassSoft, Link: "news") }, 19.5f, out var link) > 0 && link == "news")
            Dalamud.Utility.Util.OpenLink(ReleasesUrl);
        Kit.Reserve(row, Kit.Width, S(19.5f));
        Frame.EndBottom();
        Frame.EndBody();
    }

    /// <summary>
    /// A setting's row (14px 20px inside, the faint line between): its name and note, the control on
    /// the right. <paramref name="below"/> puts the control under the words instead, for wide ones.
    /// </summary>
    private static void SetRow(int index, string name, string? note, float controlW, float controlH, Action<Vector2> control, bool below = false)
    {
        var at = Kit.At;
        var w = Kit.Width;
        if (index > 0) Kit.Divider(at.X, at.X + w, at.Y - 0.5f);
        var x = at.X + S(20);
        var wordsW = below || controlW <= 0 ? w - S(40) : w - S(40) - controlW - S(16);
        var nameH = Txt.Natural(Face.Sans, 14);
        var runs = note != null ? new[] { Run.Note(note) } : Array.Empty<Run>();
        var noteH = note != null ? Txt.Flow(Vector2.Zero, wordsW, runs, 19.5f, out _, draw: false) : 0;
        var wordsH = nameH + (note != null ? S(2) + noteH : 0);
        var h = below ? S(14) * 2 + wordsH + (controlH > 0 ? S(12) + controlH : 0) : S(14) * 2 + MathF.Max(wordsH, controlH);
        var dl = Kit.Dl;
        var top = below ? at.Y + S(14) : at.Y + (h - wordsH) / 2;
        Txt.Line(dl, new Vector2(x, top), Txt.Fit(name, Face.Sans, 14, wordsW), Face.Sans, 14, Palette.Ivory);
        if (note != null) Txt.Flow(new Vector2(x, top + nameH + S(2)), wordsW, runs, 19.5f, out _);
        if (below) control(new Vector2(x, top + wordsH + S(12)));
        else if (controlW > 0) control(new Vector2(at.X + w - S(20) - controlW, at.Y + (h - controlH) / 2));
        Kit.Reserve(at, w, h);
    }

    /// <summary>A setting with a switch; true when it's flipped.</summary>
    private static bool SwitchRow(int index, string id, string name, string? note, bool on)
    {
        var flipped = false;
        SetRow(index, name, note, S(40), S(24), p => flipped = Kit.Switch(id, p, on));
        return flipped;
    }

    /// <summary>A setting with a − value + stepper; returns −1, 0 or +1.</summary>
    private static int StepperRow(int index, string id, string name, string? note, string value, bool canDown, bool canUp)
    {
        var delta = 0;
        SetRow(index, name, note, Kit.StepperWidth(value), S(40), p => delta = Kit.Stepper(id, p, value, canDown, canUp));
        return delta;
    }

    private void DrawMoreOptions()
    {
        var i = 1;
        if (SwitchRow(i++, "##anywhere", "Place anywhere",
                "Lets floating, sunk and overlapping pieces be set (the MakePlace plugin's option). The game's own check is off only while a run lasts.",
                config.PlaceAnywhere))
        {
            config.PlaceAnywhere = !config.PlaceAnywhere;
            config.Save();
        }

        var jitter = (int)MathF.Round(config.Jitter * 100);
        var d = StepperRow(i++, "##jitter", "Pace variation", "How much the pause between pieces varies, so it never keeps an exact beat",
            $"±{jitter}%", jitter > 0, jitter < 50);
        if (d != 0)
        {
            config.Jitter = Math.Clamp(jitter + d * 5, 0, 50) / 100f;
            config.Save();
        }

        var failures = config.MaxFailuresInARow;
        d = StepperRow(i++, "##failures", "When to stop", $"After this many pieces in a row don't work", failures.ToString(), failures > 1, failures < 10);
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

        if (SwitchRow(i++, "##market-prices", "Look up market prices (asks Universalis)",
                "For what no vendor sells, the shopping list asks universalis.app over the internet what your data centre's market board wants",
                config.LookUpMarketPrices))
        {
            config.LookUpMarketPrices = !config.LookUpMarketPrices;
            config.Save();
        }

        var reach = (int)MathF.Round(config.NearMeRadius);
        d = StepperRow(i++, "##near-reach", "How far Near me reaches", "Pieces lists, and Start places, only what's this close to you while Near me is on",
            $"{reach} yalms", reach > 2, reach < 40);
        if (d != 0)
        {
            config.NearMeRadius = Math.Clamp(reach + d, 2, 40);
            config.Save();
        }

        if (SwitchRow(i++, "##storeroom-auto", "Place pieces from your storeroom for me",
                "Picks each in the furnishing list's Storeroom tab and chooses Place. Off: you put them down. Gardens: always you.", config.StoreroomAutomatic))
        {
            config.StoreroomAutomatic = !config.StoreroomAutomatic;
            config.Save();
        }
        if (SwitchRow(i++, "##bags-auto", "Place pieces from your bags for me",
                "Opens the item's menu and chooses Place. Off: you put them down.", config.BagsAutomatic))
        {
            config.BagsAutomatic = !config.BagsAutomatic;
            config.Save();
        }

        var floors = new[] { ("Basement", config.ShowBasement), ("Ground floor", config.ShowGround), ("Upper floor", config.ShowUpper) };
        var floorsW = floors.Sum(f => Kit.ButtonWidth(f.Item1, BtnSize.Sm)) + S(8) * 2;
        SetRow(i++, "Floors to place", "Start places, and Pieces lists, only the floors picked here", floorsW, S(34), p =>
        {
            var x = p.X;
            for (var f = 0; f < floors.Length; f++)
            {
                var (label, on) = floors[f];
                if (Kit.Button($"##floor-pick{f}", new Vector2(x, p.Y), label, on ? Btn.Picked : Btn.Glass, BtnSize.Sm))
                {
                    switch (f)
                    {
                        case 0: config.ShowBasement = !on; break;
                        case 1: config.ShowGround = !on; break;
                        default: config.ShowUpper = !on; break;
                    }
                    config.Save();
                }
                x += Kit.ButtonWidth(label, BtnSize.Sm) + S(8);
            }
        }, below: true);

        if (SwitchRow(i++, "##labels", "Name a marker when the mouse is over it", null, config.ShowLabels))
        {
            config.ShowLabels = !config.ShowLabels;
            config.Save();
        }
        if (SwitchRow(i++, "##placed-markers", "Mark pieces already in place too", null, config.ShowPlacedMarkers))
        {
            config.ShowPlacedMarkers = !config.ShowPlacedMarkers;
            config.Save();
        }
        var range = (int)MathF.Round(config.MarkerRange);
        d = StepperRow(i++, "##marker-range", "How far markers show", "Markers further from you than this are hidden", $"{range} yalms", range > 5, range < 120);
        if (d != 0)
        {
            config.MarkerRange = Math.Clamp(range + d * 5, 5, 120);
            config.Save();
        }

        const string save = "Save a build";
        SetRow(i++, "Save what's placed as a build", "A .build you can load here, open in MakePlace, or upload to Housing Studio",
            Kit.ButtonWidth(save, BtnSize.Sm), S(34), p =>
            {
                var cant = session.Context.Side == null ? "Go to your home first"
                    : !session.Context.HasPermission ? "You can save a home you're allowed to decorate" : null;
                if (Kit.Button("##save-build", p, save, Btn.Glass, BtnSize.Sm, null, 0, cant != null, cant))
                    SaveDialog();
            });

        // Good to know: not a setting, but it belongs with them.
        SetRow(i, "Good to know",
            "Placing binds a piece to you, as placing it by hand does, and dyeing uses up the dye. Plugins that act in the game for you are against Square Enix's terms of service; " +
            "this one only works on your own furniture in a home you may decorate, through the game's own windows, at the pace you set. The choice to use it is yours.",
            0, 0, _ => { });
    }

    private void DrawTrouble()
    {
        var i = 1;
        if (SwitchRow(i++, "##step", "Step by step",
                "Holds before every click and shows what it's about to do, with the window and values, until you press Continue (or /hstudio continue). Use it for your first run.",
                config.StepByStep))
        {
            config.StepByStep = !config.StepByStep;
            config.Save();
        }
        if (SwitchRow(i++, "##move-preview", "Move the preview before Place (old method)",
                "Moves the game's preview of a storeroom piece to its spot before choosing Place. Off: the piece is placed where the game puts it, then moved to its spot.",
                config.MovePreviewBeforePlace))
        {
            config.MovePreviewBeforePlace = !config.MovePreviewBeforePlace;
            config.Save();
        }

        // The recorder: its buttons under its words, wrapping when the window is narrow.
        var recording = recorder.Recording;
        var buttons = new List<(string Id, string Label, Btn Style, bool Disabled, string? Tip, Action Act)>
        {
            recording
                ? ("##rec-stop", "Stop recording", Btn.Danger, false, null, () => OnFramework(recorder.Stop))
                : ("##rec-start", "Start recording", Btn.Glass, false, null, () => OnFramework(recorder.Start)),
            ("##rec-copy", "Copy recording and log", Btn.Glass, recorder.LineCount == 0 && log.Lines.Count == 0, null, () =>
            {
                Dalamud.Bindings.ImGui.ImGui.SetClipboardText(Diagnostics());
                Notify("Recording and log copied");
            }),
            ("##rec-dump", "Write down the windows' values", Btn.Quiet, !recording,
                "While recording: adds every value the furnishing list, the dye window and the menus hold right now (open the Storeroom tab first).",
                () => OnFramework(recorder.DumpWindows)),
        };
        var width = Kit.Width - S(40);
        var lines = 1;
        var lx = 0f;
        foreach (var b in buttons)
        {
            var bw = Kit.ButtonWidth(b.Label, BtnSize.Sm, null, b.Style);
            if (lx > 0 && lx + bw > width)
            {
                lines++;
                lx = 0;
            }
            lx += bw + S(8);
        }
        var recNote = "While on, it writes down every game window that opens or closes, every click's values and the housing state. Turn it on, place one piece from your bags and one from the storeroom, dye one piece, by hand; turn it off, copy, and send the text along.";
        if (recording) recNote = $"Recording · {Words.Count(recorder.LineCount, "line")} so far. " + recNote;
        if (recorder.SetupError != null) recNote += " " + recorder.SetupError;
        SetRow(i++, "Record what you do", recNote, width, S(34) * lines + S(8) * (lines - 1), p =>
        {
            var x = p.X;
            var y = p.Y;
            foreach (var b in buttons)
            {
                var bw = Kit.ButtonWidth(b.Label, BtnSize.Sm, null, b.Style);
                if (x > p.X && x + bw > p.X + width)
                {
                    x = p.X;
                    y += S(34) + S(8);
                }
                if (Kit.Button(b.Id, new Vector2(x, y), b.Label, b.Style, BtnSize.Sm, null, 0, b.Disabled, b.Tip)) b.Act();
                x += bw + S(8);
            }
        }, below: true);

        placeWords ??= config.PlaceMenuWords;
        var builtIn = string.Join(", ", UiTable.PlaceLabels(Services.ClientState.ClientLanguage));
        SetRow(i++, "The \"Place\" entry in an item's menu", $"Built in for your client: {builtIn}. If your menu says something else, type it here (several, comma-separated).",
            width, S(40), p =>
            {
                var text = placeWords;
                if (Kit.Field("##placewords", p, MathF.Min(width, S(360)), ref text, "e.g. Place"))
                {
                    placeWords = text;
                    config.PlaceMenuWords = text.Trim();
                }
                // Saved once you're done typing, not every key (the whole config is written each time).
                if (ImGui.IsItemDeactivatedAfterEdit()) config.Save();
            }, below: true);

        // The windows and clicks the run uses, each with where its values came from.
        var at = Kit.At;
        Kit.Divider(at.X, at.X + Kit.Width, at.Y - 0.5f);
        var calls = UiTable.All().ToList();
        var unverified = calls.Count(c => !c.Verified);
        var count = calls.Count.ToString();
        Disclosure("##calls-row", ref callsOpen, "Windows and clicks used",
            unverified > 0 ? $"{unverified} not yet seen in a recording, in brass" : "Every one seen in a recording",
            14, Txt.Measure(count, Face.Mono, 12).X, Txt.Natural(Face.Mono, 12),
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
