using HomelabDashboard.Models;
using Microsoft.EntityFrameworkCore;

namespace HomelabDashboard.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<LoginAttempt> LoginAttempts => Set<LoginAttempt>();
    public DbSet<ResourceSample> ResourceSamples => Set<ResourceSample>();
    public DbSet<DetectedErrorLog> DetectedErrorLogs => Set<DetectedErrorLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>().HasIndex(u => u.Username).IsUnique();

        // The morning report queries "all samples for guest X in the last
        // 24h" - index on (TargetType, Node, VmId, CapturedAt) so that scan
        // stays a range lookup instead of a table scan as history grows.
        modelBuilder.Entity<ResourceSample>()
            .HasIndex(s => new { s.TargetType, s.Node, s.VmId, s.CapturedAt });

        modelBuilder.Entity<DetectedErrorLog>()
            .HasIndex(e => e.DetectedAt);
    }
}
