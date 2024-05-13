using Hi3Helper.Sophon.Infos;
using System.Collections.Generic;
using System.Linq;

namespace Hi3Helper.Sophon;
public struct SophonChunkManifestInfoPair
    {
        public SophonChunksInfo   ChunksInfo;
        public SophonManifestInfo ManifestInfo;
        public SophonData         OtherSophonData;

        public SophonChunkManifestInfoPair GetOtherManifestInfoPair(string? matchingField)
        {
            var sophonManifestIdentity =
                OtherSophonData.ManifestIdentityList?.FirstOrDefault(x => x.MatchingField == matchingField);

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
                OtherSophonData = OtherSophonData
            };
        }
    }