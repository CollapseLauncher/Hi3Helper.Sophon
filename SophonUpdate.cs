﻿// ReSharper disable IdentifierTypo
// ReSharper disable StringLiteralTypo
// ReSharper disable CommentTypo
// ReSharper disable InvalidXmlDocComment

#if NET6_0_OR_GREATER
using ZstdNet;
#endif
using Google.Protobuf.Collections;
using Hi3Helper.Sophon.Helper;
using Hi3Helper.Sophon.Infos;
using Hi3Helper.Sophon.Protos;
using Hi3Helper.Sophon.Structs;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using TaskExtensions = Hi3Helper.Sophon.Helper.TaskExtensions;
using ZstdStream = ZstdNet.DecompressionStream;
// ReSharper disable ArrangeObjectCreationWhenTypeEvident

// ReSharper disable ConvertToUsingDeclaration
// ReSharper disable UseAwaitUsing

// ReSharper disable SuggestBaseTypeForParameter
// ReSharper disable ForCanBeConvertedToForeach

namespace Hi3Helper.Sophon
{
    public static class SophonUpdate
    {
        private static readonly object This = new();

        /// <summary>
        ///     Enumerate/Get the list of Sophon assets for update.
        /// </summary>
        /// <param name="httpClient">
        ///     The <seealso cref="HttpClient" /> to be used to download the manifest data.
        /// </param>
        /// <param name="infoPairOld">
        ///     Pair of the old Manifest and Chunks information struct.
        /// </param>
        /// <param name="infoPairNew">
        ///     Pair of the new Manifest and Chunks information struct.
        /// </param>
        /// <param name="downloadSpeedLimiter">
        ///     If the download speed limiter is null, the download speed will be set to unlimited.
        /// </param>
        /// <param name="token">
        ///     Cancellation token for handling cancellation while the routine is running.
        /// </param>
        /// <returns>
        ///     An enumeration to enumerate the Sophon update asset from the manifest.
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
        public static async IAsyncEnumerable<SophonAsset> EnumerateUpdateAsync(HttpClient                   httpClient,
                                                                               SophonChunkManifestInfoPair  infoPairOld,
                                                                               SophonChunkManifestInfoPair  infoPairNew,
                                                                               bool                         removeChunkAfterApply,
                                                                               SophonDownloadSpeedLimiter   downloadSpeedLimiter    = null,
                                                                               [EnumeratorCancellation]
                                                                               CancellationToken            token                   = default)

        {
            await foreach (SophonAsset asset in EnumerateUpdateAsync(httpClient,
                                                                     infoPairOld.ManifestInfo,
                                                                     infoPairOld.ChunksInfo,
                                                                     infoPairNew.ManifestInfo,
                                                                     infoPairNew.ChunksInfo,
                                                                     removeChunkAfterApply)
                              .WithCancellation(token))
            {
                yield return asset;
            }
        }


        /// <summary>
        ///     Enumerate/Get the list of Sophon assets for update.
        /// </summary>
        /// <param name="httpClient">
        ///     The <seealso cref="HttpClient" /> to be used to download the manifest data.
        /// </param>
        /// <param name="manifestInfoFrom">
        ///     Old manifest information struct.
        /// </param>
        /// <param name="chunksInfoFrom">
        ///     Old chunks information struct.
        /// </param>
        /// <param name="manifestInfoTo">
        ///     New manifest information struct.
        /// </param>
        /// <param name="chunksInfoTo">
        ///     New chunks information struct.
        /// </param>
        /// <param name="downloadSpeedLimiter">
        ///     If the download speed limiter is null, the download speed will be set to unlimited.
        /// </param>
        /// <param name="token">
        ///     Cancellation token for handling cancellation while the routine is running.
        /// </param>
        /// <returns>
        ///     An enumeration to enumerate the Sophon update asset from the manifest.
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
        public static async IAsyncEnumerable<SophonAsset> EnumerateUpdateAsync(HttpClient                 httpClient,
                                                                               SophonManifestInfo         manifestInfoFrom,
                                                                               SophonChunksInfo           chunksInfoFrom,
                                                                               SophonManifestInfo         manifestInfoTo,
                                                                               SophonChunksInfo           chunksInfoTo,
                                                                               bool                       removeChunkAfterApply,
                                                                               SophonDownloadSpeedLimiter downloadSpeedLimiter   = null,
                                                                               [EnumeratorCancellation]                          
                                                                               CancellationToken          token                  = default)
        {
        #if NET6_0_OR_GREATER
            if (!DllUtils.IsLibraryExist(DllUtils.DllName))
            {
                throw new DllNotFoundException("libzstd is not found!");
            }
        #endif

            ActionTimeoutValueTaskCallback<SophonManifestProto> manifestFromProtoTaskCallback = async innerToken =>
            {
            #if NET6_0_OR_GREATER
                await
                #endif
                    using (Stream manifestProtoStream =
                           await SophonAssetStream.CreateStreamAsync(httpClient,
                                                                     manifestInfoFrom.ManifestFileUrl,
                                                                     0,
                                                                     null,
                                                                     innerToken))
                {
                    using (Stream decompressedProtoStream = manifestInfoFrom.IsUseCompression
                               ? new ZstdStream(manifestProtoStream)
                               : manifestProtoStream)
                    {
                        return SophonManifestProto.Parser.ParseFrom(decompressedProtoStream);
                    }
                }
            };

            SophonManifestProto manifestFromProto = await TaskExtensions
               .WaitForRetryAsync(() => manifestFromProtoTaskCallback,
                                  TaskExtensions.DefaultTimeoutSec,
                                  null,
                                  null,
                                  null,
                                  token);

            ActionTimeoutValueTaskCallback<SophonManifestProto> manifestToProtoTaskCallback = async innerToken =>
            {
            #if NET6_0_OR_GREATER
                await
                #endif
                    using (Stream manifestProtoStream =
                           await SophonAssetStream.CreateStreamAsync(httpClient,
                                                                     manifestInfoTo.ManifestFileUrl,
                                                                     0,
                                                                     null,
                                                                     innerToken))
                {
                    using (Stream decompressedProtoStream = manifestInfoTo.IsUseCompression
                               ? new ZstdStream(manifestProtoStream)
                               : manifestProtoStream)
                    {
                        return SophonManifestProto.Parser.ParseFrom(decompressedProtoStream);
                    }
                }
            };

            SophonManifestProto manifestToProto = await TaskExtensions
               .WaitForRetryAsync(() => manifestToProtoTaskCallback,
                                  TaskExtensions.DefaultTimeoutSec,
                                  null,
                                  null,
                                  null,
                                  token);

            Dictionary<string, int> oldAssetNameIdx = GetProtoAssetHashKvpSet(manifestFromProto, x => x.AssetName);

            foreach (AssetProperty newAssetProperty in manifestToProto.Assets)
            {
                yield return GetPatchedTargetAsset(oldAssetNameIdx,
                                                   manifestFromProto,
                                                   newAssetProperty,
                                                   chunksInfoTo,
                                                   downloadSpeedLimiter);
            }
        }

        /// <summary>
        ///     Get the calculated diff size of an update between the old and new manifest.
        /// </summary>
        /// <param name="httpClient">
        ///     The <seealso cref="HttpClient" /> to be used to fetch the metadata information.
        /// </param>
        /// <param name="sophonAssetsEnumerable">
        ///     The enumerable of the asset. Use this as an extension of these methods:<br/>
        ///         <seealso cref="EnumerateUpdateAsync(HttpClient, SophonChunkManifestInfoPair, SophonChunkManifestInfoPair, bool, CancellationToken)"/><br/>
        ///         <seealso cref="EnumerateUpdateAsync(HttpClient, SophonManifestInfo, SophonChunksInfo, SophonManifestInfo, SophonChunksInfo, bool, CancellationToken)"/>
        /// </param>
        /// <param name="isGetDecompressSize">
        ///     Determine whether to get the decompressed or compressed size of the diff files.
        /// </param>
        /// <param name="token">
        ///     Cancellation token for handling cancellation while the routine is running.
        /// </param>
        /// <returns>
        ///     The calculated size of the diff from between the manifest.
        /// </returns>
        public static async
        #if NET6_0_OR_GREATER
            ValueTask<long>
        #else
            Task<long>
        #endif
            GetCalculatedDiffSizeAsync(this IAsyncEnumerable<SophonAsset> sophonAssetsEnumerable,
                                       bool               isGetDecompressSize = true,
                                       CancellationToken  token               = default)
        {
            long sizeDiff = 0;

            await foreach (SophonAsset asset in sophonAssetsEnumerable.WithCancellation(token))
            {
                if (asset.IsDirectory)
                {
                    continue;
                }

                SophonChunk[] chunks    = asset.Chunks;
                int           chunksLen = chunks.Length;
                for (int i = 0; i < chunksLen; i++)
                {
                    if (chunks[i].ChunkOldOffset != -1)
                    {
                        continue;
                    }

                    sizeDiff += isGetDecompressSize ? chunks[i].ChunkSizeDecompressed : chunks[i].ChunkSize;
                }
            }

            return sizeDiff;
        }

        /// <summary>
        ///     Get the calculated diff size of an update between the old and new manifest.
        ///     Use this as an extension of any <seealso cref="IEnumerable{T}"/> where <typeparamref name="T"/> is <seealso cref="SophonAsset"/>.
        /// </summary>
        /// <param name="sophonAssetsEnumerable">
        ///     The enumerable of the asset.
        /// </param>
        /// <param name="isGetDecompressSize">
        ///     Determine whether to get the decompressed or compressed size of the diff files.
        /// </param>
        /// <returns>
        ///     The calculated size of the diff from between the manifest.
        /// </returns>
        public static long GetCalculatedDiffSize(this IEnumerable<SophonAsset> sophonAssetsEnumerable,
                                                    bool isGetDecompressSize = true)
        {
            long sizeDiff = 0;

            foreach (SophonAsset asset in sophonAssetsEnumerable)
            {
                if (asset.IsDirectory)
                {
                    continue;
                }

                SophonChunk[] chunks = asset.Chunks;
                int chunksLen = chunks.Length;
                for (int i = 0; i < chunksLen; i++)
                {
                    if (chunks[i].ChunkOldOffset != -1)
                    {
                        continue;
                    }

                    sizeDiff += isGetDecompressSize ? chunks[i].ChunkSizeDecompressed : chunks[i].ChunkSize;
                }
            }

            return sizeDiff;
        }

        private static SophonAsset GetPatchedTargetAsset(Dictionary<string, int>    oldAssetNameIdx,
                                                         SophonManifestProto        oldAssetProto,
                                                         AssetProperty              newAssetProperty,
                                                         SophonChunksInfo           newChunksInfo,
                                                         SophonDownloadSpeedLimiter downloadSpeedLimiter)
        {
            // If the targeted asset has asset type != 0 or has no MD5 hash (is directory)
            // Or if the targeted asset is not exist in the old Hash set, then act it as a new asset.
            if (newAssetProperty.AssetType != 0 || string.IsNullOrEmpty(newAssetProperty.AssetHashMd5)
                                                || !oldAssetNameIdx.TryGetValue(newAssetProperty.AssetName,
                                                                                    out int oldAssetIdx))
            {
                return SophonManifest.AssetProperty2SophonAsset(newAssetProperty, newChunksInfo, downloadSpeedLimiter);
            }

            // Now check if the asset has a patch or not.
            AssetProperty oldAssetProperty = oldAssetProto.Assets[oldAssetIdx];
            if (oldAssetProperty == null) // SANITY CHECK
            {
                throw new
                    NullReferenceException($"This SHOULD NOT be happening! The old asset proto has no reference (null) to the asset: {newAssetProperty.AssetName} at old index: {oldAssetIdx}");
            }

            // Iterate and get the chunks information
            RepeatedField<AssetChunk> oldAssetProtoChunks = oldAssetProperty.AssetChunks;
            RepeatedField<AssetChunk> newAssetProtoChunks = newAssetProperty.AssetChunks;
            SophonChunk[] newAssetPatchedChunks =
                GetSophonChunkWithOldReference(oldAssetProtoChunks, newAssetProtoChunks, out bool isNewAssetHasPatch);

            // Return the new sophon asset
            string assetName = newAssetProperty.AssetName;
            string assetHash = newAssetProperty.AssetHashMd5;
            long   assetSize = newAssetProperty.AssetSize;
            return new SophonAsset
            {
                AssetName            = assetName,
                AssetHash            = assetHash,
                AssetSize            = assetSize,
                Chunks               = newAssetPatchedChunks,
                SophonChunksInfo     = newChunksInfo,
                IsDirectory          = false,
                IsHasPatch           = isNewAssetHasPatch,
                DownloadSpeedLimiter = downloadSpeedLimiter
            };
        }

        private static SophonChunk[] GetSophonChunkWithOldReference(RepeatedField<AssetChunk> oldProtoChunks,
                                                                    RepeatedField<AssetChunk> newProtoChunks,
                                                                    out bool                  isNewAssetHasPatch)
        {
            // Get the length of both old and new chunks from proto
            int           oldReturnChunksLen = oldProtoChunks.Count;
            int           newReturnChunksLen = newProtoChunks.Count;
            SophonChunk[] newReturnChunks    = new SophonChunk[newReturnChunksLen]; // Init new return chunks

            // Set initial HasPatch indicator
            isNewAssetHasPatch = false;

            // Build the old chunk hash name index set
            Dictionary<string, int> oldChunkNameIdx = new Dictionary<string, int>();
            for (int i = 0; i < oldReturnChunksLen; i++)
            {
            #if NET6_0_OR_GREATER
                if (!oldChunkNameIdx.TryAdd(oldProtoChunks[i].ChunkDecompressedHashMd5, i))
                {
                    This.PushLogWarning($"Chunk: {oldProtoChunks[i].ChunkName} is duplicated!");
                }
            #else
                if (oldChunkNameIdx.ContainsKey(oldProtoChunks[i].ChunkDecompressedHashMd5))
                {
                    This.PushLogWarning($"Chunk: {oldProtoChunks[i].ChunkName} is duplicated!");
                    continue;
                }

                oldChunkNameIdx.Add(oldProtoChunks[i].ChunkDecompressedHashMd5, i);
            #endif
            }

            // Iterate the new chunk to be processed for finding match old chunk
            for (int i = 0; i < newReturnChunksLen; i++)
            {
                // Assign new proto chunk as per index
                AssetChunk newProtoChunk = newProtoChunks[i];

                // Init the new chunk
                SophonChunk newChunk = new SophonChunk
                {
                    ChunkName = newProtoChunk.ChunkName,
                    ChunkHashDecompressed = Extension.HexToBytes(newProtoChunk.ChunkDecompressedHashMd5
                                                             #if !NET6_0_OR_GREATER
                                                                              .AsSpan()
                                                             #endif
                                                                ),
                    ChunkOldOffset        = -1, // Set as default (-1 means no old chunk reference [aka new diff])
                    ChunkOffset           = newProtoChunk.ChunkOnFileOffset,
                    ChunkSize             = newProtoChunk.ChunkSize,
                    ChunkSizeDecompressed = newProtoChunk.ChunkSizeDecompressed
                };

                // Try to get the value of the hash set from the old chunk. If it returns true, then
                // assign the old chunk offset value
                if (oldChunkNameIdx.TryGetValue(newProtoChunk.ChunkDecompressedHashMd5, out int oldProtoChunkIdx))
                {
                    isNewAssetHasPatch = true;
                    AssetChunk oldProtoChunk = oldProtoChunks[oldProtoChunkIdx];
                    newChunk.ChunkOldOffset = oldProtoChunk.ChunkOnFileOffset;
                }

                // Set the new chunk to the return array
                newReturnChunks[i] = newChunk;
            }

            // Return the new chunks array
            return newReturnChunks;
        }

        private static Dictionary<string, int> GetProtoAssetHashKvpSet(SophonManifestProto         proto,
                                                                       Func<AssetProperty, string> funcDelegate)
        {
            Dictionary<string, int> hashSet = new Dictionary<string, int>();
            for (int i = 0; i < proto.Assets.Count; i++)
            {
                hashSet.Add(funcDelegate(proto.Assets[i]), i);
            }

            return hashSet;
        }
    }
}