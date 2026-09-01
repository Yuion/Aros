using Aros.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aros.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<VocabList> VocabLists => Set<VocabList>();
    public DbSet<VocabEntry> VocabEntries => Set<VocabEntry>();
    public DbSet<TestSession> TestSessions => Set<TestSession>();
    public DbSet<TestAnswer> TestAnswers => Set<TestAnswer>();
    public DbSet<TtsClip> TtsClips => Set<TtsClip>();
    public DbSet<TtsClipStat> TtsClipStats => Set<TtsClipStat>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TtsClip>(entity =>
        {
            // The cache key — one paid synthesis per distinct sentence
            entity.HasIndex(c => c.Sentence).IsUnique();

            entity.HasOne(c => c.Stat)
                  .WithOne(s => s.TtsClip)
                  .HasForeignKey<TtsClipStat>(s => s.TtsClipId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

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
