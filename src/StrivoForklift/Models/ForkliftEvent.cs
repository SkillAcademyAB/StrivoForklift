namespace StrivoForklift.Models;

/// <summary>
/// Represents a bank transaction record stored in the database.
/// Maps to the dbo.transactions table in Azure SQL.
/// </summary>
public class Transaction
{
    /// <summary>Unique identifier for the transaction (primary key, GUID parsed from the queue message).</summary>
    public Guid TransactionId { get; set; }

    /// <summary>Account identifier from the JSON payload (JSON $.Id, not unique).</summary>
    public string? AccountId { get; set; }

    /// <summary>Source file or system that originated the transaction (JSON $.source).</summary>
    public string? Source { get; set; }

    /// <summary>Human-readable transaction description (JSON $.Message).</summary>
    public string? Message { get; set; }

    /// <summary>Timestamp of the event, parsed from the third line of the queue message.</summary>
    public DateTime? EventTs { get; set; }

    /// <summary>The original JSON payload from the queue message.</summary>
    public string? OriginalJson { get; set; }

    /// <summary>UTC time when this record was inserted into the database.</summary>
    public DateTime InsertionTime { get; set; }
}
