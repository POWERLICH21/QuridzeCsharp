using System.Security.Claims;
using Allsubs.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace Allsubs.Server.Api;

public record SubscriptionRequest(string? Name, decimal? Amount, string? Currency, string? Cycle, string? Category, DateOnly? Next, bool? Paused);
public record SubscriptionResponse(Guid Id, string Name, decimal Amount, string Currency, string Cycle, string Category, DateOnly Next, bool Paused)
{
    public static SubscriptionResponse From(Subscription s) =>
        new(s.Id, s.Name, s.Amount, s.Currency, s.Cycle, s.Category, s.NextRenewal, s.Paused);
}

public static class SubscriptionEndpoints
{
    /// <summary>Upper bound per account, so one user cannot fill the database.</summary>
    private const int MaxPerUser = 500;

    public static void MapSubscriptions(this IEndpointRouteBuilder app)
    {
        var subs = app.MapGroup("/api/subscriptions").RequireAuthorization();

        subs.MapGet("", async (ClaimsPrincipal principal, AppDb db) =>
        {
            var userId = principal.UserId();
            var list = await db.Subscriptions.AsNoTracking().Where(s => s.UserId == userId).ToListAsync();
            return list.Select(SubscriptionResponse.From);
        });

        subs.MapPost("", async (SubscriptionRequest req, ClaimsPrincipal principal, AppDb db) =>
        {
            if (Validate(req) is { } error) return Rules.Bad(error);
            var userId = principal.UserId();
            // The account may have been deleted while its cookie is still valid.
            if (!await db.Users.AnyAsync(u => u.Id == userId)) return Results.Unauthorized();
            if (await db.Subscriptions.CountAsync(s => s.UserId == userId) >= MaxPerUser)
                return Rules.Bad($"You can track up to {MaxPerUser} subscriptions.");

            var sub = new Subscription { Id = Guid.NewGuid(), UserId = userId, Name = "", Currency = "", Cycle = "", Category = "" };
            Apply(sub, req);
            db.Subscriptions.Add(sub);
            await db.SaveChangesAsync();
            return Results.Created($"/api/subscriptions/{sub.Id}", SubscriptionResponse.From(sub));
        });

        subs.MapPut("/{id:guid}", async (Guid id, SubscriptionRequest req, ClaimsPrincipal principal, AppDb db) =>
        {
            if (Validate(req) is { } error) return Rules.Bad(error);
            var userId = principal.UserId();
            // Filtering by owner makes someone else's subscription indistinguishable from a missing one.
            var sub = await db.Subscriptions.FirstOrDefaultAsync(s => s.Id == id && s.UserId == userId);
            if (sub is null) return Results.NotFound();
            Apply(sub, req);
            await db.SaveChangesAsync();
            return Results.Ok(SubscriptionResponse.From(sub));
        });

        subs.MapDelete("/{id:guid}", async (Guid id, ClaimsPrincipal principal, AppDb db) =>
        {
            var userId = principal.UserId();
            var deleted = await db.Subscriptions.Where(s => s.Id == id && s.UserId == userId).ExecuteDeleteAsync();
            return deleted == 0 ? Results.NotFound() : Results.NoContent();
        });
    }

    private static string? Validate(SubscriptionRequest r)
    {
        if (string.IsNullOrWhiteSpace(r.Name) || r.Name.Trim().Length > Rules.MaxName) return "Enter the service name (up to 100 characters).";
        if (r.Amount is not (>= 0 and <= Rules.MaxAmount)) return "Enter a price of 0 or more.";
        if (r.Currency is null || !Rules.Currencies.Contains(r.Currency)) return "Unsupported currency.";
        if (r.Cycle is null || !Rules.Cycles.Contains(r.Cycle)) return "Unsupported billing cycle.";
        if (string.IsNullOrWhiteSpace(r.Category) || r.Category.Trim().Length > Rules.MaxCategory) return "Choose a category.";
        if (r.Next is not { Year: >= 2000 and <= 2200 }) return "Choose the next renewal date.";
        return null;
    }

    private static void Apply(Subscription sub, SubscriptionRequest r)
    {
        sub.Name = r.Name!.Trim();
        sub.Amount = r.Amount!.Value;
        sub.Currency = r.Currency!;
        sub.Cycle = r.Cycle!;
        sub.Category = r.Category!.Trim();
        sub.NextRenewal = r.Next!.Value;
        sub.Paused = r.Paused ?? false;
    }
}
