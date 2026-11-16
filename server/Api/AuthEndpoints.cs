using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Subfuse.Server.Data;
using Subfuse.Server.Security;

namespace Subfuse.Server.Api;

public record RegisterRequest(string? Name, string? Email, string? Password);
public record LoginRequest(string? Email, string? Password, string? Code);
public record UserResponse(string Email, string Name, string Currency, string Theme, bool TwoFactorEnabled, string? InboundAddress)
{
    public static UserResponse From(User u, InboundSettings inbound) =>
        new(u.Email, u.Name, u.Currency, u.Theme, u.TwoFactorEnabled, inbound.AddressFor(u));
}

public static class AuthEndpoints
{
    public static void MapAuth(this IEndpointRouteBuilder app)
    {
        var auth = app.MapGroup("/api/auth").RequireRateLimiting("auth");

        auth.MapPost("/register", async (RegisterRequest req, AppDb db, AccountSecurity security, TimeProvider clock, InboundSettings inbound, HttpContext http) =>
        {
            var email = Rules.NormalizeEmail(req.Email ?? "");
            if (!Rules.IsEmail(email)) return Rules.Bad("Enter a valid email address.");
            if (Rules.PasswordProblem(req.Password) is { } problem) return Rules.Bad(problem);
            var name = (req.Name ?? "").Trim();
            if (name.Length == 0) name = email.Split('@')[0];
            if (name.Length > Rules.MaxName) return Rules.Bad("Name is too long.");

            if (await db.Users.AnyAsync(u => u.Email == email))
                return Results.Conflict(new { error = "An account with this email already exists. Sign in instead." });

            var user = new User { Id = Guid.NewGuid(), Email = email, Name = name, PasswordHash = "", CreatedAt = clock.GetUtcNow().UtcDateTime, InboundToken = InboundSettings.NewToken() };
            user.PasswordHash = security.HashPassword(user, req.Password!);
            db.Users.Add(user);
            try { await db.SaveChangesAsync(); }
            catch (DbUpdateException)
            {
                // Two registrations for the same email raced; the unique index rejected this one.
                return Results.Conflict(new { error = "An account with this email already exists. Sign in instead." });
            }

            await AccountSecurity.SignIn(http, user);
            return Results.Ok(UserResponse.From(user, inbound));
        });

        auth.MapPost("/login", async (LoginRequest req, AppDb db, AccountSecurity security, InboundSettings inbound, HttpContext http) =>
        {
            var email = Rules.NormalizeEmail(req.Email ?? "");
            var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email);

            if (user is not null && security.IsLockedOut(user))
                return Results.Json(new { error = "Too many failed attempts. Try again in a few minutes." }, statusCode: StatusCodes.Status429TooManyRequests);

            if (!security.CheckPassword(user, req.Password ?? ""))
            {
                if (user is not null) { security.RecordFailure(user); await db.SaveChangesAsync(); }
                return Unauthorized("Email or password is incorrect.");
            }

            if (user!.TwoFactorEnabled)
            {
                // The password was right; the client now asks for the code and sends the whole form again.
                if (string.IsNullOrWhiteSpace(req.Code))
                    return Results.Json(new { error = "Enter the code from your authenticator app.", twoFactorRequired = true }, statusCode: StatusCodes.Status401Unauthorized);
                if (!await security.CheckSecondFactor(user, req.Code))
                {
                    security.RecordFailure(user);
                    await db.SaveChangesAsync();
                    return Results.Json(new { error = "That code is not valid. Try the next code from your app.", twoFactorRequired = true }, statusCode: StatusCodes.Status401Unauthorized);
                }
            }

            security.RecordSuccess(user);
            await db.SaveChangesAsync();
            await AccountSecurity.SignIn(http, user);
            return Results.Ok(UserResponse.From(user, inbound));
        });

        auth.MapPost("/logout", async (HttpContext http) =>
        {
            await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.NoContent();
        });
    }

    public static IResult Unauthorized(string message) =>
        Results.Json(new { error = message }, statusCode: StatusCodes.Status401Unauthorized);
}
