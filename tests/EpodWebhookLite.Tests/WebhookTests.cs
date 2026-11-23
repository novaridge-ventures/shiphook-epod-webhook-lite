using System.Net;
using System.Text;
using EpodWebhookLite;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace EpodWebhookLite.Tests;

public class TestApp : WebApplicationFactory<Program>
{
    public const string Secret = "test-secret";
    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.UseSetting("Webhooks:Inbound:demo-epod:Secret", Secret);
}

public class WebhookTests : IClassFixture<TestApp>
{
    private readonly HttpClient _client;
    public WebhookTests(TestApp app) => _client = app.CreateClient();

    private static string Body(string eventId) =>
        $$"""{"eventId":"{{eventId}}","shipmentReference":"SHP-1001","receivedBy":"J. Receiver","signedAt":"2026-11-23T08:00:00Z"}""";

    private Task<HttpResponseMessage> Send(string body, string? secret = TestApp.Secret, DateTimeOffset? at = null, string provider = "demo-epod")
    {
        var ts = (at ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds().ToString();
        var req = new HttpRequestMessage(HttpMethod.Post, $"/webhooks/epod/{provider}")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        req.Headers.Add(WebhookSignature.TimestampHeader, ts);
        if (secret is not null) req.Headers.Add(WebhookSignature.SignatureHeader, WebhookSignature.Compute(secret, ts, body));
        return _client.SendAsync(req);
    }

    [Fact]
    public async Task Valid_signature_is_applied()
    {
        var res = await Send(Body("evt-valid"));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Contains("\"applied\"", await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Same_event_twice_is_acknowledged_but_not_applied_again()
    {
        await Send(Body("evt-dup"));
        var second = await Send(Body("evt-dup"));
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Contains("\"duplicate\"", await second.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Wrong_secret_is_rejected() =>
        Assert.Equal(HttpStatusCode.Unauthorized, (await Send(Body("evt-bad"), secret: "wrong")).StatusCode);

    [Fact]
    public async Task Missing_signature_is_rejected() =>
        Assert.Equal(HttpStatusCode.Unauthorized, (await Send(Body("evt-nosig"), secret: null)).StatusCode);

    [Fact]
    public async Task Old_timestamp_is_rejected_as_replay() =>
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await Send(Body("evt-old"), at: DateTimeOffset.UtcNow.AddMinutes(-10))).StatusCode);

    [Fact]
    public async Task Unknown_provider_is_404() =>
        Assert.Equal(HttpStatusCode.NotFound, (await Send(Body("evt-x"), provider: "nobody")).StatusCode);

    [Fact]
    public async Task Missing_required_field_is_400()
    {
        var res = await Send("""{"eventId":"evt-invalid","receivedBy":"x"}""");
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public void Tampered_body_fails_verification()
    {
        var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var sig = WebhookSignature.Compute("s", ts, "{\"a\":1}");
        Assert.False(WebhookSignature.Verify("s", ts, "{\"a\":2}", sig, TimeSpan.FromMinutes(5), DateTimeOffset.UtcNow));
        Assert.True(WebhookSignature.Verify("s", ts, "{\"a\":1}", sig, TimeSpan.FromMinutes(5), DateTimeOffset.UtcNow));
    }
}
