using System;
using System.Threading;
using System.Threading.Tasks;

// ReSharper disable UnusedType.Global
// ReSharper disable InvalidXmlDocComment
// ReSharper disable IdentifierTypo

#nullable enable
namespace Hi3Helper.Sophon;

public class SophonDownloadSpeedLimiter
{
    public static Func<nint, long, CancellationToken, ValueTask>? AddBytesOrWaitAsyncDelegate;

    public nint Context { get; }

    private SophonDownloadSpeedLimiter(nint serviceContext)
    {
        Context = serviceContext;
    }

    /// <summary>
    /// Create an instance by using current service context.
    /// </summary>
    /// <param name="serviceContext">The context to be used for the service.</param>
    /// <returns>An instance of the speed limiter</returns>
    public static SophonDownloadSpeedLimiter CreateInstance(nint serviceContext)
        => new(serviceContext);

    internal ValueTask AddBytesOrWaitAsync(long readBytes, CancellationToken token)
        => AddBytesOrWaitAsyncDelegate?.Invoke(Context, readBytes, token) ?? ValueTask.CompletedTask;
}
