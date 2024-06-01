// ReSharper disable IdentifierTypo
namespace Hi3Helper.Sophon.Structs
{
    public struct SophonChunk
    {
        public string ChunkName;
        public byte[] ChunkHashDecompressed;
        public long ChunkOffset;
        public long ChunkSize;
        public long ChunkSizeDecompressed;
    }
}