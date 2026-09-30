using AiIntelligence.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace AiIntelligence.Infrastructure.Persistence;

public sealed class AiIntelligenceDbContext : DbContext
{
    public AiIntelligenceDbContext(DbContextOptions<AiIntelligenceDbContext> options)
        : base(options)
    {
    }

    public DbSet<SourceDefinition> SourceDefinitions => Set<SourceDefinition>();

    public DbSet<RawSourceItem> RawSourceItems => Set<RawSourceItem>();

    public DbSet<IntelligenceItem> IntelligenceItems => Set<IntelligenceItem>();

    public DbSet<TrendEvidence> TrendEvidence => Set<TrendEvidence>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AiIntelligenceDbContext).Assembly);
    }
}
