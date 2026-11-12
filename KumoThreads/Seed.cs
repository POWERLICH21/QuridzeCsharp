using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace KumoThreads;

public static class Seed
{
    // name, category, collection, price, compareAt (0 = none), colorA, colorB, motif, new, trending
    private static readonly (string, string, string, int, int, string, string, string, bool, bool)[] Items =
    {
        ("Crimson Cloud Pullover Hoodie", "Hoodies", "Crimson Cloud", 5900, 0, "#1c1c22", "#c81e3a", "cloud", false, true),
        ("Night Wave Zip Hoodie", "Hoodies", "Wave Runner", 6400, 7400, "#0d2b52", "#35b6d6", "wave", false, true),
        ("Neon Yokai Oversized Hoodie", "Hoodies", "Neon Yokai", 6200, 0, "#2a0f4d", "#ff4fd8", "flame", true, false),
        ("Spirit Leaf Lightweight Hoodie", "Hoodies", "Spirit Forest", 5400, 0, "#24452f", "#b6d96a", "leaf", true, false),
        ("Mecha Pilot Bomber Jacket", "Jackets", "Mecha Garage", 7900, 9400, "#30363d", "#f08a24", "bolt", false, true),
        ("Wave Runner Varsity Jacket", "Jackets", "Wave Runner", 8400, 0, "#f2f2ee", "#1f4f8f", "wave", true, false),
        ("Yokai Parade Coach Jacket", "Jackets", "Neon Yokai", 7200, 0, "#141414", "#7cf5c5", "ring", false, false),
        ("Crimson Cloud Dream Cloak", "Cloak Coats", "Crimson Cloud", 7900, 9400, "#f6f1e6", "#c81e3a", "cloud", false, true),
        ("Forest Guardian Cloak", "Cloak Coats", "Spirit Forest", 7900, 9400, "#2f5d3a", "#e6d9a8", "leaf", false, true),
        ("Starfall Captain Cloak", "Cloak Coats", "Mecha Garage", 8200, 0, "#151a35", "#f4c84a", "star", true, false),
        ("Tidal Garden Button Up Shirt", "Shirts", "Wave Runner", 3900, 0, "#1f7a8c", "#f7d488", "wave", false, true),
        ("Paper Lantern Button Up Shirt", "Shirts", "Crimson Cloud", 3900, 0, "#f3e9d2", "#d1495b", "flame", true, false),
        ("Koi Stream Graphic Tee", "Shirts", "Spirit Forest", 2900, 3400, "#fafafa", "#ef6f3c", "ring", false, false),
        ("Arcade Spirit Graphic Tee", "Shirts", "Neon Yokai", 2900, 0, "#101010", "#b46bff", "star", true, true),
        ("Thunder Dojo Baseball Jersey", "Jerseys", "Mecha Garage", 4700, 0, "#f5f5f5", "#e2b714", "bolt", false, true),
        ("Crimson Cloud Baseball Jersey", "Jerseys", "Crimson Cloud", 4700, 0, "#1c1c22", "#c81e3a", "cloud", false, true),
        ("Moon Court Basketball Jersey", "Jerseys", "Neon Yokai", 4400, 5200, "#3a1c71", "#ffaf7b", "ring", true, false),
        ("Iron Wave Rash Guard", "Gym Wear", "Wave Runner", 4200, 0, "#0b132b", "#5bc0be", "wave", false, true),
        ("Ember Core Compression Tee", "Gym Wear", "Mecha Garage", 3800, 0, "#2b2d42", "#ef233c", "flame", true, false),
        ("Spirit Leaf Training Tank", "Gym Wear", "Spirit Forest", 2700, 3200, "#e9f5db", "#52796f", "leaf", false, false),
        ("Volt Runner Performance Shorts", "Shorts", "Mecha Garage", 3600, 0, "#111111", "#f5d90a", "bolt", false, true),
        ("Yokai Mask Gym Shorts", "Shorts", "Neon Yokai", 3400, 0, "#4b1d7a", "#19d3da", "flame", true, true),
        ("Tide Breaker Board Shorts", "Shorts", "Wave Runner", 3600, 4200, "#0077b6", "#caf0f8", "wave", false, false),
        ("Cloud Step Joggers", "Bottoms", "Crimson Cloud", 4900, 0, "#202024", "#c81e3a", "cloud", true, false),
        ("Garage Crew Cargo Pants", "Bottoms", "Mecha Garage", 5600, 0, "#4a4e45", "#f08a24", "bolt", false, false),
        ("Snow Spirit Knit Sweater", "Sweaters", "Holiday", 4900, 5900, "#12355b", "#f1faee", "snow", true, true),
        ("Lantern Festival Knit Sweater", "Sweaters", "Holiday", 4900, 5900, "#7b1e1e", "#f4d35e", "star", true, false),
        ("Cozy Kitsune Holiday Sweater", "Sweaters", "Holiday", 5200, 0, "#1b4332", "#e63946", "snow", true, true),
    };

    private static readonly (string, int, string)[] SampleReviews =
    {
        ("Mika T.", 5, "Print is sharp and the fabric feels great. True to size."),
        ("Jordan R.", 4, "Really comfortable. Shipping took about a week."),
        ("Sam K.", 5, "Got so many compliments wearing this. Ordering another colour."),
        ("Alex P.", 4, "Nice quality for the price, colours match the pictures."),
    };

    public static string Slugify(string s) =>
        string.Join("-", new string(s.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : ' ').ToArray())
            .Split(' ', StringSplitOptions.RemoveEmptyEntries));

    public static void Run(ShopDb db, IConfiguration config)
    {
        db.Database.EnsureCreated();

        if (!db.Products.Any())
        {
            var day = 0;
            foreach (var (name, cat, col, price, compare, a, b, motif, isNew, trending) in Items)
            {
                db.Products.Add(new Product
                {
                    Name = name, Slug = Slugify(name), Category = cat, Collection = col,
                    PriceCents = price, CompareAtCents = compare == 0 ? null : compare,
                    ColorA = a, ColorB = b, Motif = motif, IsNew = isNew, IsTrending = trending,
                    Sizes = cat is "Shorts" or "Bottoms" ? "S,M,L,XL,2XL" : "XS,S,M,L,XL,2XL",
                    Stock = 40,
                    CreatedAt = DateTime.UtcNow.AddDays(-day++),
                    Description = $"Part of the {col} collection. All-over print on soft, breathable fabric that keeps its colour wash after wash. " +
                                  "Made to order in small batches, with a relaxed everyday fit."
                });
            }
            db.SaveChanges();

            var i = 0;
            foreach (var p in db.Products.Where(p => p.IsTrending).ToList())
            {
                for (var k = 0; k < 2; k++)
                {
                    var (author, rating, text) = SampleReviews[i++ % SampleReviews.Length];
                    db.Reviews.Add(new Review { ProductId = p.Id, Author = author, Rating = rating, Text = text, CreatedAt = DateTime.UtcNow.AddDays(-i) });
                }
            }
            db.SaveChanges();
        }

        // Admin account comes from configuration (Admin:Email / Admin:Password), never hard-coded.
        var email = config["Admin:Email"]?.Trim().ToLowerInvariant();
        var password = config["Admin:Password"];
        if (!string.IsNullOrEmpty(email) && !string.IsNullOrEmpty(password) && !db.Users.Any(u => u.Email == email))
        {
            var admin = new User { Email = email, Name = "Admin", IsAdmin = true };
            admin.PasswordHash = new PasswordHasher<User>().HashPassword(admin, password);
            db.Users.Add(admin);
            db.SaveChanges();
        }
    }
}
