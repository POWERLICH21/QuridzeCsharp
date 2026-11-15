using System.Security.Claims;
using Allsubs.Server.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Allsubs.Server.Api;

public record RegisterRequest(string? Name, string? Email, string? Password);
public record LoginRequest(string? Email, string? Password);
public record UserResponse(string Email, string Name, string Currency, string Theme)
{
    public static UserResponse From(User u) => new(u.Email, u.Name, u.Currency, u.Theme);
}

public static class AuthEndpoints
{
    // Verified against when the email is unknown, so a missing account takes as long as a wrong password.
    private static readonly string DummyHash = new PasswordHasher<User>().HashPassword(null!, "not-a-real-password");

    public static void MapAuth(this IEndpointRouteBuilder app)
    {
        var auth = app.MapGroup("/api/auth").RequireRateLimiting("auth");

        auth.MapPost("/register", async (RegisterRequest req, AppDb db, IPasswordHasher<User> hasher, HttpContext http) =>
        {
            var email = Rules.NormalizeEmail(req.Email ?? "");
            if (!Rules.IsEmail(email)) return Rules.Bad("Enter a valid email address.");
            if (req.Password is not { Length: >= Rules.MinPassword and <= Rules.MaxPassword })
                return Rules.Bad($"Password must be at least {Rules.MinPassword} characters.");
            var name = (req.Name ?? "").Trim();
            if (name.Length == 0) name = email.Split('@')[0];
            if (name.Length > Rules.MaxName) return Rules.Bad("Name is too long.");

            if (await db.Users.AnyAsync(u => u.Email == email))
                return Results.Conflict(new { error = "An account with this email already exists. Sign in instead." });

            var user = new User { Id = Guid.NewGuid(), Email = email, Name = name, PasswordHash = "", CreatedAt = DateTime.UtcNow };
            user.PasswordHash = hasher.HashPassword(user, req.Password);
            db.Users.Add(user);
            try { await db.SaveChangesAsync(); }
            catch (DbUpdateException)
            {
                // Two registrations for the same email raced; the unique index rejected this one.
                return Results.Conflict(new { error = "An account with this email already exists. Sign in instead." });
            }

            await SignIn(http, user);
            return Results.Ok(UserResponse.From(user));
        });

        auth.MapPost("/login", async (LoginRequest req, AppDb db, IPasswordHasher<User> hasher, HttpContext http) =>
        {
            var email = Rules.NormalizeEmail(req.Email ?? "");
            var password = req.Password ?? "";
            if (password.Length > Rules.MaxPassword) password = "";
            var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email);

            var result = hasher.VerifyHashedPassword(user!, user?.PasswordHash ?? DummyHash, password);
            if (user is null || result == PasswordVerificationResult.Failed)
                return Results.Json(new { error = "Email or password is incorrect." }, statusCode: StatusCodes.Status401Unauthorized);

            if (result == PasswordVerificationResult.SuccessRehashNeeded)
            {
                user.PasswordHash = hasher.HashPassword(user, password);
                await db.SaveChangesAsync();
            }
            await SignIn(http, user);
            return Results.Ok(UserResponse.From(user));
        });

        auth.MapPost("/logout", async (HttpContext http) =>
        {
            await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.NoContent();
        });
    }

    private static Task SignIn(HttpContext http, User user)
    {
        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, user.Id.ToString())],
            CookieAuthenticationDefaults.AuthenticationScheme);
        return http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity),
            new AuthenticationProperties { IsPersistent = true });
    }

    /// <summary>The signed-in user's id. Only call from endpoints that require authorization.</summary>
    public static Guid UserId(this ClaimsPrincipal principal) =>
        Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
}
