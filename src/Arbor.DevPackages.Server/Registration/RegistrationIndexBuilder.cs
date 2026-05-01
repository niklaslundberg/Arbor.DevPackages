using System.Xml.Linq;
using System.Text.Json.Serialization;
using Arbor.DevPackages.Core.Packages;

namespace Arbor.DevPackages.Server.Registration;

/// <summary>
/// Pure builder that converts <see cref="PackageMetadata"/> objects into NuGet
/// Registration v3.6.0 JSON responses. All methods are side-effect-free and
/// independently unit-testable.
/// </summary>
public static class RegistrationIndexBuilder
{
    /// <summary>
    /// Builds the registration index for a given package ID from a list of its versions.
    /// All versions are inlined in a single page (no external page references).
    /// </summary>
    public static RegistrationIndexResponse BuildIndex(
        string baseUrl,
        string id,
        IReadOnlyList<PackageMetadata> packages)
    {
        var indexUrl = IndexUrl(baseUrl, id);

        var leafItems = packages
            .Select(p => BuildLeafItem(baseUrl, p))
            .ToArray();

        var lower = leafItems.Length > 0 ? leafItems[0].CatalogEntry.Version : string.Empty;
        var upper = leafItems.Length > 0 ? leafItems[^1].CatalogEntry.Version : string.Empty;

        var page = new RegistrationPage(
            Id: $"{indexUrl}#page/{lower}/{upper}",
            Count: leafItems.Length,
            Items: leafItems,
            Lower: lower,
            Upper: upper);

        return new RegistrationIndexResponse(
            Id: indexUrl,
            Count: 1,
            Items: [page]);
    }

    /// <summary>
    /// Builds the registration leaf for a single package version.
    /// </summary>
    public static RegistrationLeafResponse BuildLeaf(string baseUrl, PackageMetadata metadata)
    {
        var item = BuildLeafItem(baseUrl, metadata);
        return new RegistrationLeafResponse(
            Id: item.Id,
            CatalogEntry: item.CatalogEntry,
            PackageContent: item.PackageContent,
            Registration: IndexUrl(baseUrl, metadata.Identity.Id));
    }

    private static RegistrationLeafItem BuildLeafItem(string baseUrl, PackageMetadata metadata)
    {
        var id = metadata.Identity.Id;
        var version = metadata.Identity.Version;
        var leafUrl = LeafUrl(baseUrl, id, version);
        var packageContent = $"{baseUrl}/v3/flatcontainer/{id}/{version}/{id}.{version}.nupkg";
        var indexUrl = IndexUrl(baseUrl, id);

        var (authors, description) = ParseNuspec(metadata.NuspecContent);

        var catalogEntry = new CatalogEntry(
            Id: leafUrl,
            PackageId: metadata.Identity.Id,
            Version: version,
            Authors: authors,
            Description: description,
            Listed: true,
            Published: "2000-01-01T00:00:00+00:00");

        return new RegistrationLeafItem(
            Id: leafUrl,
            CatalogEntry: catalogEntry,
            PackageContent: packageContent,
            Registration: indexUrl);
    }

    private static (string Authors, string Description) ParseNuspec(string nuspecContent)
    {
        var doc = XDocument.Parse(nuspecContent);
        XNamespace ns = doc.Root?.Name.Namespace ?? XNamespace.None;
        var metadata = doc.Root?.Element(ns + "metadata") ?? doc.Root;

        var authors = metadata?.Element(ns + "authors")?.Value ?? string.Empty;
        var description = metadata?.Element(ns + "description")?.Value ?? string.Empty;

        return (authors, description);
    }

    private static string IndexUrl(string baseUrl, string id) =>
        $"{baseUrl}/v3/registration/{id}/index.json";

    private static string LeafUrl(string baseUrl, string id, string version) =>
        $"{baseUrl}/v3/registration/{id}/{version}.json";
}

// ─── Response models ──────────────────────────────────────────────────────────

public sealed record RegistrationIndexResponse(
    [property: JsonPropertyName("@id")] string Id,
    [property: JsonPropertyName("count")] int Count,
    [property: JsonPropertyName("items")] IReadOnlyList<RegistrationPage> Items)
{
    [JsonPropertyName("@type")]
    public IReadOnlyList<string> Type { get; } = ["catalog:CatalogRoot", "PackageRegistration", "catalog:Permalink"];
}

public sealed record RegistrationPage(
    [property: JsonPropertyName("@id")] string Id,
    [property: JsonPropertyName("count")] int Count,
    [property: JsonPropertyName("items")] IReadOnlyList<RegistrationLeafItem> Items,
    [property: JsonPropertyName("lower")] string Lower,
    [property: JsonPropertyName("upper")] string Upper)
{
    [JsonPropertyName("@type")]
    public string Type { get; } = "catalog:CatalogPage";
}

public sealed record RegistrationLeafItem(
    [property: JsonPropertyName("@id")] string Id,
    [property: JsonPropertyName("catalogEntry")] CatalogEntry CatalogEntry,
    [property: JsonPropertyName("packageContent")] string PackageContent,
    [property: JsonPropertyName("registration")] string Registration)
{
    [JsonPropertyName("@type")]
    public string Type { get; } = "Package";
}

public sealed record RegistrationLeafResponse(
    [property: JsonPropertyName("@id")] string Id,
    [property: JsonPropertyName("catalogEntry")] CatalogEntry CatalogEntry,
    [property: JsonPropertyName("packageContent")] string PackageContent,
    [property: JsonPropertyName("registration")] string Registration)
{
    [JsonPropertyName("@type")]
    public IReadOnlyList<string> Type { get; } = ["Package", "catalog:Permalink"];

    [JsonPropertyName("listed")]
    public bool Listed => CatalogEntry.Listed;

    [JsonPropertyName("published")]
    public string Published => CatalogEntry.Published;
}

public sealed record CatalogEntry(
    [property: JsonPropertyName("@id")] string Id,
    [property: JsonPropertyName("id")] string PackageId,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("authors")] string Authors,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("listed")] bool Listed,
    [property: JsonPropertyName("published")] string Published)
{
    [JsonPropertyName("@type")]
    public string Type { get; } = "PackageCatalogEntry";
}
