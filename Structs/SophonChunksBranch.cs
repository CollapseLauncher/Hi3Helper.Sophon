using Hi3Helper.Sophon.Infos;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Hi3Helper.Sophon
{
    public static partial class SophonManifest
    {
        public static async ValueTask<SophonBranch?> GetSophonBranchInfo(
            HttpClient client, string url, CancellationToken token)
        {
            return await client.GetFromJsonAsync(url, SophonContext.Default.SophonBranch, token);
        }

        public static async ValueTask<SophonChunkManifestInfoPair> CreateSophonChunkManifestInfoPair(
            HttpClient client, string url, string? matchingField, CancellationToken token)
        {
            var sophonBranch = await GetSophonBranchInfo(client, url, token);
            if (!(sophonBranch != null && sophonBranch.Data != null))
                throw new NullReferenceException("Url returns an empty/null data!");

            if (string.IsNullOrEmpty(matchingField))
                matchingField = "game";

            var sophonManifestIdentity =
                sophonBranch.Data.ManifestIdentityList?.FirstOrDefault(x => x.MatchingField == matchingField);

            if (sophonManifestIdentity == null)
                throw new KeyNotFoundException($"Sophon manifest with matching field: {matchingField} is not found!");

            return new SophonChunkManifestInfoPair
            {
                ChunksInfo = new SophonChunksInfo
                {
                    ChunksBaseUrl       = sophonManifestIdentity.ChunksUrlInfo!.UrlPrefix!,
                    ChunksCount         = sophonManifestIdentity.ChunkInfo!.ChunkCount,
                    FilesCount          = sophonManifestIdentity.ChunkInfo!.FileCount,
                    IsUseCompression    = sophonManifestIdentity.ChunksUrlInfo!.IsCompressed,
                    TotalSize           = sophonManifestIdentity.ChunkInfo!.UncompressedSize,
                    TotalCompressedSize = sophonManifestIdentity.ChunkInfo!.CompressedSize
                },
                ManifestInfo = new SophonManifestInfo
                {
                    ManifestBaseUrl        = sophonManifestIdentity.ManifestUrlInfo!.UrlPrefix!,
                    ManifestChecksumMd5    = sophonManifestIdentity.ManifestFileInfo!.Checksum!,
                    ManifestId             = sophonManifestIdentity.ManifestFileInfo!.FileName!,
                    ManifestSize           = sophonManifestIdentity.ManifestFileInfo!.UncompressedSize,
                    ManifestCompressedSize = sophonManifestIdentity.ManifestFileInfo!.CompressedSize,
                    IsUseCompression       = sophonManifestIdentity.ManifestUrlInfo!.IsCompressed
                },
                OtherSophonData = sophonBranch.Data
            };
        }
    }
}