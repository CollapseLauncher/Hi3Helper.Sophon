using Hi3Helper.Sophon.Infos;
using Hi3Helper.Sophon.Structs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;

// ReSharper disable IdentifierTypo
// ReSharper disable StringLiteralTypo
// ReSharper disable CheckNamespace
namespace Hi3Helper.Sophon
{
    public static partial class SophonManifest
    {
        public static async
            Task<SophonBranch>
            GetSophonBranchInfo(HttpClient        client,
                                string            url,
                                CancellationToken token)
        {
        #if NET6_0_OR_GREATER
            return await client.GetFromJsonAsync(url, SophonContext.Default.SophonBranch, token);
        #elif NET45
            JsonSerializer jsonSerializer = new JsonSerializer();
            using (HttpRequestMessage requestMessage = new HttpRequestMessage(HttpMethod.Get, url))
            using (HttpResponseMessage responseMessage =
                await client.SendAsync(requestMessage, HttpCompletionOption.ResponseHeadersRead, token))
            using (Stream responseStream = await responseMessage.Content.ReadAsStreamAsync())
            using (StreamReader streamReader = new StreamReader(responseStream))
            using (JsonTextReader jsonTextReader = new JsonTextReader(streamReader))
            {
                return jsonSerializer.Deserialize<SophonBranch>(jsonTextReader);
            }
        #else
            return await client.GetFromJsonAsync<SophonBranch>(url, token);
        #endif
        }

        public static async
            Task<SophonChunkManifestInfoPair>
            CreateSophonChunkManifestInfoPair(HttpClient        client,
                                              string            url,
                                              string            matchingField,
                                              CancellationToken token)
        {
            var sophonBranch = await GetSophonBranchInfo(client, url, token);
            if (sophonBranch.Data == null)
            {
                return new SophonChunkManifestInfoPair
                {
                    IsFound       = false,
                    ReturnCode    = sophonBranch.ReturnCode,
                    ReturnMessage = sophonBranch.ReturnMessage
                };
            }

            if (string.IsNullOrEmpty(matchingField))
            {
                matchingField = "game";
            }

            var sophonManifestIdentity =
                sophonBranch.Data.ManifestIdentityList?.FirstOrDefault(x => x.MatchingField == matchingField);

            if (sophonManifestIdentity == null)
            {
                throw new KeyNotFoundException($"Sophon manifest with matching field: {matchingField} is not found!");
            }

            return new SophonChunkManifestInfoPair
            {
                ChunksInfo = new SophonChunksInfo
                {
                    ChunksBaseUrl       = sophonManifestIdentity.ChunksUrlInfo.UrlPrefix,
                    ChunksCount         = sophonManifestIdentity.ChunkInfo.ChunkCount,
                    FilesCount          = sophonManifestIdentity.ChunkInfo.FileCount,
                    IsUseCompression    = sophonManifestIdentity.ChunksUrlInfo.IsCompressed,
                    TotalSize           = sophonManifestIdentity.ChunkInfo.UncompressedSize,
                    TotalCompressedSize = sophonManifestIdentity.ChunkInfo.CompressedSize
                },
                ManifestInfo = new SophonManifestInfo
                {
                    ManifestBaseUrl        = sophonManifestIdentity.ManifestUrlInfo.UrlPrefix,
                    ManifestChecksumMd5    = sophonManifestIdentity.ManifestFileInfo.Checksum,
                    ManifestId             = sophonManifestIdentity.ManifestFileInfo.FileName,
                    ManifestSize           = sophonManifestIdentity.ManifestFileInfo.UncompressedSize,
                    ManifestCompressedSize = sophonManifestIdentity.ManifestFileInfo.CompressedSize,
                    IsUseCompression       = sophonManifestIdentity.ManifestUrlInfo.IsCompressed
                },
                OtherSophonData = sophonBranch.Data
            };
        }
    }
}