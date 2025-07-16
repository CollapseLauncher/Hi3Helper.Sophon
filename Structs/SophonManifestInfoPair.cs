using Hi3Helper.Sophon.Infos;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.CompilerServices;

#nullable enable
// ReSharper disable IdentifierTypo
// ReSharper disable StringLiteralTypo
// ReSharper disable CommentTypo
namespace Hi3Helper.Sophon.Structs
{
    public class SophonChunkManifestInfoPair
    {
        public   SophonChunksInfo?        ChunksInfo           { get; internal set; }
        public   SophonManifestInfo?      ManifestInfo         { get; internal set; }
        public   SophonManifestBuildData? OtherSophonBuildData { get; internal set; }
        public   SophonManifestPatchData? OtherSophonPatchData { get; internal set; }
        public   bool                     IsFound              { get; internal set; } = true;
        public   int                      ReturnCode           { get; internal set; } = 0;
        public   string?                  ReturnMessage        { get; internal set; }
        public   string?                  MatchingField        { get; internal set; }
        public   string?                  CategoryName         { get; internal set; }
        public   int                      CategoryId           { get; internal set; }
        internal HashSet<string>          AssetKeepPathList    { get; set; } = [];

        public SophonChunkManifestInfoPair GetOtherManifestInfoPair(string matchingField)
        {
            SophonManifestBuildIdentity? sophonManifestIdentity =
                OtherSophonBuildData?.ManifestIdentityList?
                   .FirstOrDefault(x => x.MatchingField == matchingField);

            if (sophonManifestIdentity == null)
            {
                throw new KeyNotFoundException($"Sophon manifest with matching field: {matchingField} is not found!");
            }

            SophonChunkManifestInfoPair otherManifestIdentity = new SophonChunkManifestInfoPair
            {
                ChunksInfo = SophonManifest.CreateChunksInfo(sophonManifestIdentity.ChunksUrlInfo.UrlPrefix,
                                                             sophonManifestIdentity.ChunkInfo.ChunkCount,
                                                             sophonManifestIdentity.ChunkInfo.FileCount,
                                                             sophonManifestIdentity.ChunksUrlInfo.IsCompressed,
                                                             sophonManifestIdentity.ChunkInfo.UncompressedSize,
                                                             sophonManifestIdentity.ChunkInfo.CompressedSize),
                ManifestInfo = SophonManifest.CreateManifestInfo(sophonManifestIdentity.ManifestUrlInfo.UrlPrefix,
                                                                 sophonManifestIdentity.ManifestFileInfo.Checksum,
                                                                 sophonManifestIdentity.ManifestFileInfo.FileName,
                                                                 sophonManifestIdentity.ManifestUrlInfo.IsCompressed,
                                                                 sophonManifestIdentity.ManifestFileInfo.UncompressedSize,
                                                                 sophonManifestIdentity.ManifestFileInfo.CompressedSize),
                OtherSophonBuildData = OtherSophonBuildData,
                OtherSophonPatchData = OtherSophonPatchData,
                MatchingField        = sophonManifestIdentity.MatchingField,
                CategoryName         = sophonManifestIdentity.CategoryName,
                CategoryId           = sophonManifestIdentity.CategoryId,
                AssetKeepPathList  = AssetKeepPathList
            };
            otherManifestIdentity.ManifestInfo.AssetKeepPathList = AssetKeepPathList;

            return otherManifestIdentity;
        }

        public bool TryGetOtherPatchInfoPair(string matchingField,
                                             string versionUpdateFrom,
                                             [NotNullWhen(true)] out SophonChunkManifestInfoPair? otherPatchIdentity)
            => TryGetOtherPatchInfoPair(matchingField, versionUpdateFrom, false, out otherPatchIdentity);

        public bool TryGetOtherPatchInfoPair(string matchingField,
                                             string versionUpdateFrom,
                                             bool findNearVersionIfNoAvail,
                                             [NotNullWhen(true)] out SophonChunkManifestInfoPair? otherPatchIdentity)
        {
            Unsafe.SkipInit(out otherPatchIdentity);

            SophonManifestPatchIdentity? sophonPatchIdentity =
                OtherSophonPatchData?.ManifestIdentityList?
                   .FirstOrDefault(x => x.MatchingField == matchingField);

            if (sophonPatchIdentity == null)
            {
                return false;
            }

            // If the patch identity isn't found, try to find the one that's near with current version.
            if (!sophonPatchIdentity
                .DiffTaggedInfo
                .TryGetValue(versionUpdateFrom,
                             out var sophonChunkInfo) && findNearVersionIfNoAvail)
            {
                var versions = sophonPatchIdentity.DiffTaggedInfo.Keys;
                string? latestVersion = versions.MaxBy(Version.Parse);

                if (string.IsNullOrEmpty(latestVersion))
                {
                    return false;
                }

                sophonPatchIdentity
                   .DiffTaggedInfo
                   .TryGetValue(latestVersion, out sophonChunkInfo);
            }

            // If it's still null, then return false.
            if (sophonChunkInfo == null)
            {
                return false;
            }

            otherPatchIdentity = new SophonChunkManifestInfoPair
            {
                ChunksInfo = SophonManifest.CreateChunksInfo(sophonPatchIdentity.DiffUrlInfo.UrlPrefix,
                                                             sophonChunkInfo.ChunkCount,
                                                             sophonChunkInfo.FileCount,
                                                             sophonPatchIdentity.DiffUrlInfo.IsCompressed,
                                                             sophonChunkInfo.UncompressedSize,
                                                             sophonChunkInfo.CompressedSize),
                ManifestInfo = SophonManifest.CreateManifestInfo(sophonPatchIdentity.ManifestUrlInfo.UrlPrefix,
                                                                 sophonPatchIdentity.ManifestFileInfo.Checksum,
                                                                 sophonPatchIdentity.ManifestFileInfo.FileName,
                                                                 sophonPatchIdentity.ManifestUrlInfo.IsCompressed,
                                                                 sophonPatchIdentity.ManifestFileInfo.UncompressedSize,
                                                                 sophonPatchIdentity.ManifestFileInfo.CompressedSize),
                OtherSophonBuildData = OtherSophonBuildData,
                OtherSophonPatchData = OtherSophonPatchData,
                MatchingField        = sophonPatchIdentity.MatchingField,
                CategoryName         = sophonPatchIdentity.CategoryName,
                CategoryId           = sophonPatchIdentity.CategoryId,
                AssetKeepPathList  = AssetKeepPathList
            };
            otherPatchIdentity.ManifestInfo.AssetKeepPathList = AssetKeepPathList;

            return true;
        }

        public SophonChunkManifestInfoPair GetOtherPatchInfoPair(string matchingField,
                                                                 string versionUpdateFrom,
                                                                 bool   findNearVersionIfNoAvail = false)
        {
            if (TryGetOtherPatchInfoPair(matchingField, versionUpdateFrom, findNearVersionIfNoAvail, out var otherPatchIdentity))
            {
                return otherPatchIdentity;
            }

            throw new KeyNotFoundException($"Cannot find other sophon patch with matching field: {matchingField}!");
        }
    }
}