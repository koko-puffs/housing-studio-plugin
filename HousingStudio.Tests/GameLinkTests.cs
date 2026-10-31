using System.Net;
using System.Net.Http;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using HousingStudio.Core;
using HousingStudio.UI.Model;
using Xunit;

namespace HousingStudio.Tests;

/// <summary>0.7.0's site link: homes as the site's addresses, camera poses, shot sizes, and the calls' words.</summary>
public class HomeAddressTests
{
    private static HouseFacts House(int ward, int plot, int room = 0) =>
        new("Lich", "mist", ward, plot, false, 0, room, false);

    [Fact]
    public void AHouseIsItsWardAndPlotCountingFromOne()
    {
        var home = HomeAddress.From(House(4, 22), out var why);
        Assert.Equal(string.Empty, why);
        Assert.Equal(new SiteHome("Lich", "mist", 5, "house", Plot: 23), home);
    }

    [Fact]
    public void TheSubdivisionsPlotsAre31To60()
    {
        Assert.Equal(31, HomeAddress.From(House(0, 30), out _)!.Plot);
        Assert.Equal(60, HomeAddress.From(House(29, 59), out _)!.Plot);
        Assert.Equal(30, HomeAddress.From(House(29, 59), out _)!.Ward);
        Assert.Null(HomeAddress.From(House(0, 60), out var why));
        Assert.Contains("plot", why);
    }

    [Fact]
    public void AnApartmentIsItsNumberAndBuilding()
    {
        var main = HomeAddress.From(new HouseFacts("Lich", "goblet", 2, 0, true, 0, 41, false), out _);
        Assert.Equal(new SiteHome("Lich", "goblet", 3, "apartment", Room: 41, Subdivision: false), main);
        var sub = HomeAddress.From(new HouseFacts("Lich", "goblet", 2, 1, true, 1, 7, false), out _);
        Assert.Equal(true, sub!.Subdivision);
        Assert.Null(sub.Plot);
        // An apartment numbered 0 isn't one the game shows.
        Assert.Null(HomeAddress.From(new HouseFacts("Lich", "goblet", 2, 0, true, 0, 0, false), out var why));
        Assert.Contains("apartment", why);
    }

    [Fact]
    public void APrivateChamberIsItsPlotAndNumber()
    {
        var room = HomeAddress.From(House(4, 22, room: 3), out _);
        Assert.Equal(new SiteHome("Lich", "mist", 5, "room", Plot: 23, Room: 3), room);
    }

    [Fact]
    public void AWorkshopIsntAHome()
    {
        Assert.Null(HomeAddress.From(House(4, 22, room: 0x3FF), out var why));
        Assert.Contains("workshop", why);
        Assert.Null(HomeAddress.From(new HouseFacts("Lich", "mist", 4, 22, false, 0, 5, true), out _));
    }

    [Fact]
    public void SaysWhatItCouldntTell()
    {
        Assert.Null(HomeAddress.From(new HouseFacts(null, "mist", 0, 0, false, 0, 0, false), out var why));
        Assert.Contains("world", why);
        Assert.Null(HomeAddress.From(new HouseFacts("Lich", null, 0, 0, false, 0, 0, false), out why));
        Assert.Contains("district", why);
        Assert.Null(HomeAddress.From(new HouseFacts("Lich", "mist", 30, 0, false, 0, 0, false), out why));
        Assert.Contains("ward", why);
        Assert.Null(HomeAddress.From(new HouseFacts("  ", "mist", 0, 0, false, 0, 0, false), out _));
    }

    [Fact]
    public void AGardenIsTheHouseOnItsPlot()
    {
        Assert.Equal(new SiteHome("Lich", "empyreum", 1, "house", Plot: 45), HomeAddress.Garden("Lich", "empyreum", 0, 45, out _));
        Assert.Null(HomeAddress.Garden("Lich", "empyreum", 0, null, out _));
        Assert.Null(HomeAddress.Garden("Lich", "empyreum", -1, 3, out _));
    }

    [Fact]
    public void WritesOnlyWhatEachKindHas()
    {
        Assert.Equal("{\"world\":\"Lich\",\"district\":\"mist\",\"ward\":5,\"kind\":\"house\",\"plot\":23}",
            new SiteHome("Lich", "mist", 5, "house", Plot: 23).ToJson().ToJsonString());
        Assert.Equal("{\"world\":\"Lich\",\"district\":\"mist\",\"ward\":5,\"kind\":\"apartment\",\"room\":41,\"subdivision\":true}",
            new SiteHome("Lich", "mist", 5, "apartment", Room: 41, Subdivision: true).ToJson().ToJsonString());
        Assert.Equal("{\"world\":\"Lich\",\"district\":\"mist\",\"ward\":5,\"kind\":\"room\",\"plot\":23,\"room\":3}",
            new SiteHome("Lich", "mist", 5, "room", Plot: 23, Room: 3).ToJson().ToJsonString());
    }

    [Fact]
    public void SaysTheHomeInWords()
    {
        Assert.Equal("Lich, Mist, ward 5, plot 23", new SiteHome("Lich", "mist", 5, "house", Plot: 23).Describe());
        Assert.Equal("Lich, Lavender Beds, ward 2 (subdivision), apartment 7",
            new SiteHome("Lich", "lavender-beds", 2, "apartment", Room: 7, Subdivision: true).Describe());
    }

    [Theory]
    [InlineData("s1h1", "mist")]
    [InlineData("s1i2", "mist")]
    [InlineData("f1h1", "lavender-beds")]
    [InlineData("w1i4", "goblet")]
    [InlineData("e1h1", "shirogane")]
    [InlineData("R1I3", "empyreum")]
    [InlineData("h1i2", null)]
    [InlineData("s1", null)]
    [InlineData(null, null)]
    public void KnowsADistrictByItsTerritorysName(string? name, string? district) =>
        Assert.Equal(district, Districts.OfTerritoryName(name));
}

public class ShotPoseTests
{
    private static readonly Vector3 Eye = new(12.5f, 3.2f, -7.25f);
    private static readonly Vector3 At = new(2f, 1.5f, 4f);

    private static void Near(Vector3 expected, Vector3 actual, float within = 1e-3f) =>
        Assert.True(Vector3.Distance(expected, actual) <= within, $"{actual} isn't {expected}");

    [Fact]
    public void ReadsTheCameraFromARightHandedView()
    {
        var view = Matrix4x4.CreateLookAt(Eye, At, Vector3.UnitY);
        var projection = Matrix4x4.CreatePerspectiveFieldOfView(45 * MathF.PI / 180, 16f / 9, 0.1f, 1000);
        Assert.True(CameraMath.FromView(view, projection, null, out var eye, out var forward));
        Near(Eye, eye);
        Near(Vector3.Normalize(At - Eye), forward);
    }

    [Fact]
    public void ReadsTheCameraFromALeftHandedView()
    {
        var view = Matrix4x4.CreateLookAtLeftHanded(Eye, At, Vector3.UnitY);
        var projection = Matrix4x4.CreatePerspectiveFieldOfViewLeftHanded(60 * MathF.PI / 180, 16f / 9, 0.1f, 1000);
        Assert.True(CameraMath.FromView(view, projection, null, out var eye, out var forward));
        Near(Eye, eye);
        Near(Vector3.Normalize(At - Eye), forward);
    }

    [Fact]
    public void WithoutAPerspectiveTheLookAtPointSaysWhichWay()
    {
        var view = Matrix4x4.CreateLookAt(Eye, At, Vector3.UnitY);
        Assert.True(CameraMath.FromView(view, Matrix4x4.Identity, At, out _, out var forward));
        Near(Vector3.Normalize(At - Eye), forward);
    }

    [Theory]
    [InlineData(45f)]
    [InlineData(30f)]
    [InlineData(74.5f)]
    public void TheVerticalFieldOfViewInDegrees(float degrees)
    {
        var projection = Matrix4x4.CreatePerspectiveFieldOfView(degrees * MathF.PI / 180, 21f / 9, 0.1f, 1000);
        Assert.Equal(degrees, CameraMath.VerticalFov(projection)!.Value, 2);
        // Reversed depth and an infinite far plane change the third column, not the y scale.
        var reversed = projection;
        reversed.M33 = 0;
        reversed.M43 = 0.1f;
        Assert.Equal(degrees, CameraMath.VerticalFov(reversed)!.Value, 2);
        Assert.Null(CameraMath.VerticalFov(new Matrix4x4()));
    }

    [Fact]
    public void ARadianFieldOfViewInDegrees()
    {
        Assert.Equal(45f, CameraMath.Degrees(MathF.PI / 4)!.Value, 3);
        Assert.Null(CameraMath.Degrees(0));
        Assert.Null(CameraMath.Degrees(float.NaN));
        Assert.Null(CameraMath.Degrees(45));
    }

    [Fact]
    public void InsideThePoseIsTheHousesOwn()
    {
        var view = Matrix4x4.CreateLookAt(Eye, At, Vector3.UnitY);
        var projection = Matrix4x4.CreatePerspectiveFieldOfView(45 * MathF.PI / 180, 16f / 9, 0.1f, 1000);
        var pose = CameraMath.Pose(view, projection, 0, null, Side.Interior, null, out var why)!.Value;
        Assert.Equal(string.Empty, why);
        Near(Eye, pose.Pos);
        Near(Eye + Vector3.Normalize(At - Eye) * CameraMath.TargetDistance, pose.Target);
        Assert.Equal(45f, pose.Fov, 2);
        Assert.Equal(Side.Interior, pose.Side);
    }

    [Fact]
    public void AGardensPoseIsInItsPlotsFrame()
    {
        var plot = new PlotSpot(12, "m", 100f, 4f, -60f, 1.2f);
        var eye = new Vector3(105f, 9f, -52f);
        var at = new Vector3(100f, 4f, -60f);
        var view = Matrix4x4.CreateLookAt(eye, at, Vector3.UnitY);
        var projection = Matrix4x4.CreatePerspectiveFieldOfView(50 * MathF.PI / 180, 16f / 9, 0.1f, 1000);
        var pose = CameraMath.Pose(view, projection, 0, null, Side.Garden, plot, out _)!.Value;
        var target = eye + Vector3.Normalize(at - eye) * CameraMath.TargetDistance;
        Near(Transforms.WardToPlot(eye, 0, plot).Position, pose.Pos);
        Near(Transforms.WardToPlot(target, 0, plot).Position, pose.Target);
        // The plot's centre is its frame's origin: the camera looks at it from the same distance.
        Assert.Equal(Vector3.Distance(eye, at), pose.Pos.Length(), 2);
        Assert.Equal(Side.Garden, pose.Side);
        Assert.Null(CameraMath.Pose(view, projection, 0, null, Side.Garden, null, out var why));
        Assert.Contains("plot", why);
    }

    [Fact]
    public void FallsBackToTheCamerasOwnFieldOfView()
    {
        var view = Matrix4x4.CreateLookAt(Eye, At, Vector3.UnitY);
        var pose = CameraMath.Pose(view, Matrix4x4.Identity with { M22 = 0 }, MathF.PI / 3, At, Side.Interior, null, out _)!.Value;
        Assert.Equal(60f, pose.Fov, 2);
    }

    [Fact]
    public void RefusesACameraOutOfReach()
    {
        var far = new Vector3(6000, 0, 0);
        var view = Matrix4x4.CreateLookAt(far, far + Vector3.UnitX, Vector3.UnitY);
        var projection = Matrix4x4.CreatePerspectiveFieldOfView(1, 1, 0.1f, 1000);
        Assert.Null(CameraMath.Pose(view, projection, 0, null, Side.Interior, null, out var why));
        Assert.Contains("closer", why);
    }

    [Fact]
    public void WritesThePoseAsTheSiteKeepsIt()
    {
        var pose = new ShotPose(new Vector3(1.234567f, -0.00001f, 3f), new Vector3(4f, 5.5f, -6f), 45.12345f, Side.Garden);
        var json = JsonNode.Parse(pose.ToJson())!.AsObject();
        Assert.Equal(1.2346, json["pos"]![0]!.GetValue<double>(), 6);
        Assert.Equal(0, json["pos"]![1]!.GetValue<double>());
        Assert.Equal(3, json["target"]!.AsArray().Count);
        Assert.Equal(45.123, json["fov"]!.GetValue<double>(), 6);
        Assert.Equal("garden", json["side"]!.GetValue<string>());
        Assert.Equal("inside", JsonNode.Parse((pose with { Side = Side.Interior }).ToJson())!["side"]!.GetValue<string>());
    }

    [Fact]
    public void ThePoseKeepsTheSitesLimits()
    {
        Assert.True(new ShotPose(Vector3.Zero, Vector3.UnitX, 45, Side.Interior).Valid);
        Assert.False(new ShotPose(Vector3.Zero, Vector3.UnitX, 4, Side.Interior).Valid);
        Assert.False(new ShotPose(Vector3.Zero, Vector3.UnitX, 121, Side.Interior).Valid);
        Assert.False(new ShotPose(Vector3.Zero, Vector3.Zero, 45, Side.Interior).Valid);
        Assert.False(new ShotPose(new Vector3(0, 5001, 0), Vector3.UnitX, 45, Side.Interior).Valid);
        Assert.False(new ShotPose(new Vector3(float.NaN, 0, 0), Vector3.UnitX, 45, Side.Interior).Valid);
    }

    [Fact]
    public void TheSamePoseIsAStillCamera()
    {
        var a = new ShotPose(new Vector3(1, 2, 3), new Vector3(1, 2, 13), 45, Side.Interior);
        Assert.True(CameraMath.Same(a, a with { Pos = new Vector3(1.01f, 2, 3), Target = new Vector3(1.01f, 2, 13) }));
        Assert.False(CameraMath.Same(a, a with { Pos = new Vector3(1.2f, 2, 3), Target = new Vector3(1.2f, 2, 13) }));
        Assert.False(CameraMath.Same(a, a with { Target = new Vector3(2, 2, 13) }));
        Assert.False(CameraMath.Same(a, a with { Fov = 50 }));
        Assert.False(CameraMath.Same(a, a with { Side = Side.Garden }));
    }

    [Theory]
    [InlineData(3840, 2160, 2560, 1440)]
    [InlineData(1920, 1080, 1920, 1080)]
    [InlineData(2160, 3840, 1440, 2560)]
    [InlineData(5120, 1440, 2560, 720)]
    public void AShotIs2560AtMostOnItsLongSide(int w, int h, int ew, int eh) =>
        Assert.Equal((ew, eh), ShotSize.Fit(w, h));

    [Fact]
    public void ATinyPictureIsntSent()
    {
        Assert.Null(ShotSize.Fit(40, 40));
        Assert.Equal((1920, 1080), ShotSize.Fit(3840, 2160, ShotSize.SmallerLongEdge));
        Assert.Equal((480, 270), ShotSize.Thumb(2560, 1440));
        Assert.True(ShotSize.MaxBytes < 4 * 1024 * 1024);
        Assert.Equal(0.9f, ShotSize.Qualities[0]);
        Assert.Equal("1.8 MB", ShotSize.Bytes(1_887_437));
        Assert.Equal("860 KB", ShotSize.Bytes(860 * 1024));
    }
}

public class SiteCallsTests
{
    private const string Token = "hsp_A1b2_C3d-A1b2_C3d-A1b2_C3d-A1b2_C3d-abcdefg";
    private const string Id = "14b7ec3b-e17c-45b1-b416-22ba4367c37d";
    private const string Other = "0a1b2c3d-4e5f-4a6b-8c7d-9e0f1a2b3c4d";

    private sealed class Fake(Func<HttpRequestMessage, string?, HttpResponseMessage> answer) : HttpMessageHandler
    {
        public HttpRequestMessage? Seen;
        public string? SeenBody;
        public string? SeenType;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Seen = request;
            SeenType = request.Content?.Headers.ContentType?.ToString();
            SeenBody = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return answer(request, SeenBody);
        }
    }

    private static Fake Answer(HttpStatusCode status, string body) =>
        new((_, _) => new HttpResponseMessage(status) { Content = new StringContent(body) });

    [Fact]
    public void ReadsTheInboxNewestFirst()
    {
        var list = SiteLink.ReadInbox(JsonNode.Parse($$"""
            [
              {"id":"{{Other}}","kind":"build","ref_id":"{{Id}}","name":"Old","created_at":"2026-10-30T10:00:00+00:00","fetched_at":null},
              {"id":"{{Id}}","kind":"design","ref_id":"{{Other}}","name":"  Cozy   Café ","created_at":"2026-10-31T19:47:06.123456+00:00","fetched_at":"2026-10-31T20:00:00+00:00"},
              {"id":"nope","kind":"design","name":"Bad","created_at":"2026-10-31T19:47:06+00:00"},
              {"id":"{{Id}}","kind":"other","name":"Bad","created_at":"2026-10-31T19:47:06+00:00"}
            ]
            """))!;
        Assert.Equal(2, list.Count);
        Assert.Equal("Cozy Café", list[0].Name);
        Assert.Equal("Design", list[0].KindWord);
        Assert.NotNull(list[0].FetchedAt);
        Assert.Equal("Build", list[1].KindWord);
        Assert.Null(list[1].FetchedAt);
        Assert.Empty(SiteLink.ReadInbox(null)!);
        Assert.Null(SiteLink.ReadInbox(JsonNode.Parse("{}")));
    }

    [Fact]
    public void SaysWhatArrived()
    {
        var a = new InboxItem(Id, "design", Other, "Cozy Café", DateTimeOffset.UtcNow, null);
        Assert.Null(SiteLink.Arrived([]));
        Assert.Equal("“Cozy Café” arrived from Housing Studio. /hstudio to open it.", SiteLink.Arrived([a]));
        Assert.Equal("“Cozy Café” and 2 more arrived from Housing Studio. /hstudio to open them.", SiteLink.Arrived([a, a, a]));
    }

    [Fact]
    public void ReadsAnInboxEntryWithItsLayout()
    {
        var entry = SiteLink.ReadInboxEntry(JsonNode.Parse("""{"name":"Cozy Café","file_name":"cozy.build","house":"s1i2","layout":{"interiorFurniture":[]}}"""))!;
        Assert.Equal("cozy.build", entry.FileName);
        Assert.Equal("s1i2", entry.House);
        Assert.Equal(JsonValueKind.Object, JsonDocument.Parse(entry.LayoutJson).RootElement.ValueKind);
        Assert.Null(SiteLink.ReadInboxEntry(JsonNode.Parse("""{"name":"x","file_name":"x.json","layout":null}""")));
    }

    [Theory]
    [InlineData("Cozy Café.build", "x", "Cozy Café.build")]
    [InlineData("Winter Lodge.JSON", "x", "Winter Lodge.json")]
    [InlineData("..\\..\\evil/../win.ini", "x", "win.ini.json")]
    [InlineData("a:b*c?.build", "x", "a b c.build")]
    [InlineData("noext", "x", "noext.json")]
    [InlineData("", "My  Home", "My Home.json")]
    [InlineData(".build", "", "From Housing Studio.build")]
    [InlineData(null, null, "From Housing Studio.json")]
    [InlineData("CON.build", "x", "_CON.build")]
    [InlineData("   .json", "Named", "Named.json")]
    public void WritesASafeFileName(string? fileName, string? name, string expected) =>
        Assert.Equal(expected, SiteLink.SafeFileName(fileName, name));

    [Fact]
    public void ALongFileNameIsCut() =>
        Assert.Equal(80 + ".build".Length, SiteLink.SafeFileName(new string('a', 200) + ".build", null).Length);

    [Fact]
    public void ReadsYourDesignsAtThisHome()
    {
        var list = SiteLink.ReadMyDesigns(JsonNode.Parse($$"""
            [{"id":"{{Id}}","name":"Cozy Café","visibility":"published","verified_at":"2026-10-31T19:00:00+00:00"},
             {"id":"{{Other}}","name":"Draft","visibility":"private","verified_at":null}]
            """))!;
        Assert.True(list[0].Verified);
        Assert.False(list[1].Verified);
        Assert.Equal("private", list[1].Visibility);
    }

    [Fact]
    public void ReadsTheDesignsOfAHomeYouVisit()
    {
        var list = SiteLink.ReadHere(JsonNode.Parse($$"""
            [{"id":"{{Id}}","name":"Cozy Café","maker":{"handle":"koko","name":"Koko"},"cover":"u/d/cover one.webp",
              "verified_at":"2026-10-31T19:00:00+00:00","own":false,
              "social":{"likes":12,"dislikes":null,"my_vote":1,"favourites":3,"is_favourite":false,"comments":4},
              "stamps":{"on":true,"total":9,"mine_today":true} },
             {"id":"{{Other}}","name":"","maker":{"handle":null,"name":"Someone"},"cover":null,"own":true,"social":null,"stamps":null}]
            """))!;
        var d = list[0];
        Assert.Equal("Koko", d.Maker.Name);
        Assert.Equal("koko", d.Maker.Handle);
        Assert.Equal("u/d/cover one.webp", d.Cover);
        Assert.True(d.Social.Liked);
        Assert.Equal(12, d.Social.Likes);
        Assert.Equal(3, d.Social.Favourites);
        Assert.True(d.Stamps.On);
        Assert.Equal(9, d.Stamps.Total);
        Assert.True(d.Stamps.MineToday);
        Assert.Equal("Untitled", list[1].Name);
        Assert.True(list[1].Own);
        Assert.False(list[1].Stamps.On);
        Assert.Null(list[1].Cover);
    }

    [Fact]
    public void LinksToAPhotoAndItsSmallCopy()
    {
        Assert.Equal($"{SiteLink.SupabaseUrl}/storage/v1/object/public/hs-photos/u/d/cover%20one-s.webp", SiteLink.PhotoUrl("u/d/cover one.webp", small: true));
        Assert.Equal($"{SiteLink.SupabaseUrl}/storage/v1/object/public/hs-photos/u/d/cover%20one.webp", SiteLink.PhotoUrl("u/d/cover one.webp"));
        Assert.Equal($"https://www.kokos-tools.com/housing-studio/d/{Id}", SiteLink.DesignUrl(Id));
    }

    [Fact]
    public void ARefusalIsTheSitesOwnWords()
    {
        var (why, hint) = SiteLink.Refusal(HttpStatusCode.Forbidden, "{\"code\":\"28000\",\"message\":\"That token was revoked, or never made. Make a new one on the site.\",\"hint\":\"token\"}");
        Assert.Equal("token", hint);
        Assert.Contains("Link again", why);
        (why, hint) = SiteLink.Refusal(HttpStatusCode.NotFound, "{\"code\":\"P0002\",\"message\":\"That isn’t waiting any more. Send it again from the site.\",\"hint\":\"gone\"}");
        Assert.Equal(("That isn’t waiting any more. Send it again from the site.", "gone"), (why, hint));
        // The site's rules for likes and stamps say no in words, without a hint.
        (why, hint) = SiteLink.Refusal(HttpStatusCode.BadRequest, "{\"code\":\"P0001\",\"message\":\"You can’t stamp your own home.\",\"hint\":null}");
        Assert.Equal(("You can’t stamp your own home.", (string?)null), (why, hint));
        // The database's own words aren't for players.
        (why, _) = SiteLink.Refusal(HttpStatusCode.NotFound, "{\"code\":\"PGRST202\",\"message\":\"Could not find the function public.hs_plugin_here\"}");
        Assert.Contains("may need updating", why);
        (why, hint) = SiteLink.Refusal(HttpStatusCode.BadGateway, "<html>bad gateway</html>");
        Assert.Contains("(502)", why);
        Assert.Equal("server", hint);
    }

    [Fact]
    public void ReadsTheShotsAnswers()
    {
        var ok = SiteLink.ReadShot(HttpStatusCode.OK, $"{{\"id\":\"{Id}\",\"path\":\"u/d/shot-{Id}.jpg\",\"name\":\"Cozy Café\"}}");
        Assert.True(ok.Ok);
        Assert.Equal("Cozy Café", ok.Value!.Name);
        var full = SiteLink.ReadShot((HttpStatusCode)429, "{\"message\":\"“Cozy Café” has 12 shots from the game. Delete one on the site to send another.\",\"hint\":\"full\"}");
        Assert.Equal("full", full.Hint);
        Assert.Contains("Delete one", full.Why);
        var verify = SiteLink.ReadShot(HttpStatusCode.Forbidden, "{\"message\":\"Verify this home as “Cozy Café” first.\",\"hint\":\"verify\"}");
        Assert.Equal("verify", verify.Hint);
        Assert.True(SiteLink.ReadShot(HttpStatusCode.Unauthorized, "{\"message\":\"That isn’t a Housing Studio plugin token.\",\"hint\":\"token\"}").TokenRefused);
        Assert.Contains("4 MB", SiteLink.ReadShot(HttpStatusCode.RequestEntityTooLarge, "").Why);
        var down = SiteLink.ReadShot(HttpStatusCode.BadGateway, "{\"message\":\"The picture couldn’t be kept. Try again in a minute.\",\"hint\":\"upload\"}");
        Assert.Equal("The picture couldn’t be kept. Try again in a minute.", down.Why);
        Assert.False(SiteLink.ReadShot(HttpStatusCode.OK, "{}").Ok);
    }

    [Fact]
    public async Task ChecksTheInboxWithTheTokenInTheBodyOnly()
    {
        var fake = Answer(HttpStatusCode.OK, "[]");
        var r = await SiteLink.InboxAsync(new HttpClient(fake), Token);
        Assert.True(r.Ok);
        Assert.Empty(r.Value!);
        Assert.Equal($"{SiteLink.SupabaseUrl}/rest/v1/rpc/hs_plugin_inbox_list", fake.Seen!.RequestUri!.ToString());
        Assert.Equal(SiteLink.PublishableKey, Assert.Single(fake.Seen.Headers.GetValues("apikey")));
        Assert.DoesNotContain(Token, fake.Seen.RequestUri.ToString());
        Assert.Equal(Token, JsonNode.Parse(fake.SeenBody!)!["p_token"]!.GetValue<string>());
    }

    [Fact]
    public async Task ARefusedTokenSaysSo()
    {
        var fake = Answer(HttpStatusCode.Forbidden, "{\"code\":\"28000\",\"message\":\"That token was revoked, or never made.\",\"hint\":\"token\"}");
        var r = await SiteLink.InboxAsync(new HttpClient(fake), Token);
        Assert.True(r.TokenRefused);
        Assert.False(r.Passing);
        var none = await SiteLink.InboxAsync(new HttpClient(Answer(HttpStatusCode.OK, "[]")), "nope");
        Assert.True(none.TokenRefused);
    }

    [Fact]
    public async Task NoConnectionIsSaidInWords()
    {
        var down = new Fake((_, _) => throw new HttpRequestException("No such host"));
        var r = await SiteLink.HomeAsync(new HttpClient(down), Token, new SiteHome("Lich", "mist", 5, "house", Plot: 23));
        Assert.False(r.Ok);
        Assert.Equal("offline", r.Hint);
        Assert.True(r.Passing);
    }

    [Fact]
    public async Task AsksAboutAHomeAsAnyoneWithoutAToken()
    {
        var fake = Answer(HttpStatusCode.OK, "[]");
        var r = await SiteLink.HereAsync(new HttpClient(fake), null, new SiteHome("Lich", "mist", 5, "house", Plot: 23));
        Assert.True(r.Ok);
        var body = JsonNode.Parse(fake.SeenBody!)!;
        Assert.Equal(string.Empty, body["p_token"]!.GetValue<string>());
        Assert.Equal(23, body["p_home"]!["plot"]!.GetValue<int>());
        Assert.EndsWith("/rpc/hs_plugin_here", fake.Seen!.RequestUri!.ToString());
    }

    [Fact]
    public async Task OnlyAStampSendsTheHome()
    {
        var fake = Answer(HttpStatusCode.OK, "{\"social\":{\"likes\":1,\"my_vote\":1},\"stamps\":{\"on\":true,\"total\":2,\"mine_today\":true}}");
        var home = new SiteHome("Lich", "mist", 5, "house", Plot: 23);
        var r = await SiteLink.ActAsync(new HttpClient(fake), Token, Id, "stamp", true, home);
        Assert.True(r.Value!.Stamps.MineToday);
        Assert.Equal(23, JsonNode.Parse(fake.SeenBody!)!["p_home"]!["plot"]!.GetValue<int>());
        await SiteLink.ActAsync(new HttpClient(fake), Token, Id, "like", false, null);
        var body = JsonNode.Parse(fake.SeenBody!)!;
        Assert.Null(body["p_home"]);
        Assert.False(body["p_on"]!.GetValue<bool>());
        Assert.Equal("like", body["p_act"]!.GetValue<string>());
        Assert.Equal("act", (await SiteLink.ActAsync(new HttpClient(fake), Token, Id, "dislike", true, null)).Hint);
    }

    [Fact]
    public async Task VerifiesWithTheHome()
    {
        var fake = Answer(HttpStatusCode.OK, $"{{\"id\":\"{Id}\",\"name\":\"Cozy Café\",\"verified_at\":\"2026-10-31T19:00:00+00:00\"}}");
        var r = await SiteLink.VerifyAsync(new HttpClient(fake), Token, Id, new SiteHome("Lich", "mist", 5, "apartment", Room: 41, Subdivision: false));
        Assert.True(r.Value!.Verified);
        var body = JsonNode.Parse(fake.SeenBody!)!;
        Assert.True(body["p_on"]!.GetValue<bool>());
        Assert.Equal("apartment", body["p_home"]!["kind"]!.GetValue<string>());
        var address = Answer(HttpStatusCode.BadRequest, "{\"code\":\"22023\",\"message\":\"This home isn’t the address “Cozy Café” has on the site. Change its address there first.\",\"hint\":\"address\"}");
        var refused = await SiteLink.VerifyAsync(new HttpClient(address), Token, Id, new SiteHome("Lich", "mist", 5, "house", Plot: 1));
        Assert.Equal("address", refused.Hint);
        Assert.StartsWith("This home isn’t the address", refused.Why);
    }

    [Fact]
    public async Task TakesSomethingOffTheInbox()
    {
        Assert.True((await SiteLink.InboxRemoveAsync(new HttpClient(Answer(HttpStatusCode.OK, "true")), Token, Id)).Value);
        Assert.False((await SiteLink.InboxRemoveAsync(new HttpClient(Answer(HttpStatusCode.OK, "false")), Token, Id)).Value);
        var gone = await SiteLink.InboxGetAsync(new HttpClient(Answer(HttpStatusCode.NotFound,
            "{\"code\":\"P0002\",\"message\":\"That isn’t waiting any more. Send it again from the site.\",\"hint\":\"gone\"}")), Token, Id);
        Assert.True(gone.Gone);
    }

    [Fact]
    public async Task PostsAShotAsAForm()
    {
        var fake = Answer(HttpStatusCode.OK, $"{{\"id\":\"{Id}\",\"path\":\"p\",\"name\":\"Cozy Café\"}}");
        var jpeg = new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 };
        var pose = new ShotPose(Vector3.Zero, Vector3.UnitZ, 45, Side.Interior).ToJson();
        var r = await SiteLink.ShotAsync(new HttpClient(fake), Token, Id, pose, jpeg);
        Assert.True(r.Ok);
        Assert.Equal($"{SiteLink.SupabaseUrl}/functions/v1/hs-plugin-shot", fake.Seen!.RequestUri!.ToString());
        Assert.Equal(SiteLink.PublishableKey, Assert.Single(fake.Seen.Headers.GetValues("apikey")));
        Assert.StartsWith("multipart/form-data", fake.SeenType);
        Assert.Contains("name=\"token\"", fake.SeenBody);
        Assert.Contains("name=\"design\"", fake.SeenBody);
        Assert.Contains("name=\"pose\"", fake.SeenBody);
        Assert.Contains("name=\"image\"", fake.SeenBody);
        Assert.Contains("filename=\"shot.jpg\"", fake.SeenBody);
        Assert.Contains("image/jpeg", fake.SeenBody);
        Assert.Contains("\"side\":\"inside\"", fake.SeenBody);
        Assert.DoesNotContain(Token, fake.Seen.RequestUri.ToString());

        var tooBig = await SiteLink.ShotAsync(new HttpClient(fake), Token, Id, pose, new byte[ShotSize.MaxBytes + 1]);
        Assert.Equal("image", tooBig.Hint);
    }

    [Fact]
    public void TheNewIconsAreLucidesPaths()
    {
        string[] paths =
        {
            "M14.5 4h-5L7 7H4a2 2 0 0 0-2 2v9a2 2 0 0 0 2 2h16a2 2 0 0 0 2-2V9a2 2 0 0 0-2-2h-3l-2.5-3z",
            "M19 14c1.49-1.46 3-3.21 3-5.5A5.5 5.5 0 0 0 16.5 3c-1.76 0-3 .5-4.5 2-1.5-1.5-2.74-2-4.5-2A5.5 5.5 0 0 0 2 8.5c0 2.3 1.5 4.05 3 5.5l7 7Z",
            "m19 21-7-4-7 4V5a2 2 0 0 1 2-2h10a2 2 0 0 1 2 2v16z",
            "M19.27 13.73A2.5 2.5 0 0 0 17.5 13h-11A2.5 2.5 0 0 0 4 15.5V17a1 1 0 0 0 1 1h14a1 1 0 0 0 1-1v-1.5c0-.66-.26-1.3-.73-1.77Z",
            "M14 13V8.5C14 7 15 7 15 5a3 3 0 0 0-6 0c0 2 1 2 1 3.5V13",
            "M15 3h6v6", "M10 14 21 3", "M18 13v6a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h6",
            "M22 12h-6l-2 3h-4l-2-3H2", "M5.45 5.11 2 12v6a2 2 0 0 0 2 2h16a2 2 0 0 0 2-2v-6l-3.45-6.89A2 2 0 0 0 16.76 4H7.24a2 2 0 0 0-1.79 1.11z",
            "m15 18-6-6 6-6",
        };
        foreach (var d in paths)
        {
            var subs = SvgPath.Parse(d);
            Assert.NotEmpty(subs);
            // The icon drawer keeps 128 points a run.
            Assert.All(subs, s => Assert.InRange(s.Points.Count, 1, 128));
        }
    }
}
