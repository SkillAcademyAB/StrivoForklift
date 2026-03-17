using System.Text.Json.Serialization;

namespace StrivoForklift.Models;

/// <summary>
/// Represents the JSON payload embedded in a bank transaction queue message.
/// </summary>
public class QueueMessage
{
    /// <summary>Source file or system that originated the transaction (e.g. "fake_bank_transactions_1000.csv").</summary>
    [JsonPropertyName("source")]
    public string? Source { get; set; }

    /// <summary>Account identifier for the transaction (e.g. "tx0001").</summary>
    [JsonPropertyName("Id")]
    public string? Id { get; set; }

    /// <summary>Human-readable transaction description (e.g. "Direct debit SEK 97.77 (Internet subscription)").</summary>
    [JsonPropertyName("Message")]
    public string? Message { get; set; }
}
