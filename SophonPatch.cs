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

#if NET6_0_OR_GREATER
using ZstdNet;
#endif

// ReSharper disable CommentTypo
// ReSharper disable IdentifierTypo
// ReSharper disable StringLiteralTypo

namespace Hi3Helper.Sophon
{
    public static partial class SophonPatch
    {
        private static readonly object DummyInstance = new();

        /// <summary>
        ///     Enumerate/Get the list of Sophon patches for update.
        /// </summary>
        /// <param name="httpClient">
        ///     The <seealso cref="HttpClient" /> to be used to download the manifest data.
        /// </param>
        /// <param name="infoPair">
        ///     Pair of the Patch Manifest and Chunks information struct.
        /// </param>
        /// <param name="downloadSpeedLimiter">
        ///     If the download speed limiter is null, the download speed will be set to unlimited.
        /// </param>
        /// <param name="downloadOverUrl">
        ///     The URL to download the file with DownloadOver method if it's not existence in the game directory.
        /// </param>
        /// <param name="versionTagUpdateFrom">
        ///     Define which version tag in which the data will be patched from.
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
        /// <exception cref="ArgumentNullException">
        ///     Indicates if an argument is <c>null</c> or empty.
        /// </exception>
        public static async IAsyncEnumerable<SophonPatchAsset> EnumerateUpdateAsync(HttpClient httpClient,
                                                                                    SophonChunkManifestInfoPair infoPair,
                                                                                    string versionTagUpdateFrom,
                                                                                    string downloadOverUrl,
                                                                                    SophonDownloadSpeedLimiter downloadSpeedLimiter = null,
                                                                                    [EnumeratorCancellation]
                                                                                    CancellationToken token = default)

        {
            await foreach (SophonPatchAsset asset in EnumerateUpdateAsync(httpClient,
                                                                          infoPair.ManifestInfo,
                                                                          infoPair.ChunksInfo,
                                                                          versionTagUpdateFrom,
                                                                          downloadOverUrl,
                                                                          downloadSpeedLimiter,
                                                                          token))
            {
                yield return asset;
            }
        }


        /// <summary>
        ///     Enumerate/Get the list of Sophon patches for update.
        /// </summary>
        /// <param name="httpClient">
        ///     The <seealso cref="HttpClient" /> to be used to download the manifest data.
        /// </param>
        /// <param name="manifestInfo">
        ///     Patch Manifest information struct.
        /// </param>
        /// <param name="chunksInfo">
        ///     Patch Chunks information struct.
        /// </param>
        /// <param name="downloadSpeedLimiter">
        ///     If the download speed limiter is null, the download speed will be set to unlimited.
        /// </param>
        /// <param name="versionTagUpdateFrom">
        ///     Define which version tag in which the data will be patched from.
        /// </param>
        /// <param name="downloadOverUrl">
        ///     The URL to download the file with DownloadOver method if it's not existence in the game directory.
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
        /// <exception cref="ArgumentNullException">
        ///     Indicates if an argument is <c>null</c> or empty.
        /// </exception>
        public static async IAsyncEnumerable<SophonPatchAsset> EnumerateUpdateAsync(HttpClient httpClient,
                                                                                    SophonManifestInfo manifestInfo,
                                                                                    SophonChunksInfo chunksInfo,
                                                                                    string versionTagUpdateFrom,
                                                                                    string downloadOverUrl,
                                                                                    SophonDownloadSpeedLimiter downloadSpeedLimiter = null,
                                                                                    [EnumeratorCancellation]
                                                                                    CancellationToken token = default)
        {
#if NET6_0_OR_GREATER
            if (!DllUtils.IsLibraryExist(DllUtils.DllName))
            {
                throw new DllNotFoundException("libzstd is not found!");
            }
#endif

            if (string.IsNullOrEmpty(downloadOverUrl))
            {
                throw new ArgumentNullException(nameof(downloadOverUrl), "DownloadOver URL is not defined!");
            }

            if (string.IsNullOrEmpty(versionTagUpdateFrom))
            {
                throw new ArgumentNullException(nameof(versionTagUpdateFrom), "Version tag is not defined!");
            }

            ActionTimeoutTaskCallback<SophonPatchProto> manifestFromProtoTaskCallback =
                async innerToken => await httpClient.ReadProtoFromManifestInfo(manifestInfo, SophonPatchProto.Parser, innerToken);

            SophonPatchProto patchManifestProto = await TaskExtensions
               .WaitForRetryAsync(() => manifestFromProtoTaskCallback,
                                  TaskExtensions.DefaultTimeoutSec,
                                  null,
                                  null,
                                  null,
                                  token);

            SophonChunksInfo chunksInfoDownloadOver = chunksInfo.CopyWithNewBaseUrl(downloadOverUrl);

            foreach (SophonPatchAssetProperty patchAssetProperty in patchManifestProto.PatchAssets)
            {
                SophonPatchAssetInfo patchAssetInfo = patchAssetProperty
                    .AssetInfos
                    .FirstOrDefault(x => x.VersionTag.Equals(versionTagUpdateFrom, StringComparison.OrdinalIgnoreCase));

                if (patchAssetInfo == null)
                {
                    yield return new SophonPatchAsset
                    {
                        PatchInfo                     = chunksInfoDownloadOver,
                        TargetFileHash                = patchAssetProperty.AssetHashMd5,
                        TargetFileSize                = patchAssetProperty.AssetSize,
                        TargetFilePath                = patchAssetProperty.AssetName,
                        TargetFileDownloadOverBaseUrl = downloadOverUrl,
                        PatchMethod                   = SophonPatchMethod.DownloadOver,
                    };
                    continue;
                }

                if (string.IsNullOrEmpty(patchAssetInfo.Chunk.OriginalFileName))
                {
                    yield return new SophonPatchAsset
                    {
                        PatchInfo                     = chunksInfo,
                        PatchNameSource               = patchAssetInfo.Chunk.PatchName,
                        PatchHash                     = patchAssetInfo.Chunk.PatchMd5,
                        PatchOffset                   = patchAssetInfo.Chunk.PatchOffset,
                        PatchSize                     = patchAssetInfo.Chunk.PatchSize,
                        PatchChunkLength              = patchAssetInfo.Chunk.PatchLength,
                        TargetFilePath                = patchAssetProperty.AssetName,
                        TargetFileHash                = patchAssetProperty.AssetHashMd5,
                        TargetFileSize                = patchAssetProperty.AssetSize,
                        TargetFileDownloadOverBaseUrl = downloadOverUrl,
                        PatchMethod                   = SophonPatchMethod.CopyOver,
                    };
                    continue;
                }

                yield return new SophonPatchAsset
                {
                    PatchInfo                     = chunksInfo,
                    PatchNameSource               = patchAssetInfo.Chunk.PatchName,
                    PatchHash                     = patchAssetInfo.Chunk.PatchMd5,
                    PatchOffset                   = patchAssetInfo.Chunk.PatchOffset,
                    PatchSize                     = patchAssetInfo.Chunk.PatchSize,
                    PatchChunkLength              = patchAssetInfo.Chunk.PatchLength,
                    TargetFilePath                = patchAssetProperty.AssetName,
                    TargetFileHash                = patchAssetProperty.AssetHashMd5,
                    TargetFileSize                = patchAssetProperty.AssetSize,
                    TargetFileDownloadOverBaseUrl = downloadOverUrl,
                    OriginalFilePath              = patchAssetInfo.Chunk.OriginalFileName,
                    OriginalFileSize              = patchAssetInfo.Chunk.OriginalFileLength,
                    OriginalFileHash              = patchAssetInfo.Chunk.OriginalFileMd5,
                    PatchMethod                   = SophonPatchMethod.Patch,
                };
            }

            foreach (SophonUnusedAssetFile unusedAssetFile in patchManifestProto
                .UnusedAssets
                .SelectMany(x => x.AssetInfos.FirstOrDefault()?.Assets)
                .Where(x => x != null))
            {
                yield return new SophonPatchAsset
                {
                    OriginalFileHash = unusedAssetFile.FileMd5,
                    OriginalFileSize = unusedAssetFile.FileSize,
                    OriginalFilePath = unusedAssetFile.FileName,
                    PatchMethod      = SophonPatchMethod.Remove
                };
            }
        }

        public static IEnumerable<SophonPatchAsset> EnsureOnlyGetDedupPatchAssets(this IEnumerable<SophonPatchAsset> patchAssetEnumerable)
        {
            HashSet<string> processedAsset = [];
            foreach (SophonPatchAsset asset in patchAssetEnumerable
                .Where(x => !string.IsNullOrEmpty(x.PatchNameSource) && processedAsset.Add(x.PatchNameSource)))
            {
                yield return asset;
            }
        }

        public static void RemovePatches(this IEnumerable<SophonPatchAsset> patchAssetEnumerable, string patchOutputDir)
        {
            foreach (SophonPatchAsset asset in patchAssetEnumerable
                .EnsureOnlyGetDedupPatchAssets())
            {
                string patchFilePath = Path.Combine(patchOutputDir, asset.PatchNameSource);

                try
                {
                    FileInfo fileInfo = new FileInfo(patchFilePath);
                    if (fileInfo.Exists)
                    {
                        fileInfo.IsReadOnly = false;
                        fileInfo.Refresh();
                        fileInfo.Delete();
                        DummyInstance.PushLogDebug($"Removed patch file: {patchFilePath}");
                    }
                }
                catch (Exception ex)
                {
                    DummyInstance.PushLogError($"Failed while trying to remove patch file: {patchFilePath} | {ex}");
                }
            }
        }
    }
}
