namespace MediFlow.Domain.Messaging;

/// <summary>
/// Durable work items. Claim intake writes an <c>adjudicate-claim</c> message in the
/// same transaction as the claim; the worker drains the queue with a leasing
/// stored procedure. This transactional-outbox shape means a crash between
/// "claim saved" and "work queued" can never lose a claim (see ADR 0005).
/// </summary>
public sealed class OutboxMessage
{
    public long Id { get; set; }

    /// <summary>Message type discriminator, e.g. adjudicate-claim.</summary>
    public required string Type { get; set; }

    /// <summary>JSON payload — for adjudicate-claim, the claim id.</summary>
    public required string PayloadJson { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    /// <summary>Earliest time this message may be leased (drives retry backoff).</summary>
    public DateTime AvailableAtUtc { get; set; }

    /// <summary>Lease owner + expiry — a crashed worker's lease lapses and the message is re-leased.</summary>
    public Guid? LeaseToken { get; set; }
    public DateTime? LeasedUntilUtc { get; set; }

    public int Attempts { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public string? LastError { get; set; }

    public const string AdjudicateClaim = "adjudicate-claim";
    public const int MaxAttempts = 5;
}
