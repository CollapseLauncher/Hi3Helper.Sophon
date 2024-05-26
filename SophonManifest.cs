// ReSharper disable IdentifierTypo
// ReSharper disable StringLiteralTypo
// ReSharper disable CommentTypo
// ReSharper disable InvalidXmlDocComment

using Hi3Helper.Sophon.Helper;
using Hi3Helper.Sophon.Infos;
using Hi3Helper.Sophon.Protos;
using Hi3Helper.Sophon.Structs;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using ZstdNet;
using ZstdStream = ZstdNet.DecompressionStream;

namespace Hi3Helper.Sophon
{
    public static partial class SophonManifest
    {
        /// <summary>
        ///     Enumerate/Get the list of Sophon assets contained within the manifest.
        /// </summary>
        /// <param name="httpClient">
        ///     The <seealso cref="HttpClient"/> to be used to download the manifest data.
        /// </param>
        /// <param name="infoPair">
        ///     Pair of Manifest and Chunks information struct.
        /// </param>
        /// <param name="token">
        ///     Cancellation token for handling cancellation while the routine is running.
        /// </param>
        /// <returns>
        ///     An enumeration to enumerate the Sophon asset from the manifest.
        /// </returns>
        /// <exception cref="DllNotFoundException">
        ///     Indicates if a library required is missing.
        /// </exception>
        /// <exception cref="HttpRequestException">
        ///     Indicates if an error during Http request is happening.
        /// </exception>
        /// <exception cref="NullReferenceException">
        ///     Indicates if an argument or Http response returns a <c>null</c>.
        /// </exception>
        public static async
        #if NET6_0_OR_GREATER
            IAsyncEnumerable<SophonAsset>
            EnumerateAsync
        #else
            Task<List<SophonAsset>>
            GetAssetListAsync
        #endif
            (HttpClient httpClient, SophonChunkManifestInfoPair infoPair,
         #if NET6_0_OR_GREATER
            [EnumeratorCancellation]
         #endif
             CancellationToken token = default)

        {
        #if NET6_0_OR_GREATER
            await foreach (SophonAsset asset in EnumerateAsync(httpClient, infoPair.ManifestInfo, infoPair.ChunksInfo, token))
            {
                yield return asset;
            }
        #else
            return await GetAssetListAsync(httpClient, infoPair.ManifestInfo, infoPair.ChunksInfo, token);
        #endif
        }

        /// <summary>
        ///     Enumerate/Get the list of Sophon assets contained within the manifest.
        /// </summary>
        /// <param name="httpClient">
        ///     The <seealso cref="HttpClient"/> to be used to download the manifest data.
        /// </param>
        /// <param name="manifestInfo">
        ///     Manifest information struct.
        /// </param>
        /// <param name="chunksInfo">
        ///     Chunks information struct.
        /// </param>
        /// <param name="token">
        ///     Cancellation token for handling cancellation while the routine is running.
        /// </param>
        /// <returns>
        ///     An enumeration to get the Sophon asset list from the manifest.
        /// </returns>
        /// <exception cref="DllNotFoundException">
        ///     Indicates if a library required is missing.
        /// </exception>
        /// <exception cref="HttpRequestException">
        ///     Indicates if an error during Http request is happening.
        /// </exception>
        /// <exception cref="NullReferenceException">
        ///     Indicates if an argument or Http response returns a <c>null</c>.
        /// </exception>
        public static async
        #if NET6_0_OR_GREATER
            IAsyncEnumerable<SophonAsset>
            EnumerateAsync
        #else
            Task<List<SophonAsset>>
            GetAssetListAsync
        #endif
            (HttpClient         httpClient,
             SophonManifestInfo manifestInfo,
             SophonChunksInfo   chunksInfo,
         #if NET6_0_OR_GREATER
             [EnumeratorCancellation]
         #endif
             CancellationToken token = default)
        {
#if NET6_0_OR_GREATER
            if (!DllUtils.IsLibraryExist(DllUtils.DllName))
                throw new DllNotFoundException($"libzstd is not found!");
#else
            List<SophonAsset> assetList = new List<SophonAsset>();
#endif

            ActionTimeoutValueTaskCallback<SophonManifestProto> manifestProtoTaskCallback = new ActionTimeoutValueTaskCallback<SophonManifestProto>(
                async (innerToken) =>
                {
#if NET6_0_OR_GREATER
                    await
#endif
                        using (Stream manifestProtoStream = await SophonAssetStream.CreateStreamAsync(httpClient, manifestInfo.ManifestFileUrl, 0, null, innerToken))
                    using (Stream decompressedProtoStream = manifestInfo.IsUseCompression ? new ZstdStream(manifestProtoStream) : manifestProtoStream)
                    {
                        return SophonManifestProto.Parser.ParseFrom(decompressedProtoStream);
                    }
                });

            SophonManifestProto manifestProto = await Helper.TaskExtensions
                .WaitForRetryAsync(() => manifestProtoTaskCallback, Helper.TaskExtensions.DefaultTimeoutSec, null, null, null, token);

            foreach (AssetProperty asset in manifestProto.Assets)
            {
                SophonAsset assetAdd;
                string assetName = asset.AssetName;
                int assetType = asset.AssetType;
                if (assetType != 0 || string.IsNullOrEmpty(asset.AssetHashMd5))
                {
                    assetAdd = new SophonAsset
                    {
                        AssetName = assetName,
                        IsDirectory = true
                    };
#if NET6_0_OR_GREATER
                    yield return assetAdd;
#else
                    assetList.Add(assetAdd);
#endif
                    continue;
                }

                string assetHash = asset.AssetHashMd5;
                long assetSize = asset.AssetSize;
                SophonChunk[] assetChunks = asset.AssetChunks.Select(x => new SophonChunk
                {
                    ChunkName = x.ChunkName,
                    ChunkHashDecompressed = x.ChunkDecompressedHashMd5,
                    ChunkOffset = x.ChunkOnFileOffset,
                    ChunkSize = x.ChunkSize,
                    ChunkSizeDecompressed = x.ChunkSizeDecompressed
                }).ToArray();

                assetAdd = new SophonAsset
                {
                    AssetName = assetName,
                    AssetHash = assetHash,
                    AssetSize = assetSize,
                    Chunks = assetChunks,
                    SophonChunksInfo = chunksInfo,
                    IsDirectory = false
                };
#if NET6_0_OR_GREATER
                yield return assetAdd;
#else
                assetList.Add(assetAdd);
#endif
            }

#if !NET6_0_OR_GREATER
            return assetList;
#endif
        }

        private static async
#if NET6_0_OR_GREATER
            ValueTask<Stream>
#else
            Task<Stream>
#endif
            GetSophonHttpStream(HttpResponseMessage responseMessage, bool isCompressed, CancellationToken token)
        {
#if NET6_0_OR_GREATER
            // ReSharper disable once ConvertToUsingDeclaration
            await using(Stream networkStream = await responseMessage.Content.ReadAsStreamAsync(token))
#else
            using (Stream networkStream = await responseMessage.Content.ReadAsStreamAsync())
#endif
            {
                Stream source = networkStream;
                if (isCompressed)
                {
                    source = new ZstdStream(networkStream);
                }

#if NET6_0_OR_GREATER
                await
#endif
                using (source)
                {
                    MemoryStream tempStream = new MemoryStream();
                    await source.CopyToAsync(tempStream, 16 << 10, token);
                    tempStream.Position = 0;
                    return tempStream;
                }
            }
        }
    }
}