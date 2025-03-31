using Hi3Helper.Sophon.Infos;
using System.Collections.Generic;
using System.Linq;

// ReSharper disable IdentifierTypo
// ReSharper disable StringLiteralTypo
// ReSharper disable CommentTypo
namespace Hi3Helper.Sophon.Structs
{
    public class SophonChunkManifestInfoPair
    {
        public SophonChunksInfo        ChunksInfo           { get; internal set; }
        public SophonManifestInfo      ManifestInfo         { get; internal set; }
        public SophonManifestBuildData OtherSophonBuildData { get; internal set; }
        public SophonManifestPatchData OtherSophonPatchData { get; internal set; }
        public bool                    IsFound              { get; internal set; } = true;
        public int                     ReturnCode           { get; internal set; } = 0;
        public string                  ReturnMessage        { get; internal set; }

        public SophonChunkManifestInfoPair GetOtherManifestInfoPair(string matchingField)
        {
            SophonManifestBuildIdentity sophonManifestIdentity =
                OtherSophonBuildData.ManifestIdentityList?.FirstOrDefault(x => x.MatchingField == matchingField);

            if (sophonManifestIdentity == null)
            {
                throw new KeyNotFoundException($"Sophon manifest with matching field: {matchingField} is not found!");
            }

            return new SophonChunkManifestInfoPair
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
                OtherSophonPatchData = OtherSophonPatchData
            };
        }

        public SophonChunkManifestInfoPair GetOtherPatchInfoPair(string matchingField, string versionUpdateFrom)
        {
            SophonManifestPatchIdentity sophonPatchIdentity =
                OtherSophonPatchData.ManifestIdentityList?.FirstOrDefault(x => x.MatchingField == matchingField);

            if (sophonPatchIdentity == null)
            {
                throw new KeyNotFoundException($"Sophon patch with matching field: {matchingField} is not found!");
            }

            if (!sophonPatchIdentity
                .DiffTaggedInfo
                .TryGetValue(versionUpdateFrom,
                             out SophonManifestChunkInfo sophonChunkInfo))
            {
                throw new KeyNotFoundException($"Sophon patch diff tagged info with tag: {OtherSophonPatchData.TagName} is not found!");
            }

            return new SophonChunkManifestInfoPair
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
                OtherSophonPatchData = OtherSophonPatchData
            };
        }
    }
}