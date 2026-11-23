using System.Collections.Concurrent;

namespace EpodWebhookLite;

/// <summary>
/// Remembers which provider events were already processed, because providers retry.
/// This in-memory version is for the sample only: it forgets on restart and isn't shared between instances.
/// In production, insert (provider, eventId) into a table with a UNIQUE index in the SAME transaction
/// as your business change, and treat a unique-key violation as "duplicate".
/// </summary>
public interface IIdempotencyStore
{
    /// <returns>true if this is the first time the event was seen; false if it is a duplicate.</returns>
    bool TryRecord(string provider, string eventId);
}

public sealed class InMemoryIdempotencyStore : IIdempotencyStore
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _seen = new();
    public bool TryRecord(string provider, string eventId) =>
        _seen.TryAdd($"{provider}\u001f{eventId}", DateTimeOffset.UtcNow);
}
