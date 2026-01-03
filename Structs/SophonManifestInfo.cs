using Hi3Helper.Sophon.Infos;
using Hi3Helper.Sophon.Structs;

// ReSharper disable IdentifierTypo
// ReSharper disable StringLiteralTypo
// ReSharper disable CommentTypo

#nullable enable
namespace Hi3Helper.Sophon.Infos
{
    public class SophonManifestInfo : SophonIdentifiableProperty
    {
        public required string ManifestBaseUrl        { get; init; }
        public required string ManifestId             { get; init; }
        public required string ManifestChecksumMd5    { get; init; }
        public          bool   IsUseCompression       { get; init; }
        public          long   ManifestSize           { get; init; }
        public          long   ManifestCompressedSize { get; init; }

        public string ManifestFileUrl => ManifestBaseUrl.TrimEnd('/') + '/' + ManifestId;
    }
}

namespace Hi3Helper.Sophon
{
    public static partial class SophonManifest
    {
        /// <summary>
        ///     Create Sophon Build Manifest Information. Please refer the API response to set the argument value.
        /// </summary>
        /// <param name="manifestBaseUrl">
        ///     The base URL for the manifest. To find the value, See the API section called: <c>manifest_download</c> ->
        ///     <c>url_prefix</c>
        /// </param>
        /// <param name="manifestChecksumMd5">
        ///     The MD5 hash of the manifest. To find the value, See the API section called: <c>manifest</c> -> <c>checksum</c>
        /// </param>
        /// <param name="manifestId">
        ///     The file name/id of the manifest. To find the value, See the API section called: <c>manifest</c> -> <c>id</c>
        /// </param>
        /// <param name="isUseCompression">
        ///     Determine the use of compression within the manifest. To find the value, See the API section called:
        ///     <c>manifest_download</c> -> <c>compression</c>
        /// </param>
        /// <param name="manifestSize">
        ///     The decompressed size of the manifest file. To find the value, See the API section called: <c>stats</c> ->
        ///     <c>uncompressed_size</c>
        /// </param>
        /// <param name="manifestCompressedSize">
        ///     The compressed size of the manifest file. To find the value, See the API section called: <c>stats</c> ->
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
        /// <returns>Sophon Manifest Build Information instance</returns>
        public static SophonManifestInfo CreateManifestInfo(string  manifestBaseUrl,
                                                            string  manifestChecksumMd5,
                                                            string  manifestId,
                                                            bool    isUseCompression,
                                                            long    manifestSize,
                                                            long    manifestCompressedSize,
                                                            string? matchingField,
                                                            int     categoryId,
                                                            string? categoryName)
            => new()
            {
                ManifestBaseUrl        = manifestBaseUrl,
                ManifestChecksumMd5    = manifestChecksumMd5,
                ManifestId             = manifestId,
                IsUseCompression       = isUseCompression,
                ManifestSize           = manifestSize,
                ManifestCompressedSize = manifestCompressedSize,
                MatchingField          = matchingField,
                CategoryId             = categoryId,
                CategoryName           = categoryName
            };
    }
}