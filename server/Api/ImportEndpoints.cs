using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Subfuse.Server.Data;
using Subfuse.Server.Import;
using Subfuse.Server.Security;

namespace Subfuse.Server.Api;

/// <summary>Settings for receiving forwarded receipts. Both must be set for the feature to be offered.</summary>
public record InboundSettings(string? Domain, string? Secret)
{
    public bool Enabled => !string.IsNullOrEmpty(Domain) && !string.IsNullOrEmpty(Secret);

    /// <summary>The private address a user forwards receipts to, or null when the feature is off.</summary>
    public string? AddressFor(User user) => Enabled && user.InboundToken is not null ? $"{user.InboundToken}@{Domain}" : null;

    public static string NewToken() => "u-" + Totp.Base32(RandomNumberGenerator.GetBytes(10)).ToLowerInvariant();
}

public record ImportTextRequest(string? Text);

/// <summary>An incoming email as posted by the mail provider. Field names follow Postmark's inbound format.</summary>
public record InboundEmail(string? From, string? To, string? OriginalRecipient, string? Subject, string? TextBody);

public record SuggestionResponse(Guid Id, string Name, decimal? Amount, string Currency, string Cycle, string Category, DateOnly Next, string Source, string? Note)
{
    public static SuggestionResponse From(Suggestion s) =>
        new(s.Id, s.Name, s.Amount, s.Currency, s.Cycle, s.Category, s.NextRenewal, s.Source, s.Note);
}

public static partial class ImportEndpoints
{
    private const int MaxPendingPerUser = 100;
    private const int MaxPastedLength = 20_000;

    [GeneratedRegex(@"\b\d{6,12}\b")]
    private static partial Regex ConfirmationCode();

    public static void MapImport(this IEndpointRouteBuilder app)
    {
        var suggestions = app.MapGroup("/api/suggestions").RequireAuthorization();

        suggestions.MapGet("", async (ClaimsPrincipal principal, AppDb db) =>
        {
            var userId = principal.UserId();
            var list = await db.Suggestions.AsNoTracking().Where(s => s.UserId == userId).OrderBy(s => s.CreatedAt).ToListAsync();
            return list.Select(SuggestionResponse.From);
        });

        // Used both to dismiss a suggestion and to clear it once the user has saved it as a subscription.
        suggestions.MapDelete("/{id:guid}", async (Guid id, ClaimsPrincipal principal, AppDb db) =>
        {
            var userId = principal.UserId();
            var deleted = await db.Suggestions.Where(s => s.Id == id && s.UserId == userId).ExecuteDeleteAsync();
            return deleted == 0 ? Results.NotFound() : Results.NoContent();
        });

        // The user pastes a receipt or a list of subscriptions; what the reader finds becomes suggestions.
        app.MapPost("/api/import/text", async (ImportTextRequest req, ClaimsPrincipal principal, AppDb db, ReceiptReader reader, TimeProvider clock) =>
        {
            if (string.IsNullOrWhiteSpace(req.Text)) return Rules.Bad("Paste some text first.");
            if (req.Text.Length > MaxPastedLength) return Rules.Bad("That is too much text. Paste a shorter piece.");

            var found = reader.Read(null, null, req.Text);
            var added = await Store(db, principal.UserId(), found, "paste", clock);
            return Results.Ok(new { found = found.Count, added = added.Select(SuggestionResponse.From) });
        }).RequireAuthorization().RequireRateLimiting("auth");

        // Called by the mail provider for each email sent to a user's private forwarding address.
        app.MapPost("/api/inbound/email", async (InboundEmail email, HttpContext http, InboundSettings inbound, AppDb db, ReceiptReader reader, TimeProvider clock) =>
        {
            if (!inbound.Enabled) return Results.NotFound();
            if (!HasSecret(http, inbound.Secret!)) return Results.Unauthorized();

            // Whatever happens next, answer 200: a provider retries on errors, and a stranger probing
            // addresses should not learn which ones exist.
            var token = TokenIn(email.OriginalRecipient) ?? TokenIn(email.To);
            var user = token is null ? null : await db.Users.FirstOrDefaultAsync(u => u.InboundToken == token);
            if (user is null) return Results.Ok();

            // Gmail asks the receiving address to confirm before it will forward automatically.
            if ((email.From ?? "").Contains("forwarding-noreply@google.com", StringComparison.OrdinalIgnoreCase))
            {
                var code = ConfirmationCode().Match($"{email.Subject} {email.TextBody}");
                if (code.Success && await db.Suggestions.CountAsync(s => s.UserId == user.Id) < MaxPendingPerUser)
                {
                    db.Suggestions.Add(new Suggestion
                    {
                        Id = Guid.NewGuid(), UserId = user.Id, Name = "Gmail forwarding confirmation", Currency = "USD", Cycle = "monthly",
                        Category = "Other", Source = "notice", Note = code.Value,
                        NextRenewal = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime), CreatedAt = clock.GetUtcNow().UtcDateTime,
                    });
                    await db.SaveChangesAsync();
                }
                return Results.Ok();
            }

            await Store(db, user.Id, reader.Read(email.From, email.Subject, email.TextBody), "email", clock);
            return Results.Ok();
        }).RequireRateLimiting("inbound");
    }

    /// <summary>Saves what the reader found as suggestions, skipping ones the user already has or has pending.</summary>
    private static async Task<List<Suggestion>> Store(AppDb db, Guid userId, List<Detected> found, string source, TimeProvider clock)
    {
        var added = new List<Suggestion>();
        if (found.Count == 0) return added;

        var known = (await db.Subscriptions.Where(s => s.UserId == userId).Select(s => s.Name).ToListAsync())
            .Concat(await db.Suggestions.Where(s => s.UserId == userId).Select(s => s.Name).ToListAsync())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var room = MaxPendingPerUser - await db.Suggestions.CountAsync(s => s.UserId == userId);
        var now = clock.GetUtcNow().UtcDateTime;
        var today = DateOnly.FromDateTime(now);

        foreach (var item in found)
        {
            if (added.Count >= room || !known.Add(item.Name)) continue;
            var suggestion = new Suggestion
            {
                Id = Guid.NewGuid(), UserId = userId, Name = item.Name, Amount = item.Amount,
                Currency = Rules.Currencies.Contains(item.Currency) ? item.Currency : "USD",
                Cycle = item.Cycle, Category = item.Category, Source = source, CreatedAt = now,
                // A receipt marks a charge that just happened, so the next one is a cycle away.
                NextRenewal = item.Cycle switch
                {
                    "weekly" => today.AddDays(7), "quarterly" => today.AddMonths(3), "yearly" => today.AddYears(1), _ => today.AddMonths(1),
                },
            };
            db.Suggestions.Add(suggestion);
            added.Add(suggestion);
        }
        await db.SaveChangesAsync();
        return added;
    }

    /// <summary>The provider proves itself with the shared secret, as a header or as the password of HTTP Basic auth.</summary>
    private static bool HasSecret(HttpContext http, string secret)
    {
        string? given = http.Request.Headers["X-Inbound-Secret"];
        if (given is null && http.Request.Headers.Authorization.ToString() is { } auth && auth.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(auth[6..].Trim()));
                given = decoded[(decoded.IndexOf(':') + 1)..];
            }
            catch (FormatException) { return false; }
        }
        return given is not null && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(given), Encoding.UTF8.GetBytes(secret));
    }

    /// <summary>Pulls "u-abc123" out of "Name &lt;u-abc123@in.example.com&gt;".</summary>
    private static string? TokenIn(string? recipients)
    {
        if (recipients is null) return null;
        var match = Regex.Match(recipients, @"\bu-[a-z2-7]{16}(?=@)", RegexOptions.IgnoreCase);
        return match.Success ? match.Value.ToLowerInvariant() : null;
    }
}
