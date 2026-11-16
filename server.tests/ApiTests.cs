using System.Net;
using System.Net.Http.Json;
using Subfuse.Server.Data;
using Subfuse.Server.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Subfuse.Server.Tests;

/// <summary>Starts the real app against a private in-memory SQLite database.</summary>
public sealed class AppFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString = $"Data Source=file:test-{Guid.NewGuid():N}?mode=memory&cache=shared";
    private readonly string _keysPath = Path.Combine(Path.GetTempPath(), "subfuse-test-keys-" + Guid.NewGuid().ToString("N"));
    private SqliteConnection? _keepAlive;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // A shared in-memory database exists for as long as one connection to it stays open.
        _keepAlive = new SqliteConnection(_connectionString);
        _keepAlive.Open();
        builder.UseSetting("ConnectionStrings:Default", _connectionString);
        builder.UseSetting("DataProtection:KeysPath", _keysPath);
        builder.UseSetting("RateLimits:AuthPerMinute", "1000");
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        _keepAlive?.Dispose();
        if (Directory.Exists(_keysPath)) Directory.Delete(_keysPath, recursive: true);
    }

    /// <summary>A client with its own cookie jar, i.e. a separate browser.</summary>
    public HttpClient Browser() => CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
}

public class ApiTests(AppFactory factory) : IClassFixture<AppFactory>
{
    private static readonly object Netflixish = new { name = "Video streaming", amount = 15.49, currency = "USD", cycle = "monthly", category = "Streaming", next = "2026-12-01" };
    private record Sub(Guid Id, string Name, decimal Amount, string Currency, string Cycle, string Category, string Next, bool Paused);
    private record Me(string Email, string Name, string Currency, string Theme, bool TwoFactorEnabled);
    private record Setup(string Secret, string QrSvg);
    private record Enabled(string[] RecoveryCodes);

    private static string NewEmail() => $"user-{Guid.NewGuid():N}@example.com";

    private async Task<HttpClient> SignedUp(string? email = null, string password = "correct horse")
    {
        var client = factory.Browser();
        var res = await client.PostAsJsonAsync("/api/auth/register", new { name = "Nino", email = email ?? NewEmail(), password });
        res.EnsureSuccessStatusCode();
        return client;
    }

    [Fact]
    public async Task Anonymous_requests_are_rejected()
    {
        var client = factory.Browser();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/subscriptions")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/subscriptions", Netflixish)).StatusCode);
    }

    [Fact]
    public async Task Register_signs_in_and_normalizes_email()
    {
        var email = NewEmail();
        var client = await SignedUp("  " + email.ToUpperInvariant() + " ");
        var me = await client.GetFromJsonAsync<Me>("/api/me");
        Assert.Equal(email, me!.Email);
        Assert.Equal("Nino", me.Name);
        Assert.Equal("USD", me.Currency);
    }

    [Fact]
    public async Task Register_rejects_bad_input_and_duplicates()
    {
        var client = factory.Browser();
        var email = NewEmail();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/register", new { email = "not-an-email", password = "long enough" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/register", new { email, password = "short" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/register", new { email, password = "long enough" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await factory.Browser().PostAsJsonAsync("/api/auth/register", new { email = email.ToUpperInvariant(), password = "long enough" })).StatusCode);
    }

    [Fact]
    public async Task Login_requires_the_right_password_and_logout_ends_the_session()
    {
        var email = NewEmail();
        await SignedUp(email, "correct horse");

        var client = factory.Browser();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/login", new { email, password = "wrong horse" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/login", new { email = NewEmail(), password = "correct horse" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/me")).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/login", new { email, password = "correct horse" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/me")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/auth/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/me")).StatusCode);
    }

    [Fact]
    public async Task Passwords_are_stored_hashed()
    {
        var email = NewEmail();
        await SignedUp(email, "correct horse");
        using var scope = factory.Services.CreateScope();
        var user = await scope.ServiceProvider.GetRequiredService<AppDb>().Users.SingleAsync(u => u.Email == email);
        Assert.DoesNotContain("correct horse", user.PasswordHash);
        Assert.True(user.PasswordHash.Length > 40);
    }

    [Fact]
    public async Task Subscriptions_can_be_created_listed_updated_and_deleted()
    {
        var client = await SignedUp();

        var created = await client.PostAsJsonAsync("/api/subscriptions", Netflixish);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var sub = (await created.Content.ReadFromJsonAsync<Sub>())!;
        Assert.Equal(15.49m, sub.Amount);
        Assert.Equal("2026-12-01", sub.Next);
        Assert.False(sub.Paused);

        var updated = await client.PutAsJsonAsync($"/api/subscriptions/{sub.Id}",
            new { name = "Video HD", amount = 19.99, currency = "EUR", cycle = "yearly", category = "Streaming", next = "2027-01-15", paused = true });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);

        var list = (await client.GetFromJsonAsync<List<Sub>>("/api/subscriptions"))!;
        var only = Assert.Single(list);
        Assert.Equal(("Video HD", 19.99m, "EUR", "yearly", "2027-01-15", true), (only.Name, only.Amount, only.Currency, only.Cycle, only.Next, only.Paused));

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/subscriptions/{sub.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"/api/subscriptions/{sub.Id}")).StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<List<Sub>>("/api/subscriptions"))!);
    }

    [Theory]
    [InlineData("""{"name":"","amount":1,"currency":"USD","cycle":"monthly","category":"Other","next":"2026-12-01"}""")]
    [InlineData("""{"name":"x","amount":-1,"currency":"USD","cycle":"monthly","category":"Other","next":"2026-12-01"}""")]
    [InlineData("""{"name":"x","currency":"USD","cycle":"monthly","category":"Other","next":"2026-12-01"}""")]
    [InlineData("""{"name":"x","amount":1,"currency":"XXX","cycle":"monthly","category":"Other","next":"2026-12-01"}""")]
    [InlineData("""{"name":"x","amount":1,"currency":"USD","cycle":"daily","category":"Other","next":"2026-12-01"}""")]
    [InlineData("""{"name":"x","amount":1,"currency":"USD","cycle":"monthly","category":"Other"}""")]
    [InlineData("""{"name":"x","amount":1,"currency":"USD","cycle":"monthly","category":"Other","next":"not a date"}""")]
    public async Task Invalid_subscriptions_are_rejected(string json)
    {
        var client = await SignedUp();
        var res = await client.PostAsync("/api/subscriptions", new StringContent(json, System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<List<Sub>>("/api/subscriptions"))!);
    }

    [Fact]
    public async Task Users_cannot_see_or_change_each_others_subscriptions()
    {
        var alice = await SignedUp();
        var bob = await SignedUp();
        var sub = (await (await alice.PostAsJsonAsync("/api/subscriptions", Netflixish)).Content.ReadFromJsonAsync<Sub>())!;

        Assert.Empty((await bob.GetFromJsonAsync<List<Sub>>("/api/subscriptions"))!);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.PutAsJsonAsync($"/api/subscriptions/{sub.Id}", Netflixish)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.DeleteAsync($"/api/subscriptions/{sub.Id}")).StatusCode);
        Assert.Single((await alice.GetFromJsonAsync<List<Sub>>("/api/subscriptions"))!);
    }

    [Fact]
    public async Task Account_settings_update_only_the_given_fields()
    {
        var client = await SignedUp();
        var res = await client.PatchAsJsonAsync("/api/me", new { currency = "GEL", theme = "dark" });
        var me = (await res.Content.ReadFromJsonAsync<Me>())!;
        Assert.Equal(("Nino", "GEL", "dark"), (me.Name, me.Currency, me.Theme));
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PatchAsJsonAsync("/api/me", new { currency = "XXX" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PatchAsJsonAsync("/api/me", new { name = "   " })).StatusCode);
    }

    [Fact]
    public async Task Deleting_the_account_removes_its_data_and_invalidates_other_sessions()
    {
        var email = NewEmail();
        var client = await SignedUp(email);
        await client.PostAsJsonAsync("/api/subscriptions", Netflixish);
        var otherDevice = factory.Browser();
        (await otherDevice.PostAsJsonAsync("/api/auth/login", new { email, password = "correct horse" })).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/me/delete", new { password = "wrong horse" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/me/delete", new { password = "correct horse" })).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await otherDevice.GetAsync("/api/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await otherDevice.PostAsJsonAsync("/api/subscriptions", Netflixish)).StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDb>();
        Assert.False(await db.Users.AnyAsync(u => u.Email == email));
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.Browser().PostAsJsonAsync("/api/auth/login", new { email, password = "correct horse" })).StatusCode);
    }

    [Fact]
    public async Task Changing_the_password_signs_out_other_devices_and_replaces_the_old_password()
    {
        var email = NewEmail();
        var client = await SignedUp(email, "correct horse");
        var otherDevice = factory.Browser();
        (await otherDevice.PostAsJsonAsync("/api/auth/login", new { email, password = "correct horse" })).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/me/password", new { currentPassword = "wrong horse", newPassword = "battery staple" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/me/password", new { currentPassword = "correct horse", newPassword = "short" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/me/password", new { currentPassword = "correct horse", newPassword = "battery staple" })).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await otherDevice.GetAsync("/api/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.Browser().PostAsJsonAsync("/api/auth/login", new { email, password = "correct horse" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await factory.Browser().PostAsJsonAsync("/api/auth/login", new { email, password = "battery staple" })).StatusCode);
    }

    [Fact]
    public async Task Repeated_wrong_passwords_lock_the_account_even_for_the_right_password()
    {
        var email = NewEmail();
        await SignedUp(email, "correct horse");
        var client = factory.Browser();
        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/login", new { email, password = "wrong" + i })).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.PostAsJsonAsync("/api/auth/login", new { email, password = "correct horse" })).StatusCode);
    }

    /// <summary>Signs up and turns two-factor on; returns the authenticator secret and the recovery codes.</summary>
    private async Task<(HttpClient Client, string Email, byte[] Secret, string[] RecoveryCodes)> WithTwoFactor()
    {
        var email = NewEmail();
        var client = await SignedUp(email, "correct horse");
        var setup = (await (await client.PostAsync("/api/me/2fa/setup", null)).Content.ReadFromJsonAsync<Setup>())!;
        Assert.StartsWith("<svg", setup.QrSvg.TrimStart());
        var secret = FromBase32(setup.Secret);

        Assert.False((await client.GetFromJsonAsync<Me>("/api/me"))!.TwoFactorEnabled);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/me/2fa/enable", new { code = "000000" })).StatusCode);
        var res = await client.PostAsJsonAsync("/api/me/2fa/enable", new { code = Totp.Code(secret, Totp.StepAt(DateTimeOffset.UtcNow)) });
        res.EnsureSuccessStatusCode();
        var codes = (await res.Content.ReadFromJsonAsync<Enabled>())!.RecoveryCodes;
        Assert.Equal(8, codes.Distinct().Count());
        Assert.True((await client.GetFromJsonAsync<Me>("/api/me"))!.TwoFactorEnabled);
        return (client, email, secret, codes);
    }

    [Fact]
    public async Task Two_factor_sign_in_needs_a_fresh_code_from_the_authenticator()
    {
        var (_, email, secret, _) = await WithTwoFactor();
        var client = factory.Browser();

        var noCode = await client.PostAsJsonAsync("/api/auth/login", new { email, password = "correct horse" });
        Assert.Equal(HttpStatusCode.Unauthorized, noCode.StatusCode);
        Assert.Contains("twoFactorRequired", await noCode.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/login", new { email, password = "correct horse", code = "000000" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/me")).StatusCode);

        // The code used to enable two-factor has been spent, so sign in with the next one.
        var next = Totp.Code(secret, Totp.StepAt(DateTimeOffset.UtcNow) + 1);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/login", new { email, password = "correct horse", code = next })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/me")).StatusCode);

        // The same code cannot be replayed.
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.Browser().PostAsJsonAsync("/api/auth/login", new { email, password = "correct horse", code = next })).StatusCode);
        // A code never replaces the password.
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.Browser().PostAsJsonAsync("/api/auth/login", new { email, password = "wrong horse", code = next })).StatusCode);
    }

    [Fact]
    public async Task A_recovery_code_works_once()
    {
        var (_, email, _, codes) = await WithTwoFactor();
        Assert.Equal(HttpStatusCode.OK, (await factory.Browser().PostAsJsonAsync("/api/auth/login", new { email, password = "correct horse", code = codes[0].ToUpperInvariant() })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.Browser().PostAsJsonAsync("/api/auth/login", new { email, password = "correct horse", code = codes[0] })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await factory.Browser().PostAsJsonAsync("/api/auth/login", new { email, password = "correct horse", code = codes[1] })).StatusCode);
    }

    [Fact]
    public async Task Two_factor_secrets_and_recovery_codes_are_not_stored_readable()
    {
        var (_, email, secret, codes) = await WithTwoFactor();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDb>();
        var user = await db.Users.Include(u => u.RecoveryCodes).SingleAsync(u => u.Email == email);
        Assert.DoesNotContain(Totp.Base32(secret), user.TwoFactorSecret);
        Assert.DoesNotContain(Convert.ToBase64String(secret), user.TwoFactorSecret);
        Assert.Equal(8, user.RecoveryCodes.Count);
        Assert.DoesNotContain(user.RecoveryCodes, c => codes.Any(code => c.CodeHash.Contains(code.Replace("-", ""), StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task Turning_two_factor_off_needs_the_password_and_a_code()
    {
        var (client, email, _, codes) = await WithTwoFactor();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/me/2fa/disable", new { password = "wrong horse", code = codes[0] })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/me/2fa/disable", new { password = "correct horse", code = "000000" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/me/2fa/disable", new { password = "correct horse", code = codes[0] })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await factory.Browser().PostAsJsonAsync("/api/auth/login", new { email, password = "correct horse" })).StatusCode);
    }

    [Fact]
    public async Task Export_contains_the_account_and_its_subscriptions_but_no_secrets()
    {
        var client = await SignedUp();
        await client.PostAsJsonAsync("/api/subscriptions", Netflixish);
        var res = await client.GetAsync("/api/me/export");
        var body = await res.Content.ReadAsStringAsync();
        Assert.Contains("attachment", res.Content.Headers.ContentDisposition!.ToString());
        Assert.Contains("Video streaming", body);
        Assert.DoesNotContain("passwordHash", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("stamp", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Responses_carry_browser_security_headers()
    {
        var client = factory.Browser();
        var page = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains("default-src 'self'", string.Join(";", page.Headers.GetValues("Content-Security-Policy")));
        Assert.Equal("nosniff", page.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", page.Headers.GetValues("X-Frame-Options").Single());
        var api = await client.GetAsync("/api/me");
        Assert.Equal("no-store", api.Headers.CacheControl!.ToString());
    }

    private static byte[] FromBase32(string text)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bytes = new List<byte>();
        int buffer = 0, bits = 0;
        foreach (var c in text)
        {
            buffer = (buffer << 5) | alphabet.IndexOf(c);
            bits += 5;
            if (bits >= 8) { bytes.Add((byte)(buffer >> (bits - 8))); bits -= 8; }
        }
        return bytes.ToArray();
    }
}

public class TotpTests
{
    // Test vectors from RFC 6238, appendix B (SHA-1, truncated to 6 digits).
    [Theory]
    [InlineData(59, "287082")]
    [InlineData(1111111109, "081804")]
    [InlineData(1234567890, "005924")]
    [InlineData(20000000000, "353130")]
    public void Codes_match_the_published_standard(long unixSeconds, string expected)
    {
        var secret = System.Text.Encoding.ASCII.GetBytes("12345678901234567890");
        Assert.Equal(expected, Totp.Code(secret, Totp.StepAt(DateTimeOffset.FromUnixTimeSeconds(unixSeconds))));
    }

    [Fact]
    public void Base32_matches_the_published_standard()
    {
        Assert.Equal("MZXW6YTBOI", Totp.Base32(System.Text.Encoding.ASCII.GetBytes("foobar")));
    }
}

public class RateLimitTests
{
    [Fact]
    public async Task Repeated_sign_in_attempts_are_throttled()
    {
        using var factory = new AppFactory().WithWebHostBuilder(b => b.UseSetting("RateLimits:AuthPerMinute", "3"));
        var client = factory.CreateClient();
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 5; i++)
            statuses.Add((await client.PostAsJsonAsync("/api/auth/login", new { email = "nobody@example.com", password = "guess" + i })).StatusCode);
        Assert.Equal(3, statuses.Count(s => s == HttpStatusCode.Unauthorized));
        Assert.Equal(2, statuses.Count(s => s == HttpStatusCode.TooManyRequests));
    }
}
