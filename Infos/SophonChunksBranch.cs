using Hi3Helper.Sophon.Infos;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Hi3Helper.Sophon
{
    public struct SophonChunkManifestInfoPair
    {
        public SophonChunksInfo ChunksInfo;
        public SophonManifestInfo ManifestInfo;
    }

    public partial class SophonManifest
    {
        public static async ValueTask<SophonBranch?> GetSophonBranchInfo(HttpClient client, string url, CancellationToken token)
            => await client.GetFromJsonAsync(url, SophonContext.Default.SophonBranch, token);

        public static async ValueTask<SophonChunkManifestInfoPair> CreateSophonChunkManifestInfoPair(HttpClient client, string url, string? matchingField, CancellationToken token)
        {
            SophonBranch? sophonBranch = await GetSophonBranchInfo(client, url, token);
            if (!(sophonBranch != null && sophonBranch.Data != null))
                throw new NullReferenceException("Url returns an empty/null data!");

            if (string.IsNullOrEmpty(matchingField))
                matchingField = "game";

            SophonManifestIdentity? sophonManifestIdentity = sophonBranch.Data.ManifestIdentityList?.FirstOrDefault(x => x.MatchingField == matchingField);

            if (sophonManifestIdentity == null)
                throw new KeyNotFoundException($"Sophon manifest with matching field: {matchingField} is not found!");

            return new SophonChunkManifestInfoPair
            {
                ChunksInfo = new SophonChunksInfo
                {
                    ChunksBaseUrl = sophonManifestIdentity.ChunksUrlInfo!.UrlPrefix!,
                    ChunksCount = sophonManifestIdentity.ChunkInfo!.ChunkCount,
                    FilesCount = sophonManifestIdentity.ChunkInfo!.FileCount,
                    IsUseCompression = sophonManifestIdentity.ChunksUrlInfo!.IsCompressed,
                    TotalSize = sophonManifestIdentity.ChunkInfo!.UncompressedSize,
                    TotalCompressedSize = sophonManifestIdentity.ChunkInfo!.CompressedSize,
                },
                ManifestInfo = new SophonManifestInfo
                {
                    ManifestBaseUrl = sophonManifestIdentity.ManifestUrlInfo!.UrlPrefix!,
                    ManifestChecksumMd5 = sophonManifestIdentity.ManifestFileInfo!.Checksum!,
                    ManifestId = sophonManifestIdentity.ManifestFileInfo!.FileName!,
                    ManifestSize = sophonManifestIdentity.ManifestFileInfo!.UncompressedSize,
                    ManifestCompressedSize = sophonManifestIdentity.ManifestFileInfo!.CompressedSize,
                    IsUseCompression = sophonManifestIdentity.ManifestUrlInfo!.IsCompressed
                }
            };
        }
    }
}

namespace Hi3Helper.Sophon.Infos
{
    [JsonSerializable(typeof(SophonBranch))]
    public partial class SophonContext : JsonSerializerContext { }

    public class SophonBranch
    {
        [JsonPropertyName("retcode")]
        public int ReturnCode { get; init; }

        [JsonPropertyName("message")]
        public string? ReturnMessage { get; init; }

        [JsonPropertyName("data")]
        public SophonData? Data { get; init; }
    }

    public class SophonData
    {
        [JsonPropertyName("build_id")]
        public string? BuildId { get; init; }

        [JsonPropertyName("tag")]
        public string? TagName { get; init; }

        [JsonPropertyName("manifests")]
        public List<SophonManifestIdentity>? ManifestIdentityList { get; init; }
    }

    public class SophonManifestIdentity
    {
        [JsonPropertyName("category_id")]
        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
        public int? CategoryId { get; init; }

        [JsonPropertyName("category_name")]
        public string? CategoryName { get; init; }

        [JsonPropertyName("matching_field")]
        public string? MatchingField { get; init; }

        [JsonPropertyName("manifest")]
        public SophonManifestFileInfo? ManifestFileInfo { get; init; }

        [JsonPropertyName("manifest_download")]
        public SophonManifestUrlInfo? ManifestUrlInfo { get; init; }

        [JsonPropertyName("chunk_download")]
        public SophonManifestUrlInfo? ChunksUrlInfo { get; init; }

        [JsonPropertyName("stats")]
        public SophonManifestChunkInfo? ChunkInfo { get; init; }

        [JsonPropertyName("deduplicated_stats")]
        public SophonManifestChunkInfo? DeduplicatedChunkInfo { get; init; }
    }

    public class SophonManifestFileInfo
    {
        [JsonPropertyName("id")]
        public string? FileName { get; init; }

        [JsonPropertyName("checksum")]
        public string? Checksum { get; init; }

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

        [JsonPropertyName("password")]
        public string? EncryptionPassword { get; init; }
        
        [JsonConverter(typeof(BoolConverter))]
        [JsonPropertyName("compression")]
        public bool IsCompressed { get; init; }

        [JsonPropertyName("url_prefix")]
        public string? UrlPrefix { get; init; }

        [JsonPropertyName("url_suffix")]
        public string? UrlSuffix { get; init; }
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

    // Credit (by dbc from Stack Overflow):
    // https://stackoverflow.com/a/68685773/13362680
    public class BoolConverter : JsonConverter<bool>
    {
        public override void Write(Utf8JsonWriter writer, bool value, JsonSerializerOptions options) =>
            writer.WriteBooleanValue(value);

        public override bool Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            reader.TokenType switch
            {
                JsonTokenType.True => true,
                JsonTokenType.False => false,
                JsonTokenType.String => bool.TryParse(reader.GetString(), out bool boolFromString) ? boolFromString : throw new JsonException(),
                JsonTokenType.Number => reader.TryGetInt64(out long boolFromNumber) ? Convert.ToBoolean(boolFromNumber) : reader.TryGetDouble(out double boolFromDouble) ? Convert.ToBoolean(boolFromDouble) : false,
                _ => throw new JsonException(),
            };
    }
}
