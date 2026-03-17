using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using StrivoForklift.Data;
using StrivoForklift.Models;
using Xunit;

namespace StrivoForklift.Tests;

public class ForkliftQueueFunctionTests
{
    private static ForkliftDbContext CreateInMemoryDbContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<ForkliftDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;
        var context = new ForkliftDbContext(options);
        context.Database.EnsureCreated();
        return context;
    }

    /// <summary>Builds a valid 3-line raw queue message string.</summary>
    private static string BuildRawMessage(Guid transactionId, string source, string accountId, string message, string timestamp)
        => $"{transactionId}\n{{\"source\":\"{source}\",\"Id\":\"{accountId}\",\"Message\":\"{message}\"}}\n{timestamp}";

    [Fact]
    public async Task Run_NewMessage_InsertsRecord()
    {
        // Arrange
        using var db = CreateInMemoryDbContext(nameof(Run_NewMessage_InsertsRecord));
        var function = new ForkliftQueueFunction(db, NullLogger<ForkliftQueueFunction>.Instance);
        var guid = Guid.NewGuid();
        var rawMessage = BuildRawMessage(guid, "fake_bank_transactions_1000.csv", "tx0001",
            "Direct debit SEK 97.77 (Internet subscription)", "3/17/2026, 12:42:55 PM");

        // Act
        await function.Run(rawMessage);

        // Assert
        var stored = await db.Transactions.FindAsync(guid);
        Assert.NotNull(stored);
        Assert.Equal(guid, stored.TransactionId);
        Assert.Equal("tx0001", stored.AccountId);
        Assert.Equal("fake_bank_transactions_1000.csv", stored.Source);
        Assert.Equal("Direct debit SEK 97.77 (Internet subscription)", stored.Message);
        Assert.NotNull(stored.EventTs);
        Assert.NotNull(stored.OriginalJson);
    }

    [Fact]
    public async Task Run_DuplicateTransactionId_IsSkipped()
    {
        // Arrange
        using var db = CreateInMemoryDbContext(nameof(Run_DuplicateTransactionId_IsSkipped));
        var function = new ForkliftQueueFunction(db, NullLogger<ForkliftQueueFunction>.Instance);
        var guid = Guid.NewGuid();
        var rawMessage = BuildRawMessage(guid, "test.csv", "tx0001", "Payment A", "3/17/2026, 12:42:55 PM");

        // Act – send the same GUID twice
        await function.Run(rawMessage);
        await function.Run(rawMessage);

        // Assert – only one record should exist
        Assert.Equal(1, await db.Transactions.CountAsync());
    }

    [Fact]
    public async Task Run_MultipleDistinctTransactions_StoredSeparately()
    {
        // Arrange
        using var db = CreateInMemoryDbContext(nameof(Run_MultipleDistinctTransactions_StoredSeparately));
        var function = new ForkliftQueueFunction(db, NullLogger<ForkliftQueueFunction>.Instance);
        var guid1 = Guid.NewGuid();
        var guid2 = Guid.NewGuid();

        // Act
        await function.Run(BuildRawMessage(guid1, "test.csv", "tx0001", "Payment A", "3/17/2026, 12:42:55 PM"));
        await function.Run(BuildRawMessage(guid2, "test.csv", "tx0002", "Payment B", "3/17/2026, 1:00:00 PM"));

        // Assert
        Assert.Equal(2, await db.Transactions.CountAsync());
        var stored1 = await db.Transactions.FindAsync(guid1);
        var stored2 = await db.Transactions.FindAsync(guid2);
        Assert.NotNull(stored1);
        Assert.NotNull(stored2);
        Assert.Equal("tx0001", stored1.AccountId);
        Assert.Equal("tx0002", stored2.AccountId);
    }

    [Fact]
    public async Task Run_MalformedMessage_IsSkipped()
    {
        // Arrange
        using var db = CreateInMemoryDbContext(nameof(Run_MalformedMessage_IsSkipped));
        var function = new ForkliftQueueFunction(db, NullLogger<ForkliftQueueFunction>.Instance);

        // Act – only 1 line, not 3
        await function.Run("only-one-line");

        // Assert – nothing should be inserted
        Assert.Equal(0, await db.Transactions.CountAsync());
    }

    [Fact]
    public async Task Run_InvalidGuid_IsSkipped()
    {
        // Arrange
        using var db = CreateInMemoryDbContext(nameof(Run_InvalidGuid_IsSkipped));
        var function = new ForkliftQueueFunction(db, NullLogger<ForkliftQueueFunction>.Instance);
        var rawMessage = "not-a-guid\n{\"source\":\"test.csv\",\"Id\":\"tx0001\",\"Message\":\"Payment\"}\n3/17/2026, 12:42:55 PM";

        // Act
        await function.Run(rawMessage);

        // Assert
        Assert.Equal(0, await db.Transactions.CountAsync());
    }
}
