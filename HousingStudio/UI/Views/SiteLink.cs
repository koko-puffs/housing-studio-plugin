using System;
using System.Net.Http;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using HousingStudio.Core;
using HousingStudio.Game;
using HousingStudio.Session;
using static HousingStudio.UI.Theme;

namespace HousingStudio.UI;

/// <summary>
/// Linking to Housing Studio (Settings): a token from the site, pasted once, kept masked, Unlink.
/// Then Start's Send to my builds, beside Save this house as a build: the same layout the save
/// writes, sent to your builds on the site (Core/SiteLink.cs) off the framework thread, and how
/// it went under the buttons. The token is never logged, nor put in the activity log.
/// </summary>
public sealed partial class MainWindow
{
    private string tokenDraft = string.Empty;
    private string? tokenProblem;

    /// <summary>A send under way (its name) or done (its result), shown under Start's buttons.</summary>
    private sealed record SendState(string Name, SendResult? Result);

    private volatile SendState? sendState;

    private bool Linked => SiteLink.LooksRight(config.SiteToken);

    private bool Sending => sendState is { Result: null };

    /// <summary>Settings: Link to Housing Studio (paste a token, Link) or, linked, the token masked and Unlink.</summary>
    private void DrawSiteLink()
    {
        Kit.EyebrowRow("Housing Studio");
        Kit.Gap(10);
        Kit.BeginPanel("##site-link", 0, 0, 0);
        if (Linked)
        {
            const string unlink = "Unlink";
            SetRow(0, "Linked to Housing Studio",
                $"Token {SiteLink.Masked(config.SiteToken)}. Send to my builds, on Start, puts the house you're in straight into your builds on the site.",
                Kit.ButtonWidth(unlink, BtnSize.Sm), S(34), p =>
                {
                    if (Kit.Button("##site-unlink", p, unlink, Btn.Glass, BtnSize.Sm, null, 0, false,
                            "Forget the token here. Revoke it on the site too if you won't use it again."))
                    {
                        config.SiteToken = string.Empty;
                        config.Save();
                        sendState = null;
                        Notify("Unlinked from Housing Studio");
                    }
                });
        }
        else
        {
            var width = Kit.Width - S(40);
            const string link = "Link";
            const string get = "Get a token";
            var linkW = Kit.ButtonWidth(link, BtnSize.Md, null, Btn.Primary);
            var getW = Kit.ButtonWidth(get, BtnSize.Md, null, Btn.Quiet);
            var note = "Send houses straight to your builds on the site. Make a token there (Settings → Housing Studio → Send houses from the game) and paste it here.";
            if (tokenProblem != null) note += " " + tokenProblem;
            SetRow(0, "Link to Housing Studio", note, width, S(40), p =>
            {
                var fieldW = MathF.Max(S(120), width - linkW - getW - S(16));
                var text = tokenDraft;
                if (Kit.Field("##site-token", p, fieldW, ref text, "hsp_…", null, 128, ImGuiInputTextFlags.Password))
                {
                    tokenDraft = text;
                    tokenProblem = null;
                }
                var x = p.X + fieldW + S(8);
                if (Kit.Button("##site-link-btn", new Vector2(x, p.Y), link, Btn.Primary, BtnSize.Md, null, 0, tokenDraft.Trim().Length == 0))
                    LinkToken();
                if (Kit.Button("##site-get", new Vector2(x + linkW + S(8), p.Y), get, Btn.Quiet))
                    Dalamud.Utility.Util.OpenLink(SiteLink.TokensUrl);
            }, below: true);
        }
        Kit.EndPanel();
    }

    private void LinkToken()
    {
        var token = SiteLink.Clean(tokenDraft);
        if (!SiteLink.LooksRight(token))
        {
            tokenProblem = "That isn't a token: it starts hsp_ and is 47 letters long.";
            return;
        }
        config.SiteToken = token;
        config.Save();
        tokenDraft = string.Empty;
        tokenProblem = null;
        sendState = null;
        Notify("Linked to Housing Studio");
    }

    /// <summary>Why Send to my builds can't go now, or null.</summary>
    private string? CantSend() =>
        !Linked ? "Link to Housing Studio in Settings first"
        : session.Context.Side == null ? "Go to your home first"
        : !session.Context.HasPermission ? "You can send a home you're allowed to decorate"
        : Sending ? "Sending…"
        : null;

    /// <summary>
    /// Send to my builds: what's placed here read on the framework thread (as Save does), sent from
    /// a worker with SiteLink's wait, and the answer kept for Start to show.
    /// </summary>
    private void SendHome()
    {
        if (CantSend() != null) return;
        var token = config.SiteToken;
        OnFramework(() =>
        {
            if (Sending) return;
            var ctx = session.Context;
            if (!session.ReadPlacedNow(out var pieces, out _, out var why))
            {
                Notify(why);
                return;
            }
            if (pieces.Count == 0)
            {
                Notify("Nothing placed here to send");
                return;
            }
            var name = SendName(ctx);
            if (!Backups.Layout(session, data, name, out var json, out var house, out why))
            {
                Notify(why.Length > 0 ? why : "Couldn't read what's placed");
                return;
            }
            var state = new SendState(name, null);
            sendState = state;
            _ = Task.Run(async () =>
            {
                SendResult result;
                try
                {
                    using var http = new HttpClient();
                    result = await SiteLink.SendAsync(http, token, name, house, json).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    // SendAsync says what went wrong in words; anything else is a fault here. The
                    // exception holds no token (it's only in the request's body).
                    Services.Log.Error(ex, "Send to my builds failed");
                    result = new SendResult(null, "Something went wrong sending it (see /xllog).");
                }
                // Only the latest send's answer is shown (an Unlink meanwhile drops it).
                if (ReferenceEquals(sendState, state)) sendState = state with { Result = result };
                try
                {
                    OnFramework(() =>
                    {
                        if (result.Sent) log.Done($"Sent {name} to your builds on Housing Studio.");
                        else log.Warn($"Couldn't send {name} to your builds: {result.Why}");
                        Notify(result.Sent ? $"Sent {name}" : "Couldn't send it");
                    });
                }
                catch (Exception)
                {
                    // The plugin was unloaded while it went.
                }
            });
        });
    }

    /// <summary>A sent build's name: "Mist house, 31 Oct 2026", "Lavender Beds garden, 31 Oct 2026".</summary>
    private static string SendName(HousingContext ctx)
    {
        var style = Districts.StyleOfInterior(ctx.TerritoryName) ?? (ctx.District != null ? Districts.Display(ctx.District) : null);
        if (style != null && style.StartsWith("The ", StringComparison.Ordinal)) style = style[4..];
        if (style == "Unknown district") style = null;
        var home = ctx.Side == Side.Garden ? "garden" : HomeWord(ctx.HouseSize);
        var what = style == null ? char.ToUpperInvariant(home[0]) + home[1..] : $"{style} {home}";
        // In English whatever the PC's language (the theme's fonts, and the site, are).
        return $"{what}, {DateTime.Now.ToString("d MMM yyyy", System.Globalization.CultureInfo.InvariantCulture)}";
    }

    private static string HomeWord(string? size) => size switch
    {
        "Small" => "cottage",
        "Medium" => "house",
        "Large" => "mansion",
        "Apartment" => "apartment",
        _ => "home",
    };

    /// <summary>Under Start's buttons: sending, sent (with Open it), or why it couldn't.</summary>
    private void DrawSendState()
    {
        if (sendState is not { } s) return;
        Kit.Gap(8);
        var at = Kit.At;
        var w = Kit.Width;
        Run[] runs = s.Result switch
        {
            null => new[] { Run.Note($"Sending {s.Name}…") },
            { Sent: true } => new[]
            {
                Run.Note($"Sent: {s.Name} is in your builds. ", Palette.Success),
                new Run("Open it", Face.Sans, 13, Palette.BrassSoft, Link: "open"),
            },
            { TokenRefused: true } => new[]
            {
                Run.Note($"Couldn't send it. {s.Result.Why} ", Palette.Danger),
                new Run("Settings", Face.Sans, 13, Palette.BrassSoft, Link: "settings"),
            },
            _ => new[] { Run.Note($"Couldn't send it. {s.Result.Why}", Palette.Danger) },
        };
        var h = Txt.Flow(at, w, runs, 19.5f, out var link, center: true);
        if (link == "open" && s.Result?.BuildId is { } id) Dalamud.Utility.Util.OpenLink(SiteLink.BuildUrl(id));
        if (link == "settings") sub = Sub.Settings;
        Kit.Reserve(at, w, h);
    }
}
