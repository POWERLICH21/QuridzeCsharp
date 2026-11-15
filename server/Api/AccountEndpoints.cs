using System.Security.Claims;
using Allsubs.Server.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Allsubs.Server.Api;

public record UpdateAccountRequest(string? Name, string? Currency, string? Theme);

public static class AccountEndpoints
{
    public static void MapAccount(this IEndpointRouteBuilder app)
    {
        var me = app.MapGroup("/api/me").RequireAuthorization();

        me.MapGet("", async (ClaimsPrincipal principal, AppDb db, HttpContext http) =>
            await Find(principal, db, http) is { } user ? Results.Ok(UserResponse.From(user)) : Results.Unauthorized());

        // Only the fields present in the body are changed.
        me.MapPatch("", async (UpdateAccountRequest req, ClaimsPrincipal principal, AppDb db, HttpContext http) =>
        {
            if (await Find(principal, db, http) is not { } user) return Results.Unauthorized();

            if (req.Name is not null)
            {
                var name = req.Name.Trim();
                if (name.Length is 0 or > Rules.MaxName) return Rules.Bad("Enter a name of up to 100 characters.");
                user.Name = name;
            }
            if (req.Currency is not null)
            {
                if (!Rules.Currencies.Contains(req.Currency)) return Rules.Bad("Unsupported currency.");
                user.Currency = req.Currency;
            }
            if (req.Theme is not null)
            {
                if (!Rules.Themes.Contains(req.Theme)) return Rules.Bad("Unsupported theme.");
                user.Theme = req.Theme;
            }
            await db.SaveChangesAsync();
            return Results.Ok(UserResponse.From(user));
        });

        me.MapDelete("", async (ClaimsPrincipal principal, AppDb db, HttpContext http) =>
        {
            if (await Find(principal, db, http) is { } user)
            {
                db.Users.Remove(user); // subscriptions go with it (cascade)
                await db.SaveChangesAsync();
            }
            await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.NoContent();
        });
    }

    /// <summary>Loads the signed-in user; clears the cookie if the account no longer exists.</summary>
    private static async Task<User?> Find(ClaimsPrincipal principal, AppDb db, HttpContext http)
    {
        var user = await db.Users.FindAsync(principal.UserId());
        if (user is null) await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return user;
    }
}
