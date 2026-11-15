using System.Net;
using System.Net.Http.Json;
using Allsubs.Server.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Allsubs.Server.Tests;

/// <summary>Starts the real app against a private in-memory SQLite database.</summary>
public sealed class AppFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        _connection.Open(); // the database lives as long as this connection
        builder.UseSetting("RateLimits:AuthPerMinute", "1000");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<AppDb>>();
            services.AddDbContext<AppDb>(o => o.UseSqlite(_connection));
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        _connection.Dispose();
    }

    /// <summary>A client with its own cookie jar, i.e. a separate browser.</summary>
    public HttpClient Browser() => CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
}

public class ApiTests(AppFactory factory) : IClassFixture<AppFactory>
{
    private static readonly object Netflixish = new { name = "Video streaming", amount = 15.49, currency = "USD", cycle = "monthly", category = "Streaming", next = "2026-12-01" };
    private record Sub(Guid Id, string Name, decimal Amount, string Currency, string Cycle, string Category, string Next, bool Paused);
    private record Me(string Email, string Name, string Currency, string Theme);

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

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync("/api/me")).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await otherDevice.GetAsync("/api/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await otherDevice.PostAsJsonAsync("/api/subscriptions", Netflixish)).StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDb>();
        Assert.False(await db.Users.AnyAsync(u => u.Email == email));
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.Browser().PostAsJsonAsync("/api/auth/login", new { email, password = "correct horse" })).StatusCode);
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
