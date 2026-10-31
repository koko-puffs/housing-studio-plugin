using System.Net;
using System.Net.Http;
using System.Text.Json;
using HousingStudio.Core;
using Xunit;

namespace HousingStudio.Tests;

public class SiteLinkTests
{
    private const string Token = "hsp_A1b2_C3d-A1b2_C3d-A1b2_C3d-A1b2_C3d-abcdefg";
    private const string Id = "14b7ec3b-e17c-45b1-b416-22ba4367c37d";

    [Fact]
    public void KnowsATokenByItsShape()
    {
        Assert.True(SiteLink.LooksRight(Token));
        Assert.False(SiteLink.LooksRight(Token[..^1]));
        Assert.False(SiteLink.LooksRight("hsp_" + new string('+', 43)));
        Assert.False(SiteLink.LooksRight(null));
        Assert.Equal(Token, SiteLink.Clean($"  \"{Token}\"\r\n"));
    }

    [Fact]
    public void MasksATokenToItsLastFour()
    {
        Assert.Equal("hsp_••••••••defg", SiteLink.Masked(Token));
        Assert.DoesNotContain("A1b2", SiteLink.Masked(Token));
        Assert.Equal(string.Empty, SiteLink.Masked(""));
        Assert.Equal("••••••••", SiteLink.Masked("short"));
    }

    [Theory]
    [InlineData("Medium", "Mist", "s1i2")]
    [InlineData("Small", "The Lavender Beds", "f1i1")]
    [InlineData("Large", "The Goblet", "w1i3")]
    [InlineData("Apartment", "Shirogane", "e1i4")]
    [InlineData("Medium", "Empyreum", "r1i2")]
    [InlineData("Large", "Minimalist", "h1i3")]
    [InlineData("Small", "Minimalist II", "h2i1")]
    [InlineData("Apartment", "Minimalist", "s1i4")]
    [InlineData(null, null, "s1i2")]
    [InlineData("Medium", "Unknown district", "s1i2")]
    public void GivesTheSitesCodeForAHome(string? size, string? style, string code) =>
        Assert.Equal(code, SiteLink.HouseCode(size, style));

    [Fact]
    public void TidiesTheName()
    {
        Assert.Equal("Mist house, 31 Oct", SiteLink.Name("  Mist   house,\n31 Oct "));
        Assert.Equal("Untitled build", SiteLink.Name("   "));
        Assert.Equal(80, SiteLink.Name(new string('a', 120)).Length);
    }

    [Fact]
    public void SendsTheLayoutAsAnObject()
    {
        var body = JsonDocument.Parse(SiteLink.Body(Token, "Home", "s1i2", "{\"interiorFurniture\":[]}")).RootElement;
        Assert.Equal(Token, body.GetProperty("p_token").GetString());
        Assert.Equal("s1i2", body.GetProperty("p_house").GetString());
        Assert.Equal(JsonValueKind.Object, body.GetProperty("p_layout").ValueKind);
    }

    [Fact]
    public void ReadsTheSitesAnswers()
    {
        var ok = SiteLink.Read(HttpStatusCode.OK, $"\"{Id}\"");
        Assert.True(ok.Sent);
        Assert.Equal(Id, ok.BuildId);

        Assert.False(SiteLink.Read(HttpStatusCode.OK, "null").Sent);

        var token = SiteLink.Read(HttpStatusCode.Forbidden, "{\"code\":\"28000\",\"message\":\"That token was revoked, or never made.\",\"hint\":\"token\"}");
        Assert.True(token.TokenRefused);
        Assert.Contains("Link again", token.Why);

        var rate = SiteLink.Read(HttpStatusCode.InternalServerError, "{\"code\":\"54000\",\"message\":\"That’s 20 sends this hour. Try again later.\",\"hint\":\"rate\"}");
        Assert.Equal("That’s 20 sends this hour. Try again later.", rate.Why);
        Assert.False(rate.TokenRefused);

        var full = SiteLink.Read(HttpStatusCode.InternalServerError, "{\"code\":\"54000\",\"message\":\"You can keep up to 50 builds.\",\"hint\":null}");
        Assert.Equal("You can keep up to 50 builds.", full.Why);

        // The database's own wording about a missing function isn't for players.
        var gone = SiteLink.Read(HttpStatusCode.NotFound, "{\"code\":\"PGRST202\",\"message\":\"Could not find the function public.hs_plugin_save_build\"}");
        Assert.Contains("may need updating", gone.Why);

        Assert.Contains("(502)", SiteLink.Read(HttpStatusCode.BadGateway, "<html>bad gateway</html>").Why);
    }

    [Fact]
    public void LinksToTheBuild() =>
        Assert.Equal($"https://www.kokos-tools.com/housing-studio/build/{Id}", SiteLink.BuildUrl(Id));

    private sealed class Fake(Func<HttpRequestMessage, Task<HttpResponseMessage>> answer) : HttpMessageHandler
    {
        public HttpRequestMessage? Seen;
        public string? SeenBody;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Seen = request;
            SeenBody = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return await answer(request);
        }
    }

    [Fact]
    public async Task PostsToTheSitesFunctionWithItsKey()
    {
        var fake = new Fake(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent($"\"{Id}\"") }));
        var result = await SiteLink.SendAsync(new HttpClient(fake), Token, "Home", "s1i2", "{}");
        Assert.True(result.Sent);
        Assert.Equal(HttpMethod.Post, fake.Seen!.Method);
        Assert.Equal($"{SiteLink.SupabaseUrl}/rest/v1/rpc/hs_plugin_save_build", fake.Seen.RequestUri!.ToString());
        Assert.Equal(SiteLink.PublishableKey, Assert.Single(fake.Seen.Headers.GetValues("apikey")));
        // The token goes in the body, never the address or a header.
        Assert.DoesNotContain(Token, fake.Seen.RequestUri.ToString());
        Assert.DoesNotContain(fake.Seen.Headers, h => h.Value.Any(v => v.Contains(Token)));
        Assert.Contains(Token, fake.SeenBody);
    }

    [Fact]
    public async Task SaysWhenTheSiteCantBeReached()
    {
        var down = new Fake(_ => throw new HttpRequestException("No such host"));
        var result = await SiteLink.SendAsync(new HttpClient(down), Token, "Home", "s1i2", "{}");
        Assert.False(result.Sent);
        Assert.Contains("Couldn't reach the site", result.Why);
    }

    [Fact]
    public async Task RefusesABadTokenWithoutSending()
    {
        var fake = new Fake(_ => throw new InvalidOperationException("sent"));
        var result = await SiteLink.SendAsync(new HttpClient(fake), "nope", "Home", "s1i2", "{}");
        Assert.True(result.TokenRefused);
        Assert.Null(fake.Seen);
    }

    [Fact]
    public async Task StopsWhenAsked()
    {
        using var stop = new CancellationTokenSource();
        var fake = new Fake(async r =>
        {
            stop.Cancel();
            await Task.Delay(Timeout.Infinite, stop.Token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var result = await SiteLink.SendAsync(new HttpClient(fake), Token, "Home", "s1i2", "{}", stop.Token);
        Assert.Equal("Stopped.", result.Why);
    }
}
