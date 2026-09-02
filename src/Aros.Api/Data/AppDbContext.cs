using Aros.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aros.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<TtsClip> TtsClips => Set<TtsClip>();
    public DbSet<TtsClipStat> TtsClipStats => Set<TtsClipStat>();
    public DbSet<HomophoneGroup> HomophoneGroups => Set<HomophoneGroup>();
    public DbSet<ListeningAnswer> ListeningAnswers => Set<ListeningAnswer>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ListeningAnswer>(entity =>
        {
            entity.HasIndex(a => a.AnsweredAt);

            entity.HasOne(a => a.TtsClip)
                  .WithMany()
                  .HasForeignKey(a => a.TtsClipId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<HomophoneGroup>(entity =>
        {
            entity.HasIndex(g => g.Characters).IsUnique();

            // A starter set of characters that are genuinely indistinguishable by ear.
            // Editable and deletable from the Chinese Listening page.
            var seeded = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            entity.HasData(
                new HomophoneGroup { Id = 1, Characters = "他她它", Reading = "tā", CreatedAt = seeded },
                new HomophoneGroup { Id = 2, Characters = "你妳", Reading = "nǐ", CreatedAt = seeded },
                new HomophoneGroup { Id = 3, Characters = "的得地", Reading = "de", CreatedAt = seeded },
                new HomophoneGroup { Id = 4, Characters = "在再", Reading = "zài", CreatedAt = seeded },
                new HomophoneGroup { Id = 5, Characters = "是事", Reading = "shì", CreatedAt = seeded },
                new HomophoneGroup { Id = 6, Characters = "做作", Reading = "zuò", CreatedAt = seeded });
        });

        modelBuilder.Entity<TtsClip>(entity =>
        {
            // The cache key — one paid synthesis per distinct sentence
            entity.HasIndex(c => c.Sentence).IsUnique();

            entity.HasOne(c => c.Stat)
                  .WithOne(s => s.TtsClip)
                  .HasForeignKey<TtsClipStat>(s => s.TtsClipId)
                  .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
