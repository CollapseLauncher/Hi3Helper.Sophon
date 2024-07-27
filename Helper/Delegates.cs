// ReSharper disable once IdentifierTypo
namespace Hi3Helper.Sophon.Helper
{
    /// <summary>
    ///     Delegate to get an info of how much bytes being written per cycle while writing to disk.
    /// </summary>
    /// <param name="writeBytes">
    ///     Number of bytes being written.
    /// </param>
    public delegate void DelegateWriteStreamInfo(long writeBytes);

    /// <summary>
    ///     Delegate to get an info of how much bytes being written per cycle while downloading and writing to disk.
    /// </summary>
    /// <param name="downloadedBytes">
    ///     Number of bytes in downloaded state (can be downloaded bytes or current downloading bytes).
    /// </param>
    /// <param name="diskWriteBytes">
    ///     Number of bytes currently being downloaded and written to disk.
    /// </param>
    public delegate void DelegateWriteDownloadInfo(long downloadedBytes, long diskWriteBytes);

    /// <summary>
    ///     Delegate to get an info of which asset that has been downloaded.
    /// </summary>
    /// <param name="asset">
    ///     Downloaded asset property.
    /// </param>
    public delegate void DelegateDownloadAssetComplete(SophonAsset asset);
}