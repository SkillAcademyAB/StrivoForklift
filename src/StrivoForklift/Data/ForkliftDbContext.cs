using Microsoft.EntityFrameworkCore;
using StrivoForklift.Models;

namespace StrivoForklift.Data;

/// <summary>
/// EF Core database context for bank transaction storage.
/// </summary>
public class ForkliftDbContext : DbContext
{
    public ForkliftDbContext(DbContextOptions<ForkliftDbContext> options) : base(options)
    {
    }

    public DbSet<Transaction> Transactions { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Transaction>(entity =>
        {
            entity.ToTable("transactions", "dbo");
            entity.HasKey(e => e.TransactionId);
            entity.Property(e => e.TransactionId).IsRequired();
            entity.Property(e => e.AccountId).HasMaxLength(100);
            entity.Property(e => e.Source).HasMaxLength(255);
            entity.Property(e => e.InsertionTime)
                  .IsRequired();
            entity.HasIndex(e => e.AccountId)
                  .HasDatabaseName("IX_transactions_account_id");
        });
    }
}
