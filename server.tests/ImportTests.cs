using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Subfuse.Server.Import;
using Xunit;

namespace Subfuse.Server.Tests;

public class ReceiptReaderTests
{
    private static readonly ReceiptReader Reader = new(Catalog.Load(Path.Combine(AppContext.BaseDirectory, "catalog.json")));

    [Fact]
    public void Reads_a_receipt_email_from_a_known_sender()
    {
        var found = Reader.Read("OpenAI <noreply@tm.openai.com>", "Your receipt from OpenAI",
            "Thanks for your payment.\nChatGPT Plus Subscription\nSubtotal $20.00\nTax $0.00\nTotal $20.00\nRenews monthly.");
        var one = Assert.Single(found);
        Assert.Equal(("ChatGPT Plus", 20m, "USD", "monthly", "AI"), (one.Name, one.Amount, one.Currency, one.Cycle, one.Category));
    }

    [Fact]
    public void Picks_the_plan_that_matches_the_price_paid()
    {
        var one = Assert.Single(Reader.Read("billing@openai.com", "Receipt", "ChatGPT subscription. Amount paid: $200.00"));
        Assert.Equal("ChatGPT Pro", one.Name);
    }

    [Fact]
    public void Finds_the_original_sender_inside_a_manually_forwarded_email()
    {
        var found = Reader.Read("someone@gmail.com", "Fwd: Your Claude receipt",
            "---------- Forwarded message ---------\nFrom: Anthropic <billing@mail.anthropic.com>\nClaude Pro — Total: €21,60 billed every month");
        var one = Assert.Single(found);
        Assert.Equal(("Claude Pro", 21.60m, "EUR"), (one.Name, one.Amount, one.Currency));
    }

    [Fact]
    public void Reads_a_pasted_list_with_one_price_per_service()
    {
        var found = Reader.Read(null, null,
            "Subscriptions\nSpotify Premium\n€10.99 per month\nRenews 3 Dec\n\niCloud+ 200 GB\n$2.99 / month\n\nNintendo Switch Online\n19.99 USD yearly\n\nNetflix\nExpired");
        Assert.Equal(["Spotify Premium", "iCloud+ 200 GB", "Nintendo Switch Online", "Netflix"], found.Select(f => f.Name).ToArray());
        Assert.Equal([10.99m, 2.99m, 19.99m, null], found.Select(f => f.Amount).ToArray());
        Assert.Equal("EUR", found[0].Currency);
        Assert.Equal("yearly", found[2].Cycle);
    }

    [Fact]
    public void Suggests_an_unknown_sender_only_when_the_email_reads_like_a_receipt()
    {
        var receipt = Assert.Single(Reader.Read("billing@mail.acme-tools.com", "Invoice for your subscription", "Total 1.299,00 GEL charged yearly"));
        Assert.Equal(("Acme-tools", 1299.00m, "GEL", "yearly", "Other"), (receipt.Name, receipt.Amount, receipt.Currency, receipt.Cycle, receipt.Category));
        Assert.Empty(Reader.Read("friend@example.com", "Lunch?", "I owe you $12.50 for lunch"));
        Assert.Empty(Reader.Read(null, null, "Total $20.00 for a subscription to something unknown"));
    }

    [Fact]
    public void Does_not_match_a_service_name_inside_another_word()
    {
        Assert.Empty(Reader.Read(null, null, "A notional amount of $5.00 for the antidal subscription"));
    }
}

public class ImportApiTests(AppFactory factory) : IClassFixture<AppFactory>
{
    private const string Secret = "test-inbound-secret";
    private record Suggestion(Guid Id, string Name, decimal? Amount, string Currency, string Cycle, string Category, string Next, string Source, string? Note);
    private record Me(string Email, string? InboundAddress);
    private record Imported(int Found, List<Suggestion> Added);

    private WebApplicationFactory<Program> WithForwarding() => factory.WithWebHostBuilder(b =>
    {
        b.UseSetting("Inbound:Domain", "in.subfuse.test");
        b.UseSetting("Inbound:Secret", Secret);
    });

    private static async Task<HttpClient> SignedUp(WebApplicationFactory<Program> app)
    {
        var client = app.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        (await client.PostAsJsonAsync("/api/auth/register", new { email = $"user-{Guid.NewGuid():N}@example.com", password = "correct horse" })).EnsureSuccessStatusCode();
        return client;
    }

    private static Task<HttpResponseMessage> Deliver(HttpClient provider, string to, string from, string subject, string body, string? secret = Secret)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/inbound/email") { Content = JsonContent.Create(new { From = from, To = to, Subject = subject, TextBody = body }) };
        if (secret is not null) request.Headers.Add("X-Inbound-Secret", secret);
        return provider.SendAsync(request);
    }

    [Fact]
    public async Task The_catalogue_is_served_to_the_pages()
    {
        var res = await factory.CreateClient().GetAsync("/catalog.json");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Contains("ChatGPT Plus", await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Pasted_text_becomes_suggestions_that_can_be_dismissed()
    {
        var client = await SignedUp(factory);
        var imported = (await (await client.PostAsJsonAsync("/api/import/text", new { text = "Perplexity Pro $20.00 monthly\nDropbox Plus 11.99 USD" })).Content.ReadFromJsonAsync<Imported>())!;
        Assert.Equal(2, imported.Added.Count);

        // Pasting the same thing again adds nothing new.
        var again = (await (await client.PostAsJsonAsync("/api/import/text", new { text = "Perplexity Pro $20.00 monthly" })).Content.ReadFromJsonAsync<Imported>())!;
        Assert.Equal((1, 0), (again.Found, again.Added.Count));

        var list = (await client.GetFromJsonAsync<List<Suggestion>>("/api/suggestions"))!;
        Assert.Equal(["Perplexity Pro", "Dropbox Plus"], list.Select(s => s.Name).ToArray());
        Assert.All(list, s => Assert.Equal("paste", s.Source));

        var stranger = await SignedUp(factory);
        Assert.Empty((await stranger.GetFromJsonAsync<List<Suggestion>>("/api/suggestions"))!);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.DeleteAsync($"/api/suggestions/{list[0].Id}")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/suggestions/{list[0].Id}")).StatusCode);
        Assert.Single((await client.GetFromJsonAsync<List<Suggestion>>("/api/suggestions"))!);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/import/text", new { text = "  " })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().PostAsJsonAsync("/api/import/text", new { text = "Netflix" })).StatusCode);
    }

    [Fact]
    public async Task Forwarding_is_hidden_and_closed_until_it_is_configured()
    {
        var client = await SignedUp(factory);
        Assert.Null((await client.GetFromJsonAsync<Me>("/api/me"))!.InboundAddress);
        Assert.Equal(HttpStatusCode.NotFound, (await Deliver(factory.CreateClient(), "u-aaaaaaaaaaaaaaaa@in.subfuse.test", "a@openai.com", "Receipt", "Total $20.00")).StatusCode);
    }

    [Fact]
    public async Task A_forwarded_receipt_reaches_only_the_owner_of_the_address()
    {
        var app = WithForwarding();
        var alice = await SignedUp(app);
        var bob = await SignedUp(app);
        var address = (await alice.GetFromJsonAsync<Me>("/api/me"))!.InboundAddress!;
        Assert.Matches(@"^u-[a-z2-7]{16}@in\.subfuse\.test$", address);
        Assert.NotEqual(address, (await bob.GetFromJsonAsync<Me>("/api/me"))!.InboundAddress);

        var provider = app.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await Deliver(provider, address, "noreply@tm.openai.com", "Receipt", "ChatGPT Plus Total $20.00", secret: null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Deliver(provider, address, "noreply@tm.openai.com", "Receipt", "ChatGPT Plus Total $20.00", secret: "wrong")).StatusCode);
        Assert.Empty((await alice.GetFromJsonAsync<List<Suggestion>>("/api/suggestions"))!);

        Assert.Equal(HttpStatusCode.OK, (await Deliver(provider, $"Alice <{address.ToUpperInvariant()}>", "noreply@tm.openai.com", "Your receipt", "ChatGPT Plus\nTotal $20.00")).StatusCode);
        // Mail for an address nobody owns is accepted and dropped, so addresses cannot be probed.
        Assert.Equal(HttpStatusCode.OK, (await Deliver(provider, "u-aaaaaaaaaaaaaaaa@in.subfuse.test", "noreply@tm.openai.com", "Receipt", "ChatGPT Plus Total $20.00")).StatusCode);

        var one = Assert.Single((await alice.GetFromJsonAsync<List<Suggestion>>("/api/suggestions"))!);
        Assert.Equal(("ChatGPT Plus", 20m, "email"), (one.Name, one.Amount, one.Source));
        Assert.Empty((await bob.GetFromJsonAsync<List<Suggestion>>("/api/suggestions"))!);
    }

    [Fact]
    public async Task Gmail_forwarding_confirmation_is_shown_as_a_notice_with_its_code()
    {
        var app = WithForwarding();
        var client = await SignedUp(app);
        var address = (await client.GetFromJsonAsync<Me>("/api/me"))!.InboundAddress!;
        await Deliver(app.CreateClient(), address, "Gmail Team <forwarding-noreply@google.com>", "(#483920175) Gmail Forwarding Confirmation",
            "someone@gmail.com has requested to automatically forward mail to your email address. Confirmation code: 483920175");
        var notice = Assert.Single((await client.GetFromJsonAsync<List<Suggestion>>("/api/suggestions"))!);
        Assert.Equal(("notice", "483920175"), (notice.Source, notice.Note));
    }
}
