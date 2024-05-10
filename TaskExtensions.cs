using System;
using System.Threading;
using System.Threading.Tasks;

namespace Hi3Helper.Sophon
{
    internal static class TaskExtensions
    {
        internal const int DefaultTimeoutSec = 10;
        internal const int DefaultRetryAttempt = 5;

        internal static async Task<T?> RetryTimeoutAfter<T>(Func<Task<T?>> taskFunction, CancellationToken token = default, int timeout = DefaultTimeoutSec, int retryAttempt = DefaultRetryAttempt)
        {
            int retryTotal = retryAttempt;
            int lastTaskID = 0;
            Exception? lastException = null;

            while (retryTotal > 0)
            {
                try
                {
                    lastException = null;
                    Task<T?> taskDelegated = taskFunction();
                    lastTaskID = taskDelegated.Id;
                    Task<T?> completedTask = await Task.WhenAny(taskDelegated, ThrowExceptionAfterTimeout<T?>(timeout, taskDelegated, token));
                    if (completedTask == taskDelegated)
                        return await taskDelegated;
                }
                catch (TaskCanceledException) { throw; }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    lastException = ex;
                    await Task.Delay(1000); // Wait 1s interval before retrying
                    continue;
                }
            }

            if (lastException != null) throw lastException;
            throw new TimeoutException($"The operation for task ID: {lastTaskID} has timed out!");
        }

        internal static async ValueTask<T?> TimeoutAfter<T>(this Task<T?> task, CancellationToken token = default, int timeout = DefaultTimeoutSec)
        {
            Task<T?> completedTask = await Task.WhenAny(task, ThrowExceptionAfterTimeout<T?>(timeout, task, token));
            return await completedTask;
        }

        private static async Task<T?> ThrowExceptionAfterTimeout<T>(int timeout, Task mainTask, CancellationToken token = default)
        {
            if (token.IsCancellationRequested)
                throw new OperationCanceledException();

            int timeoutMs = timeout * 1000;
            await Task.Delay(timeoutMs, token);
            if (!(mainTask.IsCompleted ||
#if NETCOREAPP
                mainTask.IsCompletedSuccessfully ||
#endif
                mainTask.IsCanceled || mainTask.IsFaulted || mainTask.Exception != null))
                throw new TimeoutException($"The operation for task has timed out!");

            return default;
        }
    }
}
