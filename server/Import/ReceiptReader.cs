using System.Globalization;
using System.Text.RegularExpressions;

namespace Subfuse.Server.Import;

/// <summary>A subscription the reader believes it found. The user reviews it before anything is saved as real.</summary>
public record Detected(string Name, decimal? Amount, string Currency, string Cycle, string Category);

/// <summary>
/// Finds subscriptions in free text: a receipt email, or a pasted list such as a phone's subscriptions page.
/// It only recognises services from the catalogue (plus the sender of an email that clearly is a receipt),
/// and only keeps structured fields, never the original text.
/// </summary>
public partial class ReceiptReader(Catalog catalog)
{
    private const int MaxResults = 25;
    /// <summary>How far after a service's name its price is expected to appear.</summary>
    private const int Reach = 300;

    private static readonly Dictionary<string, string> Symbols = new()
    {
        ["R$"] = "BRL", ["C$"] = "CAD", ["A$"] = "AUD", ["$"] = "USD", ["€"] = "EUR", ["£"] = "GBP",
        ["₾"] = "GEL", ["¥"] = "JPY", ["₹"] = "INR", ["₺"] = "TRY",
    };

    // "$20.00", "€ 10,99", "R$ 34,90"  |  "20.00 USD", "90 GEL"  |  "USD 20.00"
    [GeneratedRegex(@"(?<sym>R\$|C\$|A\$|[$€£₾¥₹₺])\s?(?<n>\d[\d.,]*)|(?<n>\d[\d.,]*)\s?(?<code>USD|EUR|GBP|GEL|JPY|INR|BRL|CAD|AUD|TRY)\b|\b(?<code>USD|EUR|GBP|GEL|JPY|INR|BRL|CAD|AUD|TRY)\s?(?<n>\d[\d.,]*)", RegexOptions.IgnoreCase)]
    private static partial Regex MoneyPattern();

    [GeneratedRegex(@"\b(total|amount (paid|charged|due)|you paid|charged|payment of)\b", RegexOptions.IgnoreCase)]
    private static partial Regex TotalPattern();

    [GeneratedRegex(@"\b(receipt|invoice|subscription|renew(al|ed|s)?|billing|payment|charged)\b", RegexOptions.IgnoreCase)]
    private static partial Regex ReceiptWords();

    private record Money(int Position, decimal Amount, string Currency);
    private record Hit(int Position, CatalogService Service);

    /// <param name="fromAddress">The sender, for emails; null for pasted text.</param>
    public List<Detected> Read(string? fromAddress, string? subject, string? body)
    {
        var text = $"{subject}\n{body}";
        if (text.Length > 60_000) text = text[..60_000];
        var lower = text.ToLowerInvariant();
        var fromDomain = DomainOf(fromAddress);
        var money = FindMoney(text);

        var hits = new List<Hit>();
        foreach (var service in catalog.Services)
        {
            var position = FirstMention(lower, service);
            if (position < 0 && fromDomain is not null && service.Senders.Any(s => IsSameOrSubdomain(fromDomain, s))) position = 0;
            if (position >= 0) hits.Add(new Hit(position, service));
        }
        hits.Sort((a, b) => a.Position.CompareTo(b.Position));

        var results = new List<Detected>();
        for (var i = 0; i < hits.Count && results.Count < MaxResults; i++)
        {
            var (start, service) = hits[i];
            var end = Math.Min(i + 1 < hits.Count ? hits[i + 1].Position : int.MaxValue, start + Reach);
            if (end <= start) end = start + 1; // two services found at the same spot (sender match)
            var window = lower[Math.Min(start, lower.Length)..Math.Min(end, lower.Length)];

            // A lone service in an email owns the whole message, so prefer the amount labelled as the total.
            var price = hits.Count == 1
                ? Total(text, money) ?? money.FirstOrDefault()
                : money.FirstOrDefault(m => m.Position >= start && m.Position < end);

            var plan = service.Plans.Where(p => window.Contains(p.Name.ToLowerInvariant())).MaxBy(p => p.Name.Length)
                ?? (price is null ? null : service.Plans.FirstOrDefault(p => p.Amount == price.Amount))
                ?? (service.Plans.Length == 1 ? service.Plans[0] : null);

            results.Add(new Detected(
                plan?.Name ?? service.Service,
                price?.Amount ?? plan?.Amount,
                price?.Currency ?? plan?.Currency ?? "USD",
                CycleIn(hits.Count == 1 ? lower : window) ?? plan?.Cycle ?? "monthly",
                service.Category));
        }

        // An email that reads like a receipt from a sender we do not know: suggest it under the sender's name.
        if (results.Count == 0 && fromDomain is not null && money.Count > 0 && ReceiptWords().IsMatch(text))
        {
            var price = Total(text, money) ?? money[0];
            results.Add(new Detected(NameFromDomain(fromDomain), price.Amount, price.Currency, CycleIn(lower) ?? "monthly", "Other"));
        }
        return results;
    }

    private static int FirstMention(string lower, CatalogService service)
    {
        var best = -1;
        foreach (var term in service.Keywords.Concat(service.Plans.Select(p => p.Name)).Concat(service.Senders))
        {
            var position = IndexOfWord(lower, term.ToLowerInvariant());
            if (position >= 0 && (best < 0 || position < best)) best = position;
        }
        return best;
    }

    /// <summary>Finds <paramref name="term"/> where it is not part of a longer word ("tidal" but not "antidal").</summary>
    private static int IndexOfWord(string lower, string term)
    {
        for (var at = lower.IndexOf(term, StringComparison.Ordinal); at >= 0; at = lower.IndexOf(term, at + 1, StringComparison.Ordinal))
        {
            var before = at == 0 || !char.IsLetterOrDigit(lower[at - 1]);
            var afterIndex = at + term.Length;
            var after = afterIndex >= lower.Length || !char.IsLetterOrDigit(lower[afterIndex]);
            if (before && after) return at;
        }
        return -1;
    }

    private static List<Money> FindMoney(string text)
    {
        var found = new List<Money>();
        foreach (Match m in MoneyPattern().Matches(text))
        {
            var currency = m.Groups["sym"].Success ? Symbols[m.Groups["sym"].Value.ToUpperInvariant()] : m.Groups["code"].Value.ToUpperInvariant();
            if (ParseNumber(m.Groups["n"].Value) is { } amount && amount is >= 0 and <= Rules.MaxAmount)
                found.Add(new Money(m.Index, amount, currency));
        }
        return found;
    }

    /// <summary>Reads "1,299.00", "1.299,00", "19,99" and "20" alike.</summary>
    private static decimal? ParseNumber(string raw)
    {
        raw = raw.TrimEnd('.', ',');
        var lastDot = raw.LastIndexOf('.');
        var lastComma = raw.LastIndexOf(',');
        var decimalAt = Math.Max(lastDot, lastComma);
        // The last separator is a decimal point only when one or two digits follow it.
        if (decimalAt >= 0 && raw.Length - decimalAt - 1 is not (1 or 2)) decimalAt = -1;
        var digits = new string(raw.Where((c, i) => char.IsDigit(c) || i == decimalAt).ToArray()).Replace(',', '.');
        return decimal.TryParse(digits, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value) ? value : null;
    }

    private static Money? Total(string text, List<Money> money)
    {
        var label = TotalPattern().Matches(text).LastOrDefault();
        return label is null ? null : money.FirstOrDefault(m => m.Position >= label.Index && m.Position < label.Index + 120);
    }

    private static string? CycleIn(string lower) =>
        lower.Contains("year") || lower.Contains("annual") ? "yearly"
        : lower.Contains("quarter") || lower.Contains("3 months") ? "quarterly"
        : lower.Contains("week") ? "weekly"
        : lower.Contains("month") ? "monthly"
        : null;

    private static string? DomainOf(string? address)
    {
        if (string.IsNullOrWhiteSpace(address)) return null;
        var at = address.LastIndexOf('@');
        if (at < 0) return null;
        var domain = new string(address[(at + 1)..].TakeWhile(c => char.IsLetterOrDigit(c) || c is '.' or '-').ToArray());
        return domain.Contains('.') ? domain.ToLowerInvariant() : null;
    }

    private static bool IsSameOrSubdomain(string domain, string parent) =>
        domain == parent || domain.EndsWith("." + parent, StringComparison.Ordinal);

    /// <summary>"mail.acme-tools.com" becomes "Acme-tools".</summary>
    private static string NameFromDomain(string domain)
    {
        var parts = domain.Split('.');
        var label = parts.Length >= 2 ? parts[^2] : parts[0];
        if (label.Length > 40) label = label[..40];
        return char.ToUpperInvariant(label[0]) + label[1..];
    }
}
