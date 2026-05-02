using System.Text.Json.Serialization;

namespace Arbor.DevPackages.Server.Search;

internal sealed record SearchQueryResponse(
    [property: JsonPropertyName("totalHits")] int TotalHits,
    [property: JsonPropertyName("data")] IReadOnlyList<SearchResultPackage> Data);

public sealed record SearchResultPackage(
    [property: JsonPropertyName("@id")] string? AtId,
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("summary")] string? Summary,
    [property: JsonPropertyName("title")] string? Title,
    [property: JsonPropertyName("iconUrl")] string? IconUrl,
    [property: JsonPropertyName("licenseUrl")] string? LicenseUrl,
    [property: JsonPropertyName("projectUrl")] string? ProjectUrl,
    [property: JsonPropertyName("tags")] string? Tags,
    [property: JsonPropertyName("authors")] string? Authors,
    [property: JsonPropertyName("totalDownloads")] long TotalDownloads,
    [property: JsonPropertyName("verified")] bool Verified,
    [property: JsonPropertyName("versions")] IReadOnlyList<SearchVersionEntry>? Versions);

public sealed record SearchVersionEntry(
    [property: JsonPropertyName("@id")] string? AtId,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("downloads")] long Downloads);
