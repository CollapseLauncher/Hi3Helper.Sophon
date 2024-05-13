namespace Hi3Helper.Sophon;

/// <summary>
///     Delegate to get an info of how much bytes being read per cycle.
/// </summary>
/// <param name="read">
///     Number of bytes being read.
/// </param>
public delegate void DelegateReadStreamInfo(long read);

/// <summary>
///     Delegate to get an info of which asset that has been downloaded.
/// </summary>
/// <param name="asset">
///     Downloaded asset property.
/// </param>
public delegate void DelegateDownloadAssetComplete(SophonAsset asset);