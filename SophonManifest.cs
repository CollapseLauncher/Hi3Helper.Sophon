using Google.Protobuf;
using Hi3Helper.Sophon.Infos;
using Hi3Helper.Sophon.Protos;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

using ZstdStream = ZstdNet.DecompressionStream;

namespace Hi3Helper.Sophon
{
    public partial class SophonManifest
    {
        /// <summary>
        ///     Enumerate the Sophon assets contained within the manifest.
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
        ///     An <seealso cref="IAsyncEnumerable{T}"/> to enumerate the Sophon asset from the manifest.
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
        public static async IAsyncEnumerable<SophonAsset> EnumerateAsync(HttpClient httpClient, SophonManifestInfo manifestInfo, SophonChunksInfo chunksInfo,
            [EnumeratorCancellation] CancellationToken token = default)
        {
            if (!Extern.IsLibraryExist(ZstdNet.ExternMethods.DllName))
                throw new DllNotFoundException($"libzstd is not found!");

            using HttpRequestMessage? httpRequestMessage = new HttpRequestMessage(HttpMethod.Get, manifestInfo.ManifestFileUrl);
            using HttpResponseMessage? httpResponseMessage = await httpClient.SendAsync(httpRequestMessage, HttpCompletionOption.ResponseHeadersRead, token);

            if (!(httpResponseMessage?.IsSuccessStatusCode ?? false))
                throw new HttpRequestException($"Http request to the manifest file returns a non-successful status!", null, httpResponseMessage?.StatusCode);

            if (httpResponseMessage == null)
                throw new NullReferenceException($"Http response message returns a null entry");

            await using Stream manifestNetworkStream = await GetSophonHttpStream(httpResponseMessage, manifestInfo.IsUseCompression, token);
            SophonManifestProto manifestProto = SophonManifestProto.Parser.ParseFrom(manifestNetworkStream);

            foreach (AssetProperty asset in manifestProto.Assets)
            {
                string assetName = asset.AssetName;
                int assetType = asset.AssetType;
                if (assetType != 0 || string.IsNullOrEmpty(asset.AssetHashMd5))
                {
                    yield return new SophonAsset
                    {
                        AssetName = assetName,
                        IsDirectory = true
                    };
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

                yield return new SophonAsset
                {
                    AssetName = assetName,
                    AssetHash = assetHash,
                    AssetSize = assetSize,
                    Chunks = assetChunks,
                    SophonChunksInfo = chunksInfo,
                    IsDirectory = false
                };
            }
        }

        private static async ValueTask<Stream> GetSophonHttpStream(HttpResponseMessage responseMessage, bool isCompressed, CancellationToken token)
        {
            using Stream networkStream = await responseMessage.Content.ReadAsStreamAsync(token);
            Stream source = networkStream;
            if (isCompressed)
                source = new ZstdStream(networkStream);

            using (source)
            {
                MemoryStream tempStream = new MemoryStream();
                await source.CopyToAsync(tempStream, token);
                tempStream.Position = 0;
                return tempStream;
            }
        }
    }
}
