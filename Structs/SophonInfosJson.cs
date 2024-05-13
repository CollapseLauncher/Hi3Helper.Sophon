using Hi3Helper.Sophon.Helper;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Hi3Helper.Sophon.Infos;

[JsonSerializable(typeof(SophonBranch))]
public partial class SophonContext : JsonSerializerContext;

public class SophonBranch
{
    [JsonPropertyName("retcode")] public int ReturnCode { get; init; }

    [JsonPropertyName("message")] public string? ReturnMessage { get; init; }

    [JsonPropertyName("data")] public SophonData? Data { get; init; }
}

public class SophonData
{
    [JsonPropertyName("build_id")] public string? BuildId { get; init; }

    [JsonPropertyName("tag")] public string? TagName { get; init; }

    [JsonPropertyName("manifests")] public List<SophonManifestIdentity>? ManifestIdentityList { get; init; }
}

public class SophonManifestIdentity
{
    [JsonPropertyName("category_id")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public int? CategoryId { get; init; }

    [JsonPropertyName("category_name")] public string? CategoryName { get; init; }

    [JsonPropertyName("matching_field")] public string? MatchingField { get; init; }

    [JsonPropertyName("manifest")] public SophonManifestFileInfo? ManifestFileInfo { get; init; }

    [JsonPropertyName("manifest_download")]
    public SophonManifestUrlInfo? ManifestUrlInfo { get; init; }

    [JsonPropertyName("chunk_download")] public SophonManifestUrlInfo? ChunksUrlInfo { get; init; }

    [JsonPropertyName("stats")] public SophonManifestChunkInfo? ChunkInfo { get; init; }

    [JsonPropertyName("deduplicated_stats")]
    public SophonManifestChunkInfo? DeduplicatedChunkInfo { get; init; }
}

public class SophonManifestFileInfo
{
    [JsonPropertyName("id")] public string? FileName { get; init; }

    [JsonPropertyName("checksum")] public string? Checksum { get; init; }

    [JsonPropertyName("compressed_size")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public long CompressedSize { get; init; }

    [JsonPropertyName("uncompressed_size")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public long UncompressedSize { get; init; }
}

public class SophonManifestUrlInfo
{
    [JsonConverter(typeof(BoolConverter))]
    [JsonPropertyName("encryption")]
    public bool IsEncrypted { get; init; }

    [JsonPropertyName("password")] public string? EncryptionPassword { get; init; }

    [JsonConverter(typeof(BoolConverter))]
    [JsonPropertyName("compression")]
    public bool IsCompressed { get; init; }

    [JsonPropertyName("url_prefix")] public string? UrlPrefix { get; init; }

    [JsonPropertyName("url_suffix")] public string? UrlSuffix { get; init; }
}

public class SophonManifestChunkInfo
{
    [JsonPropertyName("compressed_size")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public long CompressedSize { get; init; }

    [JsonPropertyName("uncompressed_size")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public long UncompressedSize { get; init; }

    [JsonPropertyName("file_count")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public int FileCount { get; init; }

    [JsonPropertyName("chunk_count")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public int ChunkCount { get; init; }
}