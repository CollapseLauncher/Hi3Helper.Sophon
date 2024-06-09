using Hi3Helper.Sophon.Helper;
using System.Collections.Generic;
using System.Text.Json.Serialization;

// ReSharper disable IdentifierTypo
// ReSharper disable StringLiteralTypo
// ReSharper disable CommentTypo
namespace Hi3Helper.Sophon.Structs
{
#if NET6_0_OR_GREATER
    [JsonSerializable(typeof(SophonBranch))]
    public partial class SophonContext : JsonSerializerContext
    {
    }
#endif

    public class SophonBranch
    {
        [JsonPropertyName("retcode")] public int        ReturnCode    { get; set; }
        [JsonPropertyName("message")] public string     ReturnMessage { get; set; }
        [JsonPropertyName("data")]    public SophonData Data          { get; set; }
    }

    public class SophonData
    {
        [JsonPropertyName("build_id")]  public string                       BuildId              { get; set; }
        [JsonPropertyName("tag")]       public string                       TagName              { get; set; }
        [JsonPropertyName("manifests")] public List<SophonManifestIdentity> ManifestIdentityList { get; set; }
    }

    public class SophonManifestIdentity
    {
        [JsonPropertyName("category_id")]
        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
        public int CategoryId { get; set; }

        [JsonPropertyName("category_name")]  public string                 CategoryName     { get; set; }
        [JsonPropertyName("matching_field")] public string                 MatchingField    { get; set; }
        [JsonPropertyName("manifest")]       public SophonManifestFileInfo ManifestFileInfo { get; set; }

        [JsonPropertyName("manifest_download")]
        public SophonManifestUrlInfo ManifestUrlInfo { get; set; }

        [JsonPropertyName("chunk_download")] public SophonManifestUrlInfo   ChunksUrlInfo { get; set; }
        [JsonPropertyName("stats")]          public SophonManifestChunkInfo ChunkInfo     { get; set; }

        [JsonPropertyName("deduplicated_stats")]
        public SophonManifestChunkInfo DeduplicatedChunkInfo { get; set; }
    }

    public class SophonManifestFileInfo
    {
        [JsonPropertyName("id")]       public string FileName { get; set; }
        [JsonPropertyName("checksum")] public string Checksum { get; set; }

        [JsonPropertyName("compressed_size")]
        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
        public long CompressedSize { get; set; }

        [JsonPropertyName("uncompressed_size")]
        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
        public long UncompressedSize { get; set; }
    }

    public class SophonManifestUrlInfo
    {
        [JsonConverter(typeof(BoolConverter))]
        [JsonPropertyName("encryption")]
        public bool IsEncrypted { get; set; }

        [JsonPropertyName("password")] public string EncryptionPassword { get; set; }

        [JsonConverter(typeof(BoolConverter))]
        [JsonPropertyName("compression")]
        public bool IsCompressed { get; set; }

        [JsonPropertyName("url_prefix")] public string UrlPrefix { get; set; }
        [JsonPropertyName("url_suffix")] public string UrlSuffix { get; set; }
    }

    public class SophonManifestChunkInfo
    {
        [JsonPropertyName("compressed_size")]
        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
        public long CompressedSize { get; set; }

        [JsonPropertyName("uncompressed_size")]
        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
        public long UncompressedSize { get; set; }

        [JsonPropertyName("file_count")]
        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
        public int FileCount { get; set; }

        [JsonPropertyName("chunk_count")]
        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
        public int ChunkCount { get; set; }
    }
}