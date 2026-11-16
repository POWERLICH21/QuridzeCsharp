using Microsoft.EntityFrameworkCore;

namespace Subfuse.Server.Data;

public class User
{
    public Guid Id { get; set; }
    /// <summary>Always stored trimmed and lower-cased; unique.</summary>
    public required string Email { get; set; }
    public required string Name { get; set; }
    public required string PasswordHash { get; set; }
    public string Currency { get; set; } = "USD";
    public string Theme { get; set; } = "dark";
    public DateTime CreatedAt { get; set; }

    /// <summary>Copied into every session cookie. Changing it signs out all existing sessions.</summary>
    public string SecurityStamp { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Authenticator secret, encrypted with the app's data-protection keys. Set during setup, before 2FA is enabled.</summary>
    public string? TwoFactorSecret { get; set; }
    public bool TwoFactorEnabled { get; set; }
    /// <summary>Time step of the last accepted authenticator code, so a code cannot be used twice.</summary>
    public long TwoFactorLastStep { get; set; }

    /// <summary>The local part of the user's private address for forwarding receipts. Created on first use.</summary>
    public string? InboundToken { get; set; }

    public int FailedLogins { get; set; }
    public DateTime? LockoutUntil { get; set; }

    public List<Subscription> Subscriptions { get; set; } = [];
    public List<RecoveryCode> RecoveryCodes { get; set; } = [];
}

/// <summary>A single-use fallback for when the authenticator app is unavailable. Only the hash is stored.</summary>
public class RecoveryCode
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public required string CodeHash { get; set; }
}

/// <summary>A subscription found in a forwarded email or pasted text, waiting for the user to review it.</summary>
public class Suggestion
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public required string Name { get; set; }
    /// <summary>Null when no price could be read; the user fills it in.</summary>
    public decimal? Amount { get; set; }
    public required string Currency { get; set; }
    public required string Cycle { get; set; }
    public required string Category { get; set; }
    public DateOnly NextRenewal { get; set; }
    /// <summary>"email", "paste", or "notice" for a message to show the user (see Note).</summary>
    public required string Source { get; set; }
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; }
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
    public DbSet<RecoveryCode> RecoveryCodes => Set<RecoveryCode>();
    public DbSet<Suggestion> Suggestions => Set<Suggestion>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>(u =>
        {
            u.HasIndex(x => x.Email).IsUnique();
            u.Property(x => x.Email).HasMaxLength(254);
            u.Property(x => x.Name).HasMaxLength(Rules.MaxName);
            u.HasMany(x => x.Subscriptions).WithOne().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
            u.HasMany(x => x.RecoveryCodes).WithOne().HasForeignKey(c => c.UserId).OnDelete(DeleteBehavior.Cascade);
            u.HasMany<Suggestion>().WithOne().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
            u.HasIndex(x => x.InboundToken).IsUnique();
            u.Property(x => x.InboundToken).HasMaxLength(40);
        });
        b.Entity<Suggestion>(s =>
        {
            s.Property(x => x.Name).HasMaxLength(Rules.MaxName);
            s.Property(x => x.Category).HasMaxLength(Rules.MaxCategory);
            s.Property(x => x.Note).HasMaxLength(200);
        });
        b.Entity<Subscription>(s =>
        {
            s.Property(x => x.Name).HasMaxLength(Rules.MaxName);
            s.Property(x => x.Category).HasMaxLength(Rules.MaxCategory);
        });
    }
}
