using System.Security.Cryptography;
using System.Text;

namespace EpodWebhookLite;

/// <summary>
/// HMAC-SHA256 webhook signatures.
/// Headers: X-Timestamp: unix seconds, X-Signature: sha256=lowercase hex.
/// Signed content: "{timestamp}.{rawBody}". Signing the timestamp lets the receiver reject replays.
/// </summary>
public static class WebhookSignature
{
    public const string SignatureHeader = "X-Signature";
    public const string TimestampHeader = "X-Timestamp";

    public static string Compute(string secret, string timestamp, string body)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes($"{timestamp}.{body}"));
        return "sha256=" + Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static bool Verify(string secret, string? timestamp, string body, string? signatureHeader,
        TimeSpan tolerance, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(signatureHeader) || !long.TryParse(timestamp, out var unix)) return false;
        DateTimeOffset sent;
        try { sent = DateTimeOffset.FromUnixTimeSeconds(unix); }
        catch (ArgumentOutOfRangeException) { return false; }
        if ((now - sent).Duration() > tolerance) return false; // outside the replay window
        var expected = Compute(secret, timestamp!, body);
        // Constant-time compare so the check doesn't leak how many characters matched.
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(signatureHeader.Trim()));
    }
}
