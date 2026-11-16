using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Subfuse.Server.Data;

namespace Subfuse.Server.Security;

/// <summary>Passwords, second-factor codes, lockout and sessions: everything that decides who may sign in.</summary>
public class AccountSecurity(AppDb db, IPasswordHasher<User> hasher, IDataProtectionProvider protection, TimeProvider clock)
{
    public const string StampClaim = "stamp";
    private const int MaxFailures = 5;
    private static readonly TimeSpan LockoutTime = TimeSpan.FromMinutes(5);
    private const int RecoveryCodeCount = 8;

    // Verified against when the email is unknown, so a missing account takes as long as a wrong password.
    private static readonly string DummyHash = new PasswordHasher<User>().HashPassword(null!, "not-a-real-password");

    private readonly IDataProtector _secrets = protection.CreateProtector("Subfuse.TwoFactorSecret.v1");

    public string HashPassword(User user, string password) => hasher.HashPassword(user, password);

    /// <summary>Checks a password; upgrades the stored hash if the hashing settings have moved on. Does not save.</summary>
    public bool CheckPassword(User? user, string password)
    {
        if (password.Length > Rules.MaxPassword) password = "";
        var result = hasher.VerifyHashedPassword(user!, user?.PasswordHash ?? DummyHash, password);
        if (user is null || result == PasswordVerificationResult.Failed) return false;
        if (result == PasswordVerificationResult.SuccessRehashNeeded) user.PasswordHash = hasher.HashPassword(user, password);
        return true;
    }

    public bool IsLockedOut(User user) => user.LockoutUntil is { } until && until > clock.GetUtcNow().UtcDateTime;

    /// <summary>Counts a failed attempt and locks the account for a few minutes after too many. Does not save.</summary>
    public void RecordFailure(User user)
    {
        if (++user.FailedLogins < MaxFailures) return;
        user.FailedLogins = 0;
        user.LockoutUntil = clock.GetUtcNow().UtcDateTime + LockoutTime;
    }

    public void RecordSuccess(User user)
    {
        user.FailedLogins = 0;
        user.LockoutUntil = null;
    }

    // ----- Two-factor -----

    /// <summary>Creates a new, not-yet-enabled authenticator secret for the user and returns it. Does not save.</summary>
    public byte[] BeginTwoFactorSetup(User user)
    {
        var secret = Totp.NewSecret();
        user.TwoFactorSecret = _secrets.Protect(Convert.ToBase64String(secret));
        user.TwoFactorLastStep = 0;
        return secret;
    }

    /// <summary>Accepts a 6-digit authenticator code once. Does not save.</summary>
    public bool CheckAuthenticatorCode(User user, string? code)
    {
        if (user.TwoFactorSecret is null || code is null) return false;
        byte[] secret;
        try { secret = Convert.FromBase64String(_secrets.Unprotect(user.TwoFactorSecret)); }
        catch (CryptographicException) { return false; } // keys were lost; recovery codes still work
        var digits = code.Replace(" ", "");
        if (Totp.Verify(secret, digits, clock.GetUtcNow(), user.TwoFactorLastStep) is not { } step) return false;
        user.TwoFactorLastStep = step;
        return true;
    }

    /// <summary>Accepts an authenticator code or an unused recovery code (which is then consumed). Does not save.</summary>
    public async Task<bool> CheckSecondFactor(User user, string? code)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length > 40) return false;
        if (CheckAuthenticatorCode(user, code)) return true;
        var hash = HashRecoveryCode(code);
        var match = await db.RecoveryCodes.FirstOrDefaultAsync(c => c.UserId == user.Id && c.CodeHash == hash);
        if (match is null) return false;
        db.RecoveryCodes.Remove(match);
        return true;
    }

    /// <summary>Replaces the user's recovery codes and returns the new ones in readable form. Does not save.</summary>
    public async Task<string[]> NewRecoveryCodes(User user)
    {
        db.RecoveryCodes.RemoveRange(await db.RecoveryCodes.Where(c => c.UserId == user.Id).ToListAsync());
        var codes = new string[RecoveryCodeCount];
        for (var i = 0; i < codes.Length; i++)
        {
            var raw = Totp.Base32(RandomNumberGenerator.GetBytes(7))[..10].ToLowerInvariant(); // 50 bits
            codes[i] = $"{raw[..5]}-{raw[5..]}";
            db.RecoveryCodes.Add(new RecoveryCode { Id = Guid.NewGuid(), UserId = user.Id, CodeHash = HashRecoveryCode(codes[i]) });
        }
        return codes;
    }

    /// <summary>Turns two-factor off and discards its secret and recovery codes. Does not save.</summary>
    public async Task DisableTwoFactor(User user)
    {
        user.TwoFactorEnabled = false;
        user.TwoFactorSecret = null;
        user.TwoFactorLastStep = 0;
        db.RecoveryCodes.RemoveRange(await db.RecoveryCodes.Where(c => c.UserId == user.Id).ToListAsync());
    }

    // Recovery codes are random and long, so a fast hash is enough to keep them unreadable in the database.
    private static string HashRecoveryCode(string code)
    {
        var normalized = code.Replace("-", "").Replace(" ", "").ToLowerInvariant();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
    }

    // ----- Sessions -----

    public static Task SignIn(HttpContext http, User user)
    {
        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()), new Claim(StampClaim, user.SecurityStamp)],
            CookieAuthenticationDefaults.AuthenticationScheme);
        return http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity),
            new AuthenticationProperties { IsPersistent = true });
    }

    /// <summary>Signs out every other session of this user and keeps the current one. Does not save.</summary>
    public static Task SignOutEverywhereElse(HttpContext http, User user)
    {
        user.SecurityStamp = Guid.NewGuid().ToString("N");
        return SignIn(http, user);
    }

    /// <summary>Runs on every authenticated request: the session ends if the account is gone or its stamp changed.</summary>
    public static async Task ValidateSession(CookieValidatePrincipalContext ctx)
    {
        var principal = ctx.Principal!;
        var stamp = principal.FindFirstValue(StampClaim);
        var current = Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
            ? await ctx.HttpContext.RequestServices.GetRequiredService<AppDb>().Users
                .Where(u => u.Id == id).Select(u => u.SecurityStamp).FirstOrDefaultAsync()
            : null;
        if (current is null || current != stamp)
        {
            ctx.RejectPrincipal();
            await ctx.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        }
    }
}

public static class PrincipalExtensions
{
    /// <summary>The signed-in user's id. Only call from endpoints that require authorization.</summary>
    public static Guid UserId(this ClaimsPrincipal principal) =>
        Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
}
