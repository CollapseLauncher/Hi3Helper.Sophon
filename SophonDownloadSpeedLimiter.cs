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

#if NET6_0_OR_GREATER
    internal ValueTask AddBytesOrWaitAsync(long readBytes, CancellationToken token)
        => AddBytesOrWaitAsyncDelegate?.Invoke(Context, readBytes, token) ?? ValueTask.CompletedTask;
#else
    internal async ValueTask AddBytesOrWaitAsync(long readBytes, CancellationToken token)
    {
        if (AddBytesOrWaitAsyncDelegate == null)
        {
            return;
        }

        await AddBytesOrWaitAsyncDelegate.Invoke(Context, readBytes, token);
    }
#endif
}
