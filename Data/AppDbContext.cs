using Microsoft.EntityFrameworkCore;
using YGODuelSimulator.Models;

namespace YGODuelSimulator.Data;

public class AppDbContext : DbContext
{
    public DbSet<Card> Cards => Set<Card>();
    public DbSet<CardImage> CardImages => Set<CardImage>();
    public DbSet<CardSet> CardSets => Set<CardSet>();
    public DbSet<CardPrice> CardPrices => Set<CardPrice>();
    public DbSet<CardLinkMarker> CardLinkMarkers => Set<CardLinkMarker>();
    public DbSet<CardFormat> CardFormats => Set<CardFormat>();

    public AppDbContext() { }

    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
        {
            optionsBuilder.UseSqlite(DatabaseConfig.GetConnectionString());
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Card>(e =>
        {
            // Id is the API passcode, not a generated value.
            e.Property(c => c.Id).ValueGeneratedNever();
            e.HasIndex(c => c.Name);
            e.HasIndex(c => c.Archetype);
            e.HasIndex(c => c.Type);
        });

        modelBuilder.Entity<CardImage>(e =>
        {
            e.HasIndex(i => i.CardId);
            e.HasOne(i => i.Card).WithMany(c => c.Images)
                .HasForeignKey(i => i.CardId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CardSet>(e =>
        {
            e.HasIndex(s => s.CardId);
            e.HasOne(s => s.Card).WithMany(c => c.Sets)
                .HasForeignKey(s => s.CardId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CardPrice>(e =>
        {
            e.HasIndex(p => p.CardId);
            e.HasOne(p => p.Card).WithMany(c => c.Prices)
                .HasForeignKey(p => p.CardId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CardLinkMarker>(e =>
        {
            e.HasIndex(m => m.CardId);
            e.HasOne(m => m.Card).WithMany(c => c.LinkMarkers)
                .HasForeignKey(m => m.CardId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CardFormat>(e =>
        {
            e.HasIndex(f => f.CardId);
            e.HasOne(f => f.Card).WithMany(c => c.Formats)
                .HasForeignKey(f => f.CardId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
