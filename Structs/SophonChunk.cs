// ReSharper disable IdentifierTypo
// ReSharper disable ConvertToPrimaryConstructor

namespace Hi3Helper.Sophon.Structs
{
    public struct SophonChunk
    {
        public SophonChunk()
        {
            // -1 as if it does not have reference of the offset pos to the old file
            ChunkOldOffset = -1;
        }

        public string ChunkName;
        public byte[] ChunkHashDecompressed;
        public long   ChunkOldOffset;
        public long   ChunkOffset;
        public long   ChunkSize;
        public long   ChunkSizeDecompressed;
    }
}