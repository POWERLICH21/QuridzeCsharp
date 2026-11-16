using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using QRCoder;
using Subfuse.Server.Data;
using Subfuse.Server.Security;

namespace Subfuse.Server.Api;

public record UpdateAccountRequest(string? Name, string? Currency, string? Theme);
public record ChangePasswordRequest(string? CurrentPassword, string? NewPassword);
public record PasswordRequest(string? Password);
public record CodeRequest(string? Code);
public record DisableTwoFactorRequest(string? Password, string? Code);

public static class AccountEndpoints
{
    private const string WrongPassword = "Your current password is incorrect.";

    public static void MapAccount(this IEndpointRouteBuilder app)
    {
        var me = app.MapGroup("/api/me").RequireAuthorization();

        me.MapGet("", async (ClaimsPrincipal principal, AppDb db, InboundSettings inbound) =>
        {
            if (await db.Users.FindAsync(principal.UserId()) is not { } user) return Results.Unauthorized();
            // Accounts created before forwarding existed get their private address the first time it is needed.
            if (user.InboundToken is null && inbound.Enabled)
            {
                user.InboundToken = InboundSettings.NewToken();
                await db.SaveChangesAsync();
            }
            return Results.Ok(UserResponse.From(user, inbound));
        });

        // Only the fields present in the body are changed.
        me.MapPatch("", async (UpdateAccountRequest req, ClaimsPrincipal principal, AppDb db, InboundSettings inbound) =>
        {
            if (await db.Users.FindAsync(principal.UserId()) is not { } user) return Results.Unauthorized();

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
            return Results.Ok(UserResponse.From(user, inbound));
        });

        // Everything the service holds about the user, as one downloadable file.
        me.MapGet("/export", async (ClaimsPrincipal principal, AppDb db, HttpContext http) =>
        {
            var userId = principal.UserId();
            if (await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId) is not { } user) return Results.Unauthorized();
            var subs = await db.Subscriptions.AsNoTracking().Where(s => s.UserId == userId).ToListAsync();
            http.Response.Headers.ContentDisposition = "attachment; filename=\"subfuse-export.json\"";
            return Results.Json(new
            {
                account = new { user.Email, user.Name, user.Currency, user.Theme, user.CreatedAt, user.TwoFactorEnabled },
                subscriptions = subs.Select(SubscriptionResponse.From),
            });
        });

        // The endpoints below change who can get into the account, so each asks for proof and is rate limited.
        var sensitive = me.MapGroup("").RequireRateLimiting("auth");

        sensitive.MapPost("/password", async (ChangePasswordRequest req, ClaimsPrincipal principal, AppDb db, AccountSecurity security, HttpContext http) =>
        {
            if (await db.Users.FindAsync(principal.UserId()) is not { } user) return Results.Unauthorized();
            if (!security.CheckPassword(user, req.CurrentPassword ?? "")) return Rules.Bad(WrongPassword);
            if (Rules.PasswordProblem(req.NewPassword) is { } problem) return Rules.Bad(problem);

            user.PasswordHash = security.HashPassword(user, req.NewPassword!);
            await AccountSecurity.SignOutEverywhereElse(http, user);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        sensitive.MapPost("/delete", async (PasswordRequest req, ClaimsPrincipal principal, AppDb db, AccountSecurity security, HttpContext http) =>
        {
            if (await db.Users.FindAsync(principal.UserId()) is not { } user) return Results.Unauthorized();
            if (!security.CheckPassword(user, req.Password ?? "")) return Rules.Bad(WrongPassword);

            db.Users.Remove(user); // subscriptions and recovery codes go with it (cascade)
            await db.SaveChangesAsync();
            await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.NoContent();
        });

        // Step 1: issue a secret and show it as a QR code. Nothing is enforced until step 2 succeeds.
        sensitive.MapPost("/2fa/setup", async (ClaimsPrincipal principal, AppDb db, AccountSecurity security) =>
        {
            if (await db.Users.FindAsync(principal.UserId()) is not { } user) return Results.Unauthorized();
            if (user.TwoFactorEnabled) return Rules.Bad("Two-factor sign-in is already on.");

            var secret = security.BeginTwoFactorSetup(user);
            await db.SaveChangesAsync();

            var uri = Totp.Uri("Subfuse", user.Email, secret);
            using var qrData = QRCodeGenerator.GenerateQrCode(uri, QRCodeGenerator.ECCLevel.M);
            var qrSvg = new SvgQRCode(qrData).GetGraphic(4);
            return Results.Ok(new { secret = Totp.Base32(secret), qrSvg });
        });

        // Step 2: prove the authenticator app works, then turn it on and hand out recovery codes.
        sensitive.MapPost("/2fa/enable", async (CodeRequest req, ClaimsPrincipal principal, AppDb db, AccountSecurity security, HttpContext http) =>
        {
            if (await db.Users.FindAsync(principal.UserId()) is not { } user) return Results.Unauthorized();
            if (user.TwoFactorEnabled) return Rules.Bad("Two-factor sign-in is already on.");
            if (user.TwoFactorSecret is null) return Rules.Bad("Start the setup again.");
            if (!security.CheckAuthenticatorCode(user, req.Code)) return Rules.Bad("That code is not valid. Check the time on your phone and try the next code.");

            user.TwoFactorEnabled = true;
            var recoveryCodes = await security.NewRecoveryCodes(user);
            await AccountSecurity.SignOutEverywhereElse(http, user);
            await db.SaveChangesAsync();
            return Results.Ok(new { recoveryCodes });
        });

        sensitive.MapPost("/2fa/disable", async (DisableTwoFactorRequest req, ClaimsPrincipal principal, AppDb db, AccountSecurity security, HttpContext http) =>
        {
            if (await db.Users.FindAsync(principal.UserId()) is not { } user) return Results.Unauthorized();
            if (!user.TwoFactorEnabled) return Rules.Bad("Two-factor sign-in is not on.");
            if (!security.CheckPassword(user, req.Password ?? "")) return Rules.Bad(WrongPassword);
            if (!await security.CheckSecondFactor(user, req.Code)) return Rules.Bad("That code is not valid.");

            await security.DisableTwoFactor(user);
            await AccountSecurity.SignOutEverywhereElse(http, user);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });
    }
}
