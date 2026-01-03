using Hi3Helper.Sophon.Infos;
using Hi3Helper.Sophon.Structs;
using System;

// ReSharper disable NonReadonlyMemberInGetHashCode
// ReSharper disable IdentifierTypo
// ReSharper disable StringLiteralTypo
// ReSharper disable CommentTypo

#nullable enable
namespace Hi3Helper.Sophon.Infos
{
    public class SophonChunksInfo : SophonIdentifiableProperty, IEquatable<SophonChunksInfo>
    {
        public string? ChunksBaseUrl       { get; set; }
        public int     ChunksCount         { get; set; }
        public int     FilesCount          { get; set; }
        public long    TotalSize           { get; set; }
        public long    TotalCompressedSize { get; set; }
        public bool    IsUseCompression    { get; set; }

        public bool Equals(SophonChunksInfo? other) =>
            ChunksBaseUrl == other?.ChunksBaseUrl &&
            ChunksCount == other?.ChunksCount &&
            FilesCount == other.FilesCount &&
            TotalSize == other.TotalSize &&
            TotalCompressedSize == other.TotalCompressedSize &&
            IsUseCompression == other.IsUseCompression &&
            base.Equals(other);

        public override bool Equals(object? obj) => obj is SophonChunksInfo other && Equals(other);

        public override int GetHashCode() => 
#if NET6_0_OR_GREATER
            HashCode.Combine(ChunksBaseUrl,
                             ChunksCount,
                             FilesCount,
                             TotalSize,
                             TotalCompressedSize,
                             IsUseCompression);
#else
            ChunksBaseUrl.GetHashCode() ^
            ChunksCount.GetHashCode() ^
            FilesCount.GetHashCode() ^
            TotalSize.GetHashCode() ^
            TotalCompressedSize.GetHashCode() ^
            IsUseCompression.GetHashCode();
#endif
    }
}

namespace Hi3Helper.Sophon
{
    public static partial class SophonManifest
    {
        /// <summary>
        ///     Create Sophon chunk information. Please refer the API response to set the argument value.
        /// </summary>
        /// <param name="chunksBaseUrl">
        ///     The base URL for the chunks. To find the value, See the API section called: <c>chunk_download</c> ->
        ///     <c>url_prefix</c>
        /// </param>
        /// <param name="chunksCount">
        ///     The count of chunks to be downloaded. To find the value, See the API section called: <c>stats</c> ->
        ///     <c>chunk_count</c>
        /// </param>
        /// <param name="filesCount">
        ///     The count of files to be downloaded. To find the value, See the API section called: <c>stats</c> ->
        ///     <c>file_count</c>
        /// </param>
        /// <param name="isUseCompression">
        ///     Determine the use of compression within the chunks. To find the value, See the API section called:
        ///     <c>chunk_download</c> -> <c>compression</c>
        /// </param>
        /// <param name="totalSize">
        ///     Total decompressed size of files to be downloaded. To find the value, See the API section called: <c>stats</c> ->
        ///     <c>uncompressed_size</c>
        /// </param>
        /// <param name="totalCompressedSize">
        ///     Total compressed size of files to be downloaded. To find the value, See the API section called: <c>stats</c> ->
        ///     <c>compressed_size</c>
        /// </param>
        /// <param name="matchingField">
        ///     The matching field of the parent manifest. To find the value, See the API section called: <c>matching_field</c>
        /// </param>
        /// <param name="categoryId">
        ///     The category ID of the parent manifest. To find the value, See the API section called: <c>category_id</c>
        /// </param>
        /// <param name="categoryName">
        ///     The category name of the parent manifest. To find the value, See the API section called: <c>category_name</c>
        /// </param>
        /// <returns>Sophon Chunks Information struct</returns>
        internal static SophonChunksInfo CreateChunksInfo(string  chunksBaseUrl,
                                                          int     chunksCount,
                                                          int     filesCount,
                                                          bool    isUseCompression,
                                                          long    totalSize,
                                                          long    totalCompressedSize,
                                                          string? matchingField,
                                                          int     categoryId,
                                                          string? categoryName)
        {
            return new SophonChunksInfo
            {
                ChunksBaseUrl       = chunksBaseUrl,
                ChunksCount         = chunksCount,
                FilesCount          = filesCount,
                IsUseCompression    = isUseCompression,
                TotalSize           = totalSize,
                TotalCompressedSize = totalCompressedSize,
                MatchingField       = matchingField,
                CategoryId          = categoryId,
                CategoryName        = categoryName
            };
        }
    }
}