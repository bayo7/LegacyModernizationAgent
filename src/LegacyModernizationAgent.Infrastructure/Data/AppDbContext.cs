using LegacyModernizationAgent.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace LegacyModernizationAgent.Infrastructure.Data;

/// <summary>
/// EF Core bağlamı. Şema CLAUDE.md Bölüm 5 DDL'ini izler; bilinçli ekler:
/// AnalysisJobs.ErrorMessage ve ClassSummaries tablosu (gerekçe: docs/architecture.md Bölüm 4).
/// </summary>
public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<AnalysisJob> AnalysisJobs => Set<AnalysisJob>();
    public DbSet<Finding> Findings => Set<Finding>();
    public DbSet<ClassDependency> ClassDependencies => Set<ClassDependency>();
    public DbSet<ClassSummary> ClassSummaries => Set<ClassSummary>();
    public DbSet<Report> Reports => Set<Report>();
    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AnalysisJob>(e =>
        {
            e.ToTable("AnalysisJobs");
            e.HasKey(x => x.Id);
            e.Property(x => x.RepoUrlOrPath).IsRequired().HasMaxLength(500);
            // Status DB'de string tutulur (DDL: NVARCHAR(50)) — sorgularda okunabilirlik için.
            e.Property(x => x.Status).HasConversion<string>().IsRequired().HasMaxLength(50);
        });

        modelBuilder.Entity<Finding>(e =>
        {
            e.ToTable("Findings");
            e.HasKey(x => x.Id);
            e.Property(x => x.RuleName).IsRequired().HasMaxLength(100);
            e.Property(x => x.Severity).IsRequired().HasMaxLength(20);
            e.Property(x => x.Confidence).IsRequired().HasMaxLength(20);
            e.Property(x => x.FilePath).IsRequired().HasMaxLength(500);
            e.Property(x => x.ClassName).HasMaxLength(200);
            e.Property(x => x.Description).IsRequired();
            e.Property(x => x.LimitationNote).HasMaxLength(500);
            e.HasOne<AnalysisJob>().WithMany().HasForeignKey(x => x.AnalysisJobId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ClassDependency>(e =>
        {
            e.ToTable("ClassDependencies");
            e.HasKey(x => x.Id);
            e.Property(x => x.FromClass).IsRequired().HasMaxLength(200);
            e.Property(x => x.ToClass).IsRequired().HasMaxLength(200);
            e.HasOne<AnalysisJob>().WithMany().HasForeignKey(x => x.AnalysisJobId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ClassSummary>(e =>
        {
            e.ToTable("ClassSummaries");
            e.HasKey(x => x.Id);
            e.Property(x => x.ClassName).IsRequired().HasMaxLength(200);
            e.Property(x => x.Namespace).IsRequired().HasMaxLength(300);
            e.Property(x => x.FilePath).IsRequired().HasMaxLength(500);
            e.Property(x => x.SummaryText).IsRequired();
            e.HasOne<AnalysisJob>().WithMany().HasForeignKey(x => x.AnalysisJobId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Report>(e =>
        {
            e.ToTable("Reports");
            e.HasKey(x => x.Id);
            e.Property(x => x.ReportJson).IsRequired();
            e.HasOne<AnalysisJob>().WithMany().HasForeignKey(x => x.AnalysisJobId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ChatMessage>(e =>
        {
            e.ToTable("ChatMessages");
            e.HasKey(x => x.Id);
            e.Property(x => x.Role).IsRequired().HasMaxLength(20);
            e.Property(x => x.Content).IsRequired();
            e.HasOne<AnalysisJob>().WithMany().HasForeignKey(x => x.AnalysisJobId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
