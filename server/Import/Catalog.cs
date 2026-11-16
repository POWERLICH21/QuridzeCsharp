using System.Text.Json;

namespace Subfuse.Server.Import;

public record CatalogPlan(string Name, decimal? Amount, string Currency, string Cycle);

public record CatalogService(string Service, string Category, string[] Senders, string[] Keywords, CatalogPlan[] Plans);

/// <summary>
/// Known subscription services. The single source is wwwroot/catalog.json, which the pages also load
/// to suggest names and typical prices while typing.
/// </summary>
public class Catalog(IReadOnlyList<CatalogService> services)
{
    public IReadOnlyList<CatalogService> Services { get; } = services;

    public static Catalog Load(string path)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var services = JsonSerializer.Deserialize<List<CatalogService>>(File.ReadAllText(path), options)
            ?? throw new InvalidOperationException("catalog.json is empty.");
        return new Catalog(services);
    }
}
