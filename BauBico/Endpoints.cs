using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace BauBico;

public record RegisterRequest(string? Name, string? Email, string? Password);
public record LoginRequest(string? Email, string? Password);
public record OrderLine(int ProductId, string? Size, int Qty);
public record OrderRequest(string? Name, string? Email, string? Address, string? City, string? Country, string? Zip, List<OrderLine>? Items);
public record ReviewRequest(int Rating, string? Text);
public record EmailRequest(string? Email);
public record ContactRequest(string? Name, string? Email, string? Message);
public record StatusRequest(string? Status);
public record ProductInput(string? Name, string? Description, string? Category, string? Collection, int PriceCents,
    int? CompareAtCents, string? Sizes, string? ColorA, string? ColorB, string? Motif, bool IsNew, bool IsTrending, int Stock);

public static partial class ShopApi
{
    private static readonly string[] Statuses = { "Placed", "Processing", "Shipped", "Delivered", "Cancelled" };
    private static readonly string[] Motifs = { "cloud", "wave", "star", "bolt", "leaf", "ring", "flame", "snow" };
    private static readonly PasswordHasher<User> Hasher = new();

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")] private static partial Regex EmailRx();
    [GeneratedRegex("^#[0-9a-fA-F]{6}$")] private static partial Regex ColorRx();

    private static IResult Bad(string message) => Results.BadRequest(new { error = message });
    private static string Clean(string? s, int max) { s = (s ?? "").Trim(); return s.Length > max ? s[..max] : s; }
    private static bool On(string? flag) => flag is "1" or "true";
    private static bool IsEmail(string s) => s.Length <= 200 && EmailRx().IsMatch(s);
    private static int? UserId(ClaimsPrincipal u) => int.TryParse(u.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    private static Task SignIn(HttpContext http, User u)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, u.Id.ToString()),
            new(ClaimTypes.Name, u.Name),
            new(ClaimTypes.Email, u.Email),
            new("admin", u.IsAdmin ? "true" : "false"),
        };
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        return http.SignInAsync(new ClaimsPrincipal(identity), new AuthenticationProperties { IsPersistent = true });
    }

    private static object OrderView(Order o) => new
    {
        o.Number, o.Status, o.CreatedAt, o.Name, o.Email, o.Address, o.City, o.Country, o.Zip,
        o.SubtotalCents, o.ShippingCents, o.TotalCents,
        Items = o.Items.Select(i => new { i.ProductId, i.Name, i.Size, i.Qty, i.UnitPriceCents })
    };

    public static void MapShopApi(this WebApplication app)
    {
        var api = app.MapGroup("/api");
        var shopCfg = app.Configuration.GetSection("Shop");
        var freeFrom = shopCfg.GetValue("FreeShippingFromCents", 7500);
        var shipping = shopCfg.GetValue("ShippingCents", 695);

        api.MapGet("/config", () => new { freeShippingFromCents = freeFrom, shippingCents = shipping });

        // ---------- Catalogue ----------
        api.MapGet("/categories", async (ShopDb db) => new
        {
            categories = await db.Products.GroupBy(p => p.Category).Select(g => new { name = g.Key, count = g.Count() }).OrderBy(c => c.name).ToListAsync(),
            collections = await db.Products.GroupBy(p => p.Collection).Select(g => new { name = g.Key, count = g.Count() }).OrderBy(c => c.name).ToListAsync(),
        });

        api.MapGet("/products", async (ShopDb db, string? category, string? collection, string? q, string? sort,
            string? ids, string? sale, string? trending, string? isNew, int page = 1, int pageSize = 12) =>
        {
            var query = db.Products.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(category)) query = query.Where(p => p.Category == category);
            if (!string.IsNullOrWhiteSpace(collection)) query = query.Where(p => p.Collection == collection);
            if (!string.IsNullOrWhiteSpace(q))
            {
                var term = q.Trim().ToLower();
                query = query.Where(p => p.Name.ToLower().Contains(term) || p.Category.ToLower().Contains(term) || p.Collection.ToLower().Contains(term));
            }
            if (!string.IsNullOrWhiteSpace(ids))
            {
                var list = ids.Split(',').Select(s => int.TryParse(s, out var n) ? n : 0).Where(n => n > 0).Take(100).ToList();
                query = query.Where(p => list.Contains(p.Id));
            }
            if (On(sale)) query = query.Where(p => p.CompareAtCents != null);
            if (On(trending)) query = query.Where(p => p.IsTrending);
            if (On(isNew)) query = query.Where(p => p.IsNew);

            query = sort switch
            {
                "price-asc" => query.OrderBy(p => p.PriceCents).ThenBy(p => p.Id),
                "price-desc" => query.OrderByDescending(p => p.PriceCents).ThenBy(p => p.Id),
                "name" => query.OrderBy(p => p.Name),
                _ => query.OrderByDescending(p => p.CreatedAt).ThenBy(p => p.Id),
            };

            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 100);
            var total = await query.CountAsync();
            var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
            return new { total, page, pageSize, items };
        });

        api.MapGet("/products/{slug}", async (ShopDb db, string slug) =>
        {
            var p = await db.Products.AsNoTracking().FirstOrDefaultAsync(x => x.Slug == slug);
            if (p is null) return Results.NotFound();
            var reviews = await db.Reviews.AsNoTracking().Where(r => r.ProductId == p.Id).OrderByDescending(r => r.CreatedAt)
                .Select(r => new { r.Author, r.Rating, r.Text, r.CreatedAt }).ToListAsync();
            var related = await db.Products.AsNoTracking().Where(x => x.Id != p.Id && (x.Category == p.Category || x.Collection == p.Collection))
                .OrderByDescending(x => x.IsTrending).ThenBy(x => x.Id).Take(4).ToListAsync();
            return Results.Ok(new { product = p, reviews, related });
        });

        api.MapPost("/products/{id:int}/reviews", async (ShopDb db, ClaimsPrincipal user, int id, ReviewRequest req) =>
        {
            var text = Clean(req.Text, 1000);
            if (req.Rating is < 1 or > 5) return Bad("Rating must be between 1 and 5.");
            if (text.Length < 3) return Bad("Please write a few words.");
            if (!await db.Products.AnyAsync(p => p.Id == id)) return Results.NotFound();
            var uid = UserId(user);
            if (await db.Reviews.AnyAsync(r => r.ProductId == id && r.UserId == uid)) return Bad("You have already reviewed this product.");
            db.Reviews.Add(new Review { ProductId = id, UserId = uid, Author = user.Identity!.Name ?? "Customer", Rating = req.Rating, Text = text });
            await db.SaveChangesAsync();
            return Results.Ok(new { ok = true });
        }).RequireAuthorization().RequireRateLimiting("forms");

        // ---------- Accounts ----------
        api.MapPost("/auth/register", async (ShopDb db, HttpContext http, RegisterRequest req) =>
        {
            var name = Clean(req.Name, 80);
            var email = Clean(req.Email, 200).ToLowerInvariant();
            var password = req.Password ?? "";
            if (name.Length < 2) return Bad("Please enter your name.");
            if (!IsEmail(email)) return Bad("Please enter a valid email address.");
            if (password.Length is < 8 or > 200) return Bad("Password must be at least 8 characters.");
            if (await db.Users.AnyAsync(u => u.Email == email)) return Bad("An account with this email already exists.");
            var user = new User { Name = name, Email = email };
            user.PasswordHash = Hasher.HashPassword(user, password);
            db.Users.Add(user);
            await db.SaveChangesAsync();
            await SignIn(http, user);
            return Results.Ok(new { user.Name, user.Email, user.IsAdmin });
        }).RequireRateLimiting("forms");

        api.MapPost("/auth/login", async (ShopDb db, HttpContext http, LoginRequest req) =>
        {
            var email = Clean(req.Email, 200).ToLowerInvariant();
            var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email);
            if (user is null || Hasher.VerifyHashedPassword(user, user.PasswordHash, req.Password ?? "") == PasswordVerificationResult.Failed)
                return Bad("Wrong email or password.");
            await SignIn(http, user);
            return Results.Ok(new { user.Name, user.Email, user.IsAdmin });
        }).RequireRateLimiting("forms");

        api.MapPost("/auth/logout", async (HttpContext http) => { await http.SignOutAsync(); return Results.Ok(new { ok = true }); });

        api.MapGet("/auth/me", (ClaimsPrincipal user) => user.Identity?.IsAuthenticated == true
            ? Results.Ok(new { name = user.Identity.Name, email = user.FindFirstValue(ClaimTypes.Email), isAdmin = user.HasClaim("admin", "true") })
            : Results.Ok(null));

        // ---------- Orders ----------
        api.MapPost("/orders", async (ShopDb db, ClaimsPrincipal user, OrderRequest req) =>
        {
            var order = new Order
            {
                UserId = UserId(user),
                Name = Clean(req.Name, 80), Email = Clean(req.Email, 200).ToLowerInvariant(),
                Address = Clean(req.Address, 200), City = Clean(req.City, 80), Country = Clean(req.Country, 80), Zip = Clean(req.Zip, 20),
            };
            if (order.Name.Length < 2 || order.Address.Length < 4 || order.City.Length < 2 || order.Country.Length < 2 || order.Zip.Length < 2)
                return Bad("Please fill in all shipping details.");
            if (!IsEmail(order.Email)) return Bad("Please enter a valid email address.");
            if (req.Items is null || req.Items.Count == 0) return Bad("Your cart is empty.");
            if (req.Items.Count > 50) return Bad("Too many items in one order.");

            // Prices and stock always come from the database, never from the browser.
            await using var tx = await db.Database.BeginTransactionAsync();
            foreach (var group in req.Items.GroupBy(i => (i.ProductId, Size: Clean(i.Size, 10))))
            {
                var qty = group.Sum(i => i.Qty);
                if (group.Any(i => i.Qty < 1) || qty > 20) return Bad("Invalid quantity.");
                var p = await db.Products.FirstOrDefaultAsync(x => x.Id == group.Key.ProductId);
                if (p is null) return Bad("A product in your cart is no longer available.");
                if (!p.Sizes.Split(',').Contains(group.Key.Size)) return Bad($"Size {group.Key.Size} is not available for {p.Name}.");
                if (p.Stock < qty) return Bad($"Only {p.Stock} left of {p.Name}.");
                p.Stock -= qty;
                order.Items.Add(new OrderItem { ProductId = p.Id, Name = p.Name, Size = group.Key.Size, Qty = qty, UnitPriceCents = p.PriceCents });
            }
            order.SubtotalCents = order.Items.Sum(i => i.Qty * i.UnitPriceCents);
            order.ShippingCents = order.SubtotalCents >= freeFrom ? 0 : shipping;
            order.TotalCents = order.SubtotalCents + order.ShippingCents;
            order.Number = "BB-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(5));
            db.Orders.Add(order);
            await db.SaveChangesAsync();
            await tx.CommitAsync();
            return Results.Ok(OrderView(order));
        }).RequireRateLimiting("forms");

        api.MapGet("/orders/mine", async (ShopDb db, ClaimsPrincipal user) =>
        {
            var uid = UserId(user);
            var orders = await db.Orders.AsNoTracking().Include(o => o.Items).Where(o => o.UserId == uid).OrderByDescending(o => o.CreatedAt).ToListAsync();
            return orders.Select(OrderView);
        }).RequireAuthorization();

        api.MapGet("/orders/track", async (ShopDb db, string? number, string? email) =>
        {
            var n = Clean(number, 30).ToUpperInvariant();
            var e = Clean(email, 200).ToLowerInvariant();
            var order = await db.Orders.AsNoTracking().Include(o => o.Items).FirstOrDefaultAsync(o => o.Number == n && o.Email == e);
            return order is null ? Results.NotFound(new { error = "No order found with that number and email." }) : Results.Ok(OrderView(order));
        }).RequireRateLimiting("forms");

        // ---------- Newsletter and contact ----------
        api.MapPost("/newsletter", async (ShopDb db, EmailRequest req) =>
        {
            var email = Clean(req.Email, 200).ToLowerInvariant();
            if (!IsEmail(email)) return Bad("Please enter a valid email address.");
            if (!await db.Subscribers.AnyAsync(s => s.Email == email))
            {
                db.Subscribers.Add(new Subscriber { Email = email });
                await db.SaveChangesAsync();
            }
            return Results.Ok(new { ok = true });
        }).RequireRateLimiting("forms");

        api.MapPost("/contact", async (ShopDb db, ContactRequest req) =>
        {
            var msg = new ContactMessage { Name = Clean(req.Name, 80), Email = Clean(req.Email, 200).ToLowerInvariant(), Message = Clean(req.Message, 2000) };
            if (msg.Name.Length < 2 || msg.Message.Length < 5) return Bad("Please enter your name and a message.");
            if (!IsEmail(msg.Email)) return Bad("Please enter a valid email address.");
            db.ContactMessages.Add(msg);
            await db.SaveChangesAsync();
            return Results.Ok(new { ok = true });
        }).RequireRateLimiting("forms");

        // ---------- Admin ----------
        var admin = api.MapGroup("/admin").RequireAuthorization("Admin");

        admin.MapGet("/stats", async (ShopDb db) => new
        {
            products = await db.Products.CountAsync(),
            orders = await db.Orders.CountAsync(),
            customers = await db.Users.CountAsync(u => !u.IsAdmin),
            subscribers = await db.Subscribers.CountAsync(),
            revenueCents = await db.Orders.Where(o => o.Status != "Cancelled").SumAsync(o => o.TotalCents),
        });

        admin.MapGet("/orders", async (ShopDb db) =>
            (await db.Orders.AsNoTracking().Include(o => o.Items).OrderByDescending(o => o.CreatedAt).Take(200).ToListAsync()).Select(OrderView));

        admin.MapPut("/orders/{number}/status", async (ShopDb db, string number, StatusRequest req) =>
        {
            if (!Statuses.Contains(req.Status)) return Bad("Unknown status.");
            var order = await db.Orders.FirstOrDefaultAsync(o => o.Number == number);
            if (order is null) return Results.NotFound();
            order.Status = req.Status!;
            await db.SaveChangesAsync();
            return Results.Ok(new { ok = true });
        });

        admin.MapGet("/messages", async (ShopDb db) =>
            await db.ContactMessages.AsNoTracking().OrderByDescending(m => m.CreatedAt).Take(100).ToListAsync());

        admin.MapPost("/products", async (ShopDb db, ProductInput input) =>
        {
            var p = new Product();
            var error = Apply(p, input);
            if (error is not null) return Bad(error);
            var slug = Seed.Slugify(p.Name);
            if (slug.Length == 0) slug = "product";
            p.Slug = slug;
            for (var n = 2; await db.Products.AnyAsync(x => x.Slug == p.Slug); n++) p.Slug = $"{slug}-{n}";
            db.Products.Add(p);
            await db.SaveChangesAsync();
            return Results.Ok(p);
        });

        admin.MapPut("/products/{id:int}", async (ShopDb db, int id, ProductInput input) =>
        {
            var p = await db.Products.FindAsync(id);
            if (p is null) return Results.NotFound();
            var error = Apply(p, input);
            if (error is not null) return Bad(error);
            await db.SaveChangesAsync();
            return Results.Ok(p);
        });

        admin.MapDelete("/products/{id:int}", async (ShopDb db, int id) =>
        {
            var p = await db.Products.FindAsync(id);
            if (p is null) return Results.NotFound();
            db.Products.Remove(p);
            await db.SaveChangesAsync();
            return Results.Ok(new { ok = true });
        });
    }

    private static string? Apply(Product p, ProductInput i)
    {
        var name = Clean(i.Name, 120);
        var category = Clean(i.Category, 40);
        var sizes = Clean(i.Sizes, 60).ToUpperInvariant().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (name.Length < 3) return "Name is too short.";
        if (category.Length < 2) return "Category is required.";
        if (i.PriceCents is < 1 or > 10_000_00) return "Price is out of range.";
        if (i.CompareAtCents is not null && i.CompareAtCents <= i.PriceCents) return "The old price must be higher than the price.";
        if (i.Stock is < 0 or > 100_000) return "Stock is out of range.";
        if (sizes.Length == 0 || sizes.Any(s => s.Length > 5)) return "Enter sizes separated by commas, for example S,M,L.";
        if (!ColorRx().IsMatch(i.ColorA ?? "") || !ColorRx().IsMatch(i.ColorB ?? "")) return "Colours must look like #1a2b3c.";
        if (!Motifs.Contains(i.Motif)) return "Unknown motif.";

        p.Name = name; p.Category = category; p.Collection = Clean(i.Collection, 40);
        p.Description = Clean(i.Description, 2000);
        p.PriceCents = i.PriceCents; p.CompareAtCents = i.CompareAtCents;
        p.Sizes = string.Join(",", sizes.Distinct());
        p.ColorA = i.ColorA!; p.ColorB = i.ColorB!; p.Motif = i.Motif!;
        p.IsNew = i.IsNew; p.IsTrending = i.IsTrending; p.Stock = i.Stock;
        return null;
    }
}
