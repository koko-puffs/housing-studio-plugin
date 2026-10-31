using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace HousingStudio.Core;

/// <summary>
/// What the site said to one of the plugin's calls: its answer, or why not in words (the site's
/// own, written to be shown) with its hint, what to do about it: <c>token</c> link again,
/// <c>gone</c> no longer there, <c>rate</c> too many this hour, <c>address</c> the design's address
/// isn't this home, <c>verify</c> verify it first, <c>full</c> delete a shot on the site. The
/// plugin's own: <c>offline</c> (no connection), <c>wait</c> (no answer in time), <c>stopped</c>,
/// <c>answer</c> (an answer it couldn't read), <c>server</c> (the site's trouble, 5xx).
/// </summary>
public sealed record SiteReply<T>(T? Value, string? Why = null, string? Hint = null)
{
    public bool Ok => Why == null;

    /// <summary>The site doesn't know the token (revoked, or never made): link again.</summary>
    public bool TokenRefused => Hint == "token";

    /// <summary>It isn't there any more (an inbox item removed, or no longer yours to have).</summary>
    public bool Gone => Hint == "gone";

    /// <summary>Worth trying again later by itself: no connection, no answer, the site's own trouble, too many.</summary>
    public bool Passing => Hint is "offline" or "wait" or "server" or "rate";

    public SiteReply<TOther> As<TOther>() => new(default, Why, Hint);
}

/// <summary>Something sent to the game from the site, waiting in the plugin's inbox (<c>hs_plugin_inbox_list</c>).</summary>
/// <param name="Kind">"build" or "design".</param>
public sealed record InboxItem(string Id, string Kind, string RefId, string Name, DateTimeOffset CreatedAt, DateTimeOffset? FetchedAt)
{
    public string KindWord => Kind == "build" ? "Build" : "Design";
}

/// <summary>One of them, whole (<c>hs_plugin_inbox_get</c>): its name, the file name it's kept under, a build's home code, and the layout's JSON.</summary>
public sealed record InboxEntry(string Name, string FileName, string? House, string LayoutJson);

/// <summary>One of your designs whose address is the home you're in (<c>hs_plugin_home</c>).</summary>
public sealed record MyDesign(string Id, string Name, string Visibility, DateTimeOffset? VerifiedAt)
{
    public bool Verified => VerifiedAt != null;
}

/// <summary>Who made a design: their handle (for their page) and name.</summary>
public sealed record Maker(string? Handle, string Name);

/// <summary>A design's likes, saves and comments, and your own like and save (<c>hs_social</c>).</summary>
public sealed record Social(int Likes, int MyVote, int Favourites, bool IsFavourite, int Comments)
{
    public bool Liked => MyVote == 1;

    public static readonly Social None = new(0, 0, 0, false, 0);
}

/// <summary>A design's visitor stamps: whether stamps are on at all, how many, and whether you stamped it today.</summary>
public sealed record Stamps(bool On, int Total, bool MineToday)
{
    public static readonly Stamps None = new(false, 0, false);
}

/// <summary>A published design verified as the home you're visiting (<c>hs_plugin_here</c>).</summary>
/// <param name="Cover">Its cover photo's path in the public <c>hs-photos</c> bucket, or null.</param>
/// <param name="Own">Your own design: no like, save or stamp then.</param>
public sealed record HereDesign(string Id, string Name, Maker Maker, string? Cover, DateTimeOffset? VerifiedAt, bool Own, Social Social, Stamps Stamps);

/// <summary>A like, save or stamp's answer: the design's likes and stamps as they are now.</summary>
public sealed record Reactions(Social Social, Stamps Stamps);

/// <summary>A shot the site kept: its id, its path in the bucket, and the design's name.</summary>
public sealed record ShotSent(string Id, string Path, string Name);

/// <summary>
/// The plugin's calls to the site beyond sending a build (the contract in the site's
/// <c>…_hs_plugin_game_link.sql</c>): the inbox of things sent from the site, the designs of the home
/// you're in and verifying one, the designs of a home you visit with like, save and stamp, and a
/// shot from the game to the edge function <c>hs-plugin-shot</c>. Each is a POST with the site's
/// publishable key, the token in the body only; none throws (a lost connection or no answer in
/// time is said in words), and none is ever called on the game's framework thread.
/// </summary>
public static partial class SiteLink
{
    /// <summary>The longest a light read (the inbox, a home's designs) or an action waits for the site.</summary>
    public static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(15);

    /// <summary>The longest opening something from the inbox waits (a design's layout can be big).</summary>
    public static readonly TimeSpan FetchTimeout = TimeSpan.FromSeconds(30);

    /// <summary>The longest a shot's upload waits.</summary>
    public static readonly TimeSpan ShotTimeout = TimeSpan.FromSeconds(90);

    /// <summary>A design's page on the site.</summary>
    public static string DesignUrl(string id) => $"https://www.kokos-tools.com/housing-studio/d/{Uri.EscapeDataString(id)}";

    /// <summary>Where a new design is posted.</summary>
    public const string NewDesignUrl = "https://www.kokos-tools.com/housing-studio/new";

    /// <summary>
    /// A photo in the public bucket (the site's <c>photoUrl</c>): each part of its path escaped; the
    /// small copy is the same name with "-s" before its extension (<c>smallPath</c>).
    /// </summary>
    public static string PhotoUrl(string path, bool small = false)
    {
        var p = small ? Regex.Replace(path, @"(\.\w+)$", "-s$1", RegexOptions.CultureInvariant) : path;
        return $"{SupabaseUrl}/storage/v1/object/public/hs-photos/{string.Join('/', p.Split('/').Select(Uri.EscapeDataString))}";
    }

    public static bool IsUuid(string? id) => id != null && BuildId.IsMatch(id);

    // ---------------------------------------------------------------------------------------------
    // One call, and what came back.

    /// <summary>Calls one of the site's database functions; its JSON answer, or why not.</summary>
    public static async Task<SiteReply<JsonNode?>> CallAsync(HttpClient http, string function, JsonObject args, TimeSpan wait, CancellationToken cancel = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        timeout.CancelAfter(wait);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{SupabaseUrl}/rest/v1/rpc/{function}")
            {
                Content = new StringContent(args.ToJsonString(), Encoding.UTF8, "application/json"),
            };
            request.Headers.Add("apikey", PublishableKey);
            request.Headers.Accept.ParseAdd("application/json");
            using var response = await http.SendAsync(request, timeout.Token).ConfigureAwait(false);
            var text = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
            return ReadCall(response.StatusCode, text);
        }
        catch (OperationCanceledException) when (!cancel.IsCancellationRequested)
        {
            return new SiteReply<JsonNode?>(null, "The site didn't answer in time. Try again in a minute.", "wait");
        }
        catch (OperationCanceledException)
        {
            return new SiteReply<JsonNode?>(null, "Stopped.", "stopped");
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            return new SiteReply<JsonNode?>(null, "Couldn't reach the site. Check your connection.", "offline");
        }
    }

    /// <summary>A function's answer: its JSON (null is an answer too), or its refusal in words.</summary>
    public static SiteReply<JsonNode?> ReadCall(HttpStatusCode status, string? body)
    {
        var code = (int)status;
        if (code is >= 200 and < 300)
        {
            try
            {
                return new SiteReply<JsonNode?>(string.IsNullOrWhiteSpace(body) ? null : JsonNode.Parse(body));
            }
            catch (JsonException)
            {
                return new SiteReply<JsonNode?>(null, "The site answered with something the plugin can't read.", "answer");
            }
        }
        var (why, hint) = Refusal(status, body);
        return new SiteReply<JsonNode?>(null, why, hint);
    }

    /// <summary>Words that are the database's, not the site's (a missing function, a bad value): not for players.</summary>
    private static readonly string[] Internal =
    {
        "function", "column", "relation", "syntax", "violates", "permission denied", "schema", "operator does not exist", "invalid input",
    };

    /// <summary>
    /// A refusal in words, and its hint: the site's own message when it wrote one (a token it doesn't
    /// know gets "Link again" after it), else what the status says.
    /// </summary>
    public static (string Why, string? Hint) Refusal(HttpStatusCode status, string? body)
    {
        var code = (int)status;
        string? message = null, hint = null, pgCode = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(body) && JsonNode.Parse(body) is JsonObject o)
            {
                message = Text(o["message"]);
                hint = Text(o["hint"]);
                pgCode = Text(o["code"]);
            }
        }
        catch (JsonException)
        {
            // Not JSON (a proxy's page): said by the status below.
        }

        if (hint == "token")
            return ((message ?? "The site doesn't know this token.") + " Link again in Settings with a new one.", hint);
        var own = message != null && message.Length <= 240
                  && !(pgCode?.StartsWith("PGRST", StringComparison.OrdinalIgnoreCase) ?? false)
                  && !Internal.Any(w => message.Contains(w, StringComparison.OrdinalIgnoreCase));
        if (own) return (message!, hint ?? (code >= 500 ? "server" : null));
        return code switch
        {
            401 or 403 => ("The site turned the plugin away. It may need updating.", hint),
            404 => ("The site can't do that from the plugin right now. The plugin may need updating.", hint),
            413 => ("That's too big to send.", hint),
            429 => ("The site is busy. Try again in a minute.", "rate"),
            >= 500 => ($"The site had a problem ({code}). Try again in a minute.", "server"),
            _ => ($"The site said no ({code}).", hint),
        };
    }

    private static SiteReply<T> NoToken<T>() => new(default, "That isn't a Housing Studio token. Link again in Settings.", "token");

    private static SiteReply<T> Unreadable<T>() => new(default, "The site answered with something the plugin can't read.", "answer");

    // ---------------------------------------------------------------------------------------------
    // Reading answers.

    private static string? Str(JsonNode? n) => n is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    private static int Int(JsonNode? n)
    {
        if (n is not JsonValue v) return 0;
        if (v.TryGetValue<int>(out var i)) return i;
        if (v.TryGetValue<long>(out var l)) return (int)Math.Clamp(l, int.MinValue, int.MaxValue);
        if (v.TryGetValue<double>(out var d) && double.IsFinite(d)) return (int)Math.Clamp(d, int.MinValue, int.MaxValue);
        return 0;
    }

    private static bool Bool(JsonNode? n) => n is JsonValue v && v.TryGetValue<bool>(out var b) && b;

    private static DateTimeOffset? Time(JsonNode? n) =>
        Str(n) is { } s && DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var t) ? t : null;

    /// <summary>A name as the site shows it, its spaces tidied; <paramref name="otherwise"/> when blank.</summary>
    private static string Named(JsonNode? n, string otherwise)
    {
        var s = Regex.Replace(Str(n) ?? string.Empty, @"\s+", " ").Trim();
        return s.Length == 0 ? otherwise : s;
    }

    /// <summary>The inbox, newest first; rows it can't read are left out. Null when it isn't a list.</summary>
    public static List<InboxItem>? ReadInbox(JsonNode? node)
    {
        if (node is null) return new List<InboxItem>();
        if (node is not JsonArray rows) return null;
        var list = new List<InboxItem>();
        foreach (var row in rows.OfType<JsonObject>())
        {
            var id = Str(row["id"]);
            var kind = Str(row["kind"]);
            if (!IsUuid(id) || kind is not ("build" or "design") || Time(row["created_at"]) is not { } created) continue;
            list.Add(new InboxItem(id!, kind, Str(row["ref_id"]) ?? string.Empty, Named(row["name"], "Untitled"), created, Time(row["fetched_at"])));
        }
        return list.OrderByDescending(i => i.CreatedAt).ToList();
    }

    /// <summary>One inbox item, whole; null when it has no layout.</summary>
    public static InboxEntry? ReadInboxEntry(JsonNode? node)
    {
        if (node is not JsonObject o || o["layout"] is not JsonObject layout) return null;
        var name = Named(o["name"], "Untitled");
        return new InboxEntry(name, SafeFileName(Str(o["file_name"]), name), Str(o["house"]), layout.ToJsonString());
    }

    public static List<MyDesign>? ReadMyDesigns(JsonNode? node)
    {
        if (node is null) return new List<MyDesign>();
        if (node is not JsonArray rows) return null;
        return rows.OfType<JsonObject>()
            .Where(r => IsUuid(Str(r["id"])))
            .Select(r => new MyDesign(Str(r["id"])!, Named(r["name"], "Untitled"), Str(r["visibility"]) ?? string.Empty, Time(r["verified_at"])))
            .ToList();
    }

    public static Social ReadSocial(JsonNode? node) => node is JsonObject o
        ? new Social(Int(o["likes"]), Int(o["my_vote"]), Int(o["favourites"]), Bool(o["is_favourite"]), Int(o["comments"]))
        : Social.None;

    public static Stamps ReadStamps(JsonNode? node) => node is JsonObject o
        ? new Stamps(Bool(o["on"]), Int(o["total"]), Bool(o["mine_today"]))
        : Stamps.None;

    public static List<HereDesign>? ReadHere(JsonNode? node)
    {
        if (node is null) return new List<HereDesign>();
        if (node is not JsonArray rows) return null;
        var list = new List<HereDesign>();
        foreach (var r in rows.OfType<JsonObject>())
        {
            var id = Str(r["id"]);
            if (!IsUuid(id)) continue;
            var maker = r["maker"] as JsonObject;
            var cover = Str(r["cover"]);
            list.Add(new HereDesign(id!, Named(r["name"], "Untitled"),
                new Maker(Str(maker?["handle"]), Named(maker?["name"], "Someone")),
                string.IsNullOrWhiteSpace(cover) ? null : cover,
                Time(r["verified_at"]), Bool(r["own"]), ReadSocial(r["social"]), ReadStamps(r["stamps"])));
        }
        return list;
    }

    // ---------------------------------------------------------------------------------------------
    // 1. The inbox: things sent to the game from the site.

    public static async Task<SiteReply<List<InboxItem>>> InboxAsync(HttpClient http, string token, CancellationToken cancel = default)
    {
        if (!LooksRight(token)) return NoToken<List<InboxItem>>();
        var r = await CallAsync(http, "hs_plugin_inbox_list", new JsonObject { ["p_token"] = token }, CallTimeout, cancel).ConfigureAwait(false);
        if (!r.Ok) return r.As<List<InboxItem>>();
        return ReadInbox(r.Value) is { } list ? new SiteReply<List<InboxItem>>(list) : Unreadable<List<InboxItem>>();
    }

    public static async Task<SiteReply<InboxEntry>> InboxGetAsync(HttpClient http, string token, string id, CancellationToken cancel = default)
    {
        if (!LooksRight(token)) return NoToken<InboxEntry>();
        if (!IsUuid(id)) return new SiteReply<InboxEntry>(null, "That isn't waiting any more.", "gone");
        var r = await CallAsync(http, "hs_plugin_inbox_get", new JsonObject { ["p_token"] = token, ["p_id"] = id }, FetchTimeout, cancel).ConfigureAwait(false);
        if (!r.Ok) return r.As<InboxEntry>();
        return ReadInboxEntry(r.Value) is { } entry
            ? new SiteReply<InboxEntry>(entry)
            : new SiteReply<InboxEntry>(null, "That came without a layout to open.", "answer");
    }

    /// <summary>Takes one off the inbox; its value is whether it was there.</summary>
    public static async Task<SiteReply<bool>> InboxRemoveAsync(HttpClient http, string token, string id, CancellationToken cancel = default)
    {
        if (!LooksRight(token)) return NoToken<bool>();
        if (!IsUuid(id)) return new SiteReply<bool>(false);
        var r = await CallAsync(http, "hs_plugin_inbox_remove", new JsonObject { ["p_token"] = token, ["p_id"] = id }, CallTimeout, cancel).ConfigureAwait(false);
        return r.Ok ? new SiteReply<bool>(r.Value is JsonValue v && v.TryGetValue<bool>(out var b) && b) : r.As<bool>();
    }

    /// <summary>
    /// The name an inbox item is written to disk under: the site's file name (only its last part,
    /// without the letters Windows doesn't allow in a name, 80 at most), keeping its .build or .json
    /// (.json when it has neither); the item's name when that leaves nothing.
    /// </summary>
    public static string SafeFileName(string? fileName, string? name)
    {
        var file = CleanName(fileName);
        var ext = Path.GetExtension(file).ToLowerInvariant();
        string stem;
        if (ext is ".build" or ".json") stem = file[..^ext.Length];
        else
        {
            stem = file;
            ext = ".json";
        }
        stem = stem.Trim().Trim('.').Trim();
        if (stem.Length == 0) stem = CleanName(name).Trim('.').Trim();
        if (stem.Length == 0) stem = "From Housing Studio";
        if (stem.Length > 80) stem = stem[..80].TrimEnd(' ', '.');
        if (Reserved.IsMatch(stem)) stem = "_" + stem;
        return stem + ext;
    }

    /// <summary>Windows' device names, which can't be a file's name (with any extension).</summary>
    private static readonly Regex Reserved = new(@"^(con|prn|aux|nul|com[0-9]|lpt[0-9])(\..*)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static string CleanName(string? s)
    {
        s ??= string.Empty;
        var slash = Math.Max(s.LastIndexOf('/'), s.LastIndexOf('\\'));
        if (slash >= 0) s = s[(slash + 1)..];
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s) sb.Append(char.IsControl(ch) || "<>:\"/\\|?*".Contains(ch) ? ' ' : ch);
        // (Its dots stay: ".build" alone is an extension with no name.)
        return Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
    }

    /// <summary>
    /// What's said in chat and the notification when things arrive: "“Cozy Café” arrived from Housing
    /// Studio. /hstudio to open it.", or "… and 2 more arrived … to open them."; null for none.
    /// </summary>
    public static string? Arrived(IReadOnlyList<InboxItem> fresh)
    {
        if (fresh.Count == 0) return null;
        var first = fresh[0].Name;
        return fresh.Count == 1
            ? $"“{first}” arrived from Housing Studio. /hstudio to open it."
            : $"“{first}” and {fresh.Count - 1} more arrived from Housing Studio. /hstudio to open them.";
    }

    // ---------------------------------------------------------------------------------------------
    // 2. Verified homes, and visitors.

    /// <summary>Your designs whose address is this home, and whether each is verified.</summary>
    public static async Task<SiteReply<List<MyDesign>>> HomeAsync(HttpClient http, string token, SiteHome home, CancellationToken cancel = default)
    {
        if (!LooksRight(token)) return NoToken<List<MyDesign>>();
        var r = await CallAsync(http, "hs_plugin_home", new JsonObject { ["p_token"] = token, ["p_home"] = home.ToJson() }, CallTimeout, cancel).ConfigureAwait(false);
        if (!r.Ok) return r.As<List<MyDesign>>();
        return ReadMyDesigns(r.Value) is { } list ? new SiteReply<List<MyDesign>>(list) : Unreadable<List<MyDesign>>();
    }

    /// <summary>Verifies one of your designs as this home (or, <paramref name="on"/> false, takes it back).</summary>
    public static async Task<SiteReply<MyDesign>> VerifyAsync(HttpClient http, string token, string designId, SiteHome home, bool on = true, CancellationToken cancel = default)
    {
        if (!LooksRight(token)) return NoToken<MyDesign>();
        if (!IsUuid(designId)) return new SiteReply<MyDesign>(null, "That isn't one of your designs.", "design");
        var r = await CallAsync(http, "hs_plugin_verify", new JsonObject
        {
            ["p_token"] = token,
            ["p_design"] = designId,
            ["p_home"] = home.ToJson(),
            ["p_on"] = on,
        }, CallTimeout, cancel).ConfigureAwait(false);
        if (!r.Ok) return r.As<MyDesign>();
        if (r.Value is not JsonObject o || !IsUuid(Str(o["id"]))) return Unreadable<MyDesign>();
        return new SiteReply<MyDesign>(new MyDesign(Str(o["id"])!, Named(o["name"], "Untitled"), string.Empty, Time(o["verified_at"])));
    }

    /// <summary>The published designs verified as this home; with your token, your own like, save and stamp too (an empty token asks as anyone).</summary>
    public static async Task<SiteReply<List<HereDesign>>> HereAsync(HttpClient http, string? token, SiteHome home, CancellationToken cancel = default)
    {
        var t = LooksRight(token) ? token! : string.Empty;
        var r = await CallAsync(http, "hs_plugin_here", new JsonObject { ["p_token"] = t, ["p_home"] = home.ToJson() }, CallTimeout, cancel).ConfigureAwait(false);
        if (!r.Ok) return r.As<List<HereDesign>>();
        return ReadHere(r.Value) is { } list ? new SiteReply<List<HereDesign>>(list) : Unreadable<List<HereDesign>>();
    }

    /// <summary>The acts a visitor's plugin can do: like, save (a favourite), stamp (today's, from inside the home).</summary>
    public static readonly string[] Acts = { "like", "save", "stamp" };

    /// <summary>Like, save or stamp a design (<paramref name="on"/> false takes it back); a stamp needs the home you're in.</summary>
    public static async Task<SiteReply<Reactions>> ActAsync(HttpClient http, string token, string designId, string act, bool on, SiteHome? home, CancellationToken cancel = default)
    {
        if (!LooksRight(token)) return NoToken<Reactions>();
        if (!IsUuid(designId) || Array.IndexOf(Acts, act) < 0) return new SiteReply<Reactions>(null, "The plugin asked for something the site doesn't do.", "act");
        var args = new JsonObject
        {
            ["p_token"] = token,
            ["p_design"] = designId,
            ["p_act"] = act,
            ["p_on"] = on,
            ["p_home"] = home?.ToJson(),
        };
        var r = await CallAsync(http, "hs_plugin_act", args, CallTimeout, cancel).ConfigureAwait(false);
        if (!r.Ok) return r.As<Reactions>();
        if (r.Value is not JsonObject o) return Unreadable<Reactions>();
        return new SiteReply<Reactions>(new Reactions(ReadSocial(o["social"]), ReadStamps(o["stamps"])));
    }

    // ---------------------------------------------------------------------------------------------
    // 3. Shots from the game.

    /// <summary>The edge function that takes a shot (the plugin can't upload to storage itself).</summary>
    public static string ShotUrl => $"{SupabaseUrl}/functions/v1/hs-plugin-shot";

    /// <summary>
    /// The shot's form: <c>token</c>, <c>design</c>, <c>pose</c> (its JSON) and <c>image</c> (the
    /// JPEG, as a file). Each part's name is quoted, as every form parser reads it.
    /// </summary>
    public static MultipartFormDataContent ShotForm(string token, string designId, string poseJson, byte[] jpeg)
    {
        var form = new MultipartFormDataContent();
        form.Add(Field("token", token));
        form.Add(Field("design", designId));
        form.Add(Field("pose", poseJson));
        var image = new ByteArrayContent(jpeg);
        image.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        image.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data") { Name = "\"image\"", FileName = "\"shot.jpg\"" };
        form.Add(image);
        return form;
    }

    private static StringContent Field(string name, string value)
    {
        var c = new StringContent(value, Encoding.UTF8);
        c.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data") { Name = $"\"{name}\"" };
        return c;
    }

    /// <summary>Sends a shot of a verified home to its design; never throws.</summary>
    public static async Task<SiteReply<ShotSent>> ShotAsync(HttpClient http, string token, string designId, string poseJson, byte[] jpeg, CancellationToken cancel = default)
    {
        if (!LooksRight(token)) return NoToken<ShotSent>();
        if (!IsUuid(designId)) return new SiteReply<ShotSent>(null, "The plugin didn't say which design.", "design");
        if (jpeg.Length == 0 || jpeg.Length > ShotSize.MaxBytes) return new SiteReply<ShotSent>(null, "That picture is too big to send.", "image");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        timeout.CancelAfter(ShotTimeout);
        try
        {
            using var form = ShotForm(token, designId, poseJson, jpeg);
            using var request = new HttpRequestMessage(HttpMethod.Post, ShotUrl) { Content = form };
            request.Headers.Add("apikey", PublishableKey);
            request.Headers.Accept.ParseAdd("application/json");
            using var response = await http.SendAsync(request, timeout.Token).ConfigureAwait(false);
            var text = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
            return ReadShot(response.StatusCode, text);
        }
        catch (OperationCanceledException) when (!cancel.IsCancellationRequested)
        {
            return new SiteReply<ShotSent>(null, "The site didn't answer in time. Look on the design's page before sending it again.", "wait");
        }
        catch (OperationCanceledException)
        {
            return new SiteReply<ShotSent>(null, "Stopped.", "stopped");
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            return new SiteReply<ShotSent>(null, "Couldn't reach the site. Check your connection and try again.", "offline");
        }
    }

    /// <summary>The edge function's answer: <c>{id, path, name}</c>, or <c>{message, hint}</c> in words.</summary>
    public static SiteReply<ShotSent> ReadShot(HttpStatusCode status, string? body)
    {
        var code = (int)status;
        if (code is >= 200 and < 300)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(body) && JsonNode.Parse(body) is JsonObject o && IsUuid(Str(o["id"])))
                    return new SiteReply<ShotSent>(new ShotSent(Str(o["id"])!, Str(o["path"]) ?? string.Empty, Named(o["name"], "your design")));
            }
            catch (JsonException)
            {
                // Said below.
            }
            return new SiteReply<ShotSent>(null, "The site answered, but not with the shot. Look on the design's page before sending it again.", "answer");
        }
        var (why, hint) = Refusal(status, body);
        if (code == 413) return new SiteReply<ShotSent>(null, why == "That's too big to send." ? "That picture is too big to send (4 MB at most)." : why, hint ?? "image");
        if (code == 404 && hint == null) return new SiteReply<ShotSent>(null, "The site can't take shots from the plugin right now. The plugin may need updating.");
        return new SiteReply<ShotSent>(null, why, hint);
    }
}
