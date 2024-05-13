using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Hi3Helper.Sophon;

internal static class TaskExtensions
{
    internal const int DefaultTimeoutSec   = 10;
    internal const int DefaultRetryAttempt = 5;

    internal static async Task<T?> RetryTimeoutAfter<T>(Func<Task<T?>> taskFunction, CancellationToken token = default,
                                                        int            timeout      = DefaultTimeoutSec,
                                                        int            retryAttempt = DefaultRetryAttempt)
    {
        int        retryTotal    = 0;
        int        lastTaskID    = 0;
        Exception? lastException = null;

        while (retryTotal <= retryAttempt)
        {
            try
            {
                lastException = null;
                Task<T?> taskDelegated = taskFunction();
                lastTaskID = taskDelegated.Id;
                Task<T?> completedTask =
                    await Task.WhenAny(taskDelegated, ThrowExceptionAfterTimeout<T?>(timeout, taskDelegated, token));
                if (completedTask == taskDelegated)
                    return await taskDelegated;

                if (completedTask.Exception != null)
                    throw completedTask.Exception.Flatten().InnerExceptions.First();
            }
            catch (TaskCanceledException)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastException = ex;
                await Task.Delay(TimeSpan.FromSeconds(1), token); // Wait 1s interval before retrying
            }
            finally
            {
                retryTotal++;
            }
        }

        if (lastException != null) throw lastException;
        throw new TimeoutException($"The operation for task ID: {lastTaskID} has timed out!");
    }

    internal static async ValueTask<T?> TimeoutAfter<T>(this Task<T?> task, CancellationToken token = default, int timeout = DefaultTimeoutSec)
        => await await Task.WhenAny(task, ThrowExceptionAfterTimeout<T?>(timeout, task, token));

    private static async Task<T?> ThrowExceptionAfterTimeout<T>(int timeout, Task mainTask, CancellationToken token = default)
    {
        if (token.IsCancellationRequested)
            throw new OperationCanceledException();

        await Task.Delay(TimeSpan.FromSeconds(timeout), token);
        if (!(mainTask.IsCompleted ||
              mainTask.IsCompletedSuccessfully ||
              mainTask.IsCanceled || mainTask.IsFaulted || mainTask.Exception != null))
            throw new TimeoutException($"The operation for task has timed out!");

        return default;
    }
}