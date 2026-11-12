using Microsoft.EntityFrameworkCore;

namespace BauBico;

public class Product
{
    public int Id { get; set; }
    public string Slug { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Category { get; set; } = "";
    public string Collection { get; set; } = "";
    public int PriceCents { get; set; }
    public int? CompareAtCents { get; set; }
    public string Sizes { get; set; } = "S,M,L,XL";
    public string ColorA { get; set; } = "#333333";
    public string ColorB { get; set; } = "#999999";
    public string Motif { get; set; } = "cloud";
    public bool IsNew { get; set; }
    public bool IsTrending { get; set; }
    public int Stock { get; set; }
    public string? ImageUrl { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class User
{
    public int Id { get; set; }
    public string Email { get; set; } = "";
    public string Name { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public bool IsAdmin { get; set; }
}

public class Order
{
    public int Id { get; set; }
    public string Number { get; set; } = "";
    public int? UserId { get; set; }
    public string Email { get; set; } = "";
    public string Name { get; set; } = "";
    public string Address { get; set; } = "";
    public string City { get; set; } = "";
    public string Country { get; set; } = "";
    public string Zip { get; set; } = "";
    public int SubtotalCents { get; set; }
    public int ShippingCents { get; set; }
    public int TotalCents { get; set; }
    public string Status { get; set; } = "Pre-ordered";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<OrderItem> Items { get; set; } = new();
}

public class OrderItem
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public int ProductId { get; set; }
    public string Name { get; set; } = "";
    public string Size { get; set; } = "";
    public int Qty { get; set; }
    public int UnitPriceCents { get; set; }
}

public class Review
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public int? UserId { get; set; }
    public string Author { get; set; } = "";
    public int Rating { get; set; }
    public string Text { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class Subscriber
{
    public int Id { get; set; }
    public string Email { get; set; } = "";
}

public class ContactMessage
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public string Message { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class ShopDb(DbContextOptions<ShopDb> options) : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<Review> Reviews => Set<Review>();
    public DbSet<Subscriber> Subscribers => Set<Subscriber>();
    public DbSet<ContactMessage> ContactMessages => Set<ContactMessage>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Product>().HasIndex(p => p.Slug).IsUnique();
        b.Entity<User>().HasIndex(u => u.Email).IsUnique();
        b.Entity<Order>().HasIndex(o => o.Number).IsUnique();
        b.Entity<Subscriber>().HasIndex(s => s.Email).IsUnique();
        b.Entity<Review>().HasOne<Product>().WithMany().HasForeignKey(r => r.ProductId).OnDelete(DeleteBehavior.Cascade);
    }
}
