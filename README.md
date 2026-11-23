# ePOD Webhook Receiver (lite): signed, replay-safe, idempotent webhooks in .NET 8

A small, tested ASP.NET Core 8 minimal API that receives **electronic proof-of-delivery (ePOD)** callbacks from a driver app or ePOD provider and does the three things most webhook endpoints get wrong:

1. **Verifies an HMAC-SHA256 signature over the raw body.** It uses a constant-time compare and never checks a re-serialised object.
2. **Rejects replays.** The timestamp is part of the signed content, and requests outside a 5-minute window are refused.
3. **Is idempotent.** Providers retry, so the same `eventId` is acknowledged with `200` but applied only once.

```
POST /webhooks/epod/{provider}
X-Timestamp: 1795392111
X-Signature: sha256=<hex HMAC-SHA256 of "{timestamp}.{rawBody}">

{"eventId":"evt-123","shipmentReference":"SHP-42","receivedBy":"J. Receiver","signedAt":"2026-11-23T08:00:00Z"}
```

| Response | When |
|---|---|
| `200 {"status":"applied"}` | First valid delivery of the event |
| `200 {"status":"duplicate"}` | Same provider and eventId seen before (provider retry) |
| `401` | Missing or wrong signature, or a timestamp outside the window |
| `400` | Invalid JSON or a missing required field |
| `404` | No secret configured for that provider |

## Run it

```bash
dotnet run --project src/EpodWebhookLite --urls http://localhost:5080
bash samples/send-epod.sh SHP-42          # signs and sends a sample event
dotnet test                               # 8 tests: valid, duplicate, wrong secret, missing signature, replay, unknown provider, validation, tampered body
```

Set one secret per provider in `appsettings.json` (`Webhooks:Inbound:{provider}:Secret`). In production, use user-secrets or environment variables instead.

## Signing on the sender's side

```csharp
var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
var signature = WebhookSignature.Compute(secret, ts, body);   // "sha256=..."
request.Headers.Add("X-Timestamp", ts);
request.Headers.Add("X-Signature", signature);
```

```bash
SIG="sha256=$(printf '%s' "$TS.$BODY" | openssl dgst -sha256 -hmac "$SECRET" -hex | awk '{print $NF}')"
```

## What this sample deliberately leaves out

- **Durable idempotency.** `InMemoryIdempotencyStore` forgets on restart and isn't shared between instances. In production, insert `(provider, eventId)` into a table with a UNIQUE index **in the same transaction** as your business change, and treat a unique-key violation as a duplicate.
- **Business rules.** There's no shipment model or state machine here. A delivered parcel shouldn't go back to "in transit".
- **Notifying downstream systems reliably.** Calling another API straight from the webhook handler loses events when that API is down. Use a transactional outbox instead.

## Want the full kit?

**[ShipHook: .NET 8 Logistics Integration Starter Kit](https://payhip.com/b/j4THg?utm_source=github&utm_medium=readme&utm_campaign=shiphook-lite)** ($29) is the complete version of this sample:

- A shipments API with a status state machine, event history and API-key auth.
- EF Core on SQLite or SQL Server.
- ePOD webhooks with **database-backed** idempotency.
- A **transactional outbox** that sends signed `shipment.delivered` webhooks to subscribers, with exponential backoff and dead-lettering.
- Health checks, Swagger, a Dockerfile, PowerShell and bash test senders, and 18 xUnit tests.

This sample stays free under MIT either way.

## Licence

MIT © 2026 Novaridge Ventures.
