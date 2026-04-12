using Aros.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aros.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<VocabList> VocabLists => Set<VocabList>();
    public DbSet<VocabEntry> VocabEntries => Set<VocabEntry>();
    public DbSet<TestSession> TestSessions => Set<TestSession>();
    public DbSet<TestAnswer> TestAnswers => Set<TestAnswer>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TestAnswer>(entity =>
        {
            entity.HasOne(a => a.VocabEntry)
                  .WithMany(e => e.TestAnswers)
                  .HasForeignKey(a => a.VocabEntryId)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(a => a.SelectedVocabEntry)
                  .WithMany()
                  .HasForeignKey(a => a.SelectedVocabEntryId)
                  .OnDelete(DeleteBehavior.SetNull);
        });
    }
}
