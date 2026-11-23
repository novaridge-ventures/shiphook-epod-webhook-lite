using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using EpodWebhookLite;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IIdempotencyStore, InMemoryIdempotencyStore>();
var app = builder.Build();

var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);

app.MapGet("/", () => "ePOD webhook receiver (lite). POST /webhooks/epod/{provider}");

// POST /webhooks/epod/{provider}
//   X-Timestamp: unix seconds
//   X-Signature: sha256=<hex HMAC-SHA256 of "{timestamp}.{rawBody}">
// Secret per provider in config: Webhooks:Inbound:{provider}:Secret
app.MapPost("/webhooks/epod/{provider}", async (string provider, HttpRequest http, IConfiguration config,
    TimeProvider clock, IIdempotencyStore seen, ILogger<Program> log, CancellationToken ct) =>
{
    var secret = config[$"Webhooks:Inbound:{provider}:Secret"];
    if (string.IsNullOrEmpty(secret)) return Results.NotFound(new { title = $"Unknown provider '{provider}'" });

    // 1. Read the RAW body. Verify the bytes that were signed, not a re-serialised object.
    using var reader = new StreamReader(http.Body);
    var body = await reader.ReadToEndAsync(ct);

    // 2. Verify signature and replay window before trusting anything in the payload.
    var tolerance = TimeSpan.FromSeconds(config.GetValue("Webhooks:ToleranceSeconds", 300));
    if (!WebhookSignature.Verify(secret, http.Headers[WebhookSignature.TimestampHeader], body,
            http.Headers[WebhookSignature.SignatureHeader], tolerance, clock.GetUtcNow()))
    {
        log.LogWarning("Rejected ePOD webhook from {Provider}: bad signature or timestamp", provider);
        return Results.Unauthorized();
    }

    // 3. Parse and validate.
    EpodEvent? evt;
    try { evt = JsonSerializer.Deserialize<EpodEvent>(body, json); }
    catch (JsonException) { return Results.Problem("Body is not valid JSON.", statusCode: 400); }
    if (evt is null) return Results.Problem("Empty body.", statusCode: 400);
    var errors = new List<ValidationResult>();
    if (!Validator.TryValidateObject(evt, new ValidationContext(evt), errors, true))
        return Results.ValidationProblem(errors.GroupBy(e => e.MemberNames.FirstOrDefault() ?? "")
            .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage ?? "invalid").ToArray()));

    // 4. Idempotency: acknowledge duplicates with 200 so the provider stops retrying, but don't apply them twice.
    if (!seen.TryRecord(provider, evt.EventId))
        return Results.Ok(new { status = "duplicate", evt.EventId });

    // 5. Apply your business change here (mark the shipment delivered, store the POD...).
    log.LogInformation("POD for {Ref} signed by {By} at {At}", evt.ShipmentReference, evt.ReceivedBy, evt.SignedAt);
    return Results.Ok(new { status = "applied", evt.EventId, shipment = evt.ShipmentReference });
});

app.Run();

public record EpodEvent(
    [property: Required] string EventId,
    [property: Required] string ShipmentReference,
    [property: Required] string ReceivedBy,
    DateTimeOffset SignedAt,
    double? Latitude,
    double? Longitude,
    string? Outcome);

public partial class Program;
