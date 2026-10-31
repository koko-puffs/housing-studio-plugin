using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace HousingStudio.Core;

/// <summary>What became of a send: the build's id on the site, or why it didn't go.</summary>
/// <param name="TokenRefused">The site doesn't know the token (revoked, or mistyped): link again.</param>
public sealed record SendResult(string? BuildId, string? Why, bool TokenRefused = false)
{
    public bool Sent => BuildId != null;
}

/// <summary>
/// Sending the house you're in straight to your builds on Housing Studio, with the token made on
/// the site (Settings → Housing Studio → Send houses from the game). The plugin calls the site's
/// database function <c>hs_plugin_save_build</c> with the site's publishable key, as the site
/// itself would; the token says whose builds it goes in. It's never logged, nor put in the log.
/// </summary>
public static class SiteLink
{
    /// <summary>The site's database (Supabase): its address and its publishable key, public by design.</summary>
    public const string SupabaseUrl = "https://upjcxjuuzxujqddtwehn.supabase.co";

    public const string PublishableKey = "sb_publishable_A-5zpDIk5O6xyKTicipcuA_M3J0sGVP";

    /// <summary>Where a token is made, and where a sent build opens.</summary>
    public const string TokensUrl = "https://www.kokos-tools.com/housing-studio";

    /// <summary>The longest a send waits for the site.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    private static readonly Regex Shape = new(@"^hsp_[A-Za-z0-9_-]{43}$", RegexOptions.CultureInvariant);

    private static readonly Regex BuildId = new(@"^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$", RegexOptions.CultureInvariant);

    /// <summary>A token as pasted, without the spaces, quotes or line breaks that come with a copy.</summary>
    public static string Clean(string? pasted) => (pasted ?? string.Empty).Trim().Trim('"', '\'', '`').Trim();

    /// <summary>Does it look like a token the site makes ("hsp_" and 43 letters)?</summary>
    public static bool LooksRight(string? token) => token != null && Shape.IsMatch(token);

    /// <summary>A token shown without giving it away: "hsp_••••••••3xQz".</summary>
    public static string Masked(string? token) =>
        string.IsNullOrEmpty(token) ? string.Empty
        : token.Length <= 8 ? new string('•', 8)
        : (token.StartsWith("hsp_", StringComparison.Ordinal) ? "hsp_" : string.Empty) + new string('•', 8) + token[^4..];

    /// <summary>A sent build on the site.</summary>
    public static string BuildUrl(string id) => $"https://www.kokos-tools.com/housing-studio/build/{Uri.EscapeDataString(id)}";

    /// <summary>
    /// The site's code for a home, as its builds keep it ("s1i2": a medium house in Mist): the
    /// style from a file's District line ("Mist", "The Lavender Beds", "Minimalist II"), the size
    /// from its houseSize. What it can't tell is Mist, and a small house (as the file then says);
    /// Minimalist apartments don't exist, so one is a Mist apartment (as the site's <c>houseCode</c>).
    /// </summary>
    public static string HouseCode(string? houseSize, string? style)
    {
        var n = (houseSize ?? string.Empty).ToLowerInvariant() switch
        {
            "medium" => 2,
            "large" => 3,
            var h when h.Contains("apartment") || h.Contains("room") || h.Contains("chamber") => 4,
            // Small, and a size it can't tell (the file says Small then: LayoutWriter).
            _ => 1,
        };
        var s = (style ?? string.Empty).Trim().ToLowerInvariant();
        var code = s.StartsWith("minimal", StringComparison.Ordinal)
            ? Regex.IsMatch(s, @"^minimal\w*\s+(ii|2)$", RegexOptions.CultureInvariant) ? "h2" : "h1"
            : Districts.FromName(s) switch
            {
                "lavender-beds" => "f1",
                "goblet" => "w1",
                "shirogane" => "e1",
                "empyreum" => "r1",
                _ => "s1",
            };
        if (n == 4 && code[0] == 'h') code = "s1";
        return $"{code}i{n}";
    }

    /// <summary>A build's name: as given, its spaces tidied, 80 letters at most (the site's limit); "Untitled build" when blank.</summary>
    public static string Name(string? name)
    {
        var s = Regex.Replace(name ?? string.Empty, @"\s+", " ").Trim();
        if (s.Length > 80) s = s[..80].TrimEnd();
        return s.Length == 0 ? "Untitled build" : s;
    }

    /// <summary>What's sent: the token, the name, the house and the layout (the .build's JSON, as an object).</summary>
    public static string Body(string token, string name, string house, string layoutJson) =>
        new JsonObject
        {
            ["p_token"] = token,
            ["p_name"] = Name(name),
            ["p_house"] = house,
            ["p_layout"] = JsonNode.Parse(layoutJson),
        }.ToJsonString();

    /// <summary>
    /// What the site answered, in words: the new build's id (a quoted id), or its refusal's message
    /// (the database's own words, written to be shown) with what to do about a refused token.
    /// </summary>
    public static SendResult Read(HttpStatusCode status, string? body)
    {
        var code = (int)status;
        JsonNode? node = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(body)) node = JsonNode.Parse(body);
        }
        catch (JsonException)
        {
            // Not JSON (a proxy's page): said by the status below.
        }

        if (code is >= 200 and < 300)
        {
            var id = node is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
            return id != null && BuildId.IsMatch(id)
                ? new SendResult(id, null)
                : new SendResult(null, "The site answered, but not with a build. Look in your builds before sending it again.");
        }

        string? message = null, hint = null;
        if (node is JsonObject o)
        {
            message = Text(o["message"]);
            hint = Text(o["hint"]);
        }
        if (hint == "token")
            return new SendResult(null, (message ?? "The site doesn't know this token.") + " Link again in Settings with a new one.", true);
        if (hint is "off" or "rate" or "house" or "layout" or "size" && message != null)
            return new SendResult(null, message);
        // The 50 builds (the site's own words), and anything else the database said in words.
        if (message != null && code is 400 or 403 or 409 or 500 && message.Length <= 200 && !message.Contains("function", StringComparison.OrdinalIgnoreCase))
            return new SendResult(null, message);
        return code switch
        {
            401 or 403 => new SendResult(null, "The site turned the plugin away. It may need updating."),
            404 => new SendResult(null, "The site can't take builds from the plugin right now. The plugin may need updating."),
            413 => new SendResult(null, "This house is too big to send. Save it as a file instead."),
            429 => new SendResult(null, "The site is busy. Try again in a minute."),
            >= 500 => new SendResult(null, $"The site had a problem ({code}). Try again in a minute."),
            _ => new SendResult(null, $"The site said no ({code})."),
        };
    }

    private static string? Text(JsonNode? n) =>
        n is JsonValue v && v.TryGetValue<string>(out var s) && !string.IsNullOrWhiteSpace(s) ? s.Trim() : null;

    /// <summary>
    /// Sends a build to the token's owner's builds and says how it went; never throws (a lost
    /// connection or the wait running out is said in words). The token goes in the body only.
    /// </summary>
    public static async Task<SendResult> SendAsync(HttpClient http, string token, string name, string house, string layoutJson, CancellationToken cancel = default)
    {
        if (!LooksRight(token)) return new SendResult(null, "That isn't a Housing Studio token. Link again in Settings.", true);
        string body;
        try
        {
            body = Body(token, name, house, layoutJson);
        }
        catch (JsonException)
        {
            return new SendResult(null, "What's placed couldn't be written down to send.");
        }
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        timeout.CancelAfter(Timeout);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{SupabaseUrl}/rest/v1/rpc/hs_plugin_save_build")
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
            request.Headers.Add("apikey", PublishableKey);
            request.Headers.Accept.ParseAdd("application/json");
            using var response = await http.SendAsync(request, timeout.Token).ConfigureAwait(false);
            var text = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
            return Read(response.StatusCode, text);
        }
        catch (OperationCanceledException) when (!cancel.IsCancellationRequested)
        {
            return new SendResult(null, "The site didn't answer in time. Look in your builds before sending it again.");
        }
        catch (OperationCanceledException)
        {
            return new SendResult(null, "Stopped.");
        }
        catch (HttpRequestException)
        {
            return new SendResult(null, "Couldn't reach the site. Check your connection and try again.");
        }
    }
}
