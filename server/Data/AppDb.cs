using Microsoft.EntityFrameworkCore;

namespace Allsubs.Server.Data;

public class User
{
    public Guid Id { get; set; }
    /// <summary>Always stored trimmed and lower-cased; unique.</summary>
    public required string Email { get; set; }
    public required string Name { get; set; }
    public required string PasswordHash { get; set; }
    public string Currency { get; set; } = "USD";
    public string Theme { get; set; } = "system";
    public DateTime CreatedAt { get; set; }

    public List<Subscription> Subscriptions { get; set; } = [];
}

public class Subscription
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public required string Name { get; set; }
    public decimal Amount { get; set; }
    public required string Currency { get; set; }
    public required string Cycle { get; set; }
    public required string Category { get; set; }
    /// <summary>A known charge date; later charges are derived from it and the cycle.</summary>
    public DateOnly NextRenewal { get; set; }
    public bool Paused { get; set; }
}

public class AppDb(DbContextOptions<AppDb> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Subscription> Subscriptions => Set<Subscription>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>(u =>
        {
            u.HasIndex(x => x.Email).IsUnique();
            u.Property(x => x.Email).HasMaxLength(254);
            u.Property(x => x.Name).HasMaxLength(Rules.MaxName);
            u.HasMany(x => x.Subscriptions).WithOne().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<Subscription>(s =>
        {
            s.Property(x => x.Name).HasMaxLength(Rules.MaxName);
            s.Property(x => x.Category).HasMaxLength(Rules.MaxCategory);
        });
    }
}
