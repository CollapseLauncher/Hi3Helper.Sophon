// ReSharper disable once IdentifierTypo
namespace Hi3Helper.Sophon.Helper
{
    /// <summary>
    ///     Delegate to get an info of how much bytes being written per cycle while downloading/writing to disk.
    /// </summary>
    /// <param name="write">
    ///     Number of bytes being written.
    /// </param>
    public delegate void DelegateWriteStreamInfo(long write);

    /// <summary>
    ///     Delegate to get an info of which asset that has been downloaded.
    /// </summary>
    /// <param name="asset">
    ///     Downloaded asset property.
    /// </param>
    public delegate void DelegateDownloadAssetComplete(SophonAsset asset);
}