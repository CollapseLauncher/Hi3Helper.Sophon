﻿using System;
using System.Threading;
using System.Threading.Tasks;

// ReSharper disable ConvertIfStatementToNullCoalescingAssignment

// ReSharper disable once IdentifierTypo
namespace Hi3Helper.Sophon.Helper
{
    internal delegate
    #if NETSTANDARD2_0 || NET6_0_OR_GREATER
        ValueTask<TResult>
    #else
        Task<TResult>
    #endif
        ActionTimeoutValueTaskCallback<TResult>(CancellationToken token);

    internal delegate void ActionOnTimeOutRetry(int retryAttemptCount, int retryAttemptTotal, int timeOutSecond,
                                                int timeOutStep);

    internal static class TaskExtensions
    {
        internal const int DefaultTimeoutSec   = 20;
        internal const int DefaultRetryAttempt = 10;

        internal static async
        #if NETSTANDARD2_0 || NET6_0_OR_GREATER
            ValueTask<TResult>
        #else
            Task<TResult>
        #endif
            WaitForRetryAsync<TResult>(Func<ActionTimeoutValueTaskCallback<TResult>> funcCallback,
                                       int?                                          timeout       = null,
                                       int?                                          timeoutStep   = null,
                                       int?                                          retryAttempt  = null,
                                       ActionOnTimeOutRetry                          actionOnRetry = null,
                                       CancellationToken                             fromToken     = default)
        {
            if (timeout == null)
            {
                timeout = DefaultTimeoutSec;
            }

            if (retryAttempt == null)
            {
                retryAttempt = DefaultRetryAttempt;
            }

            if (timeoutStep == null)
            {
                timeoutStep = 0;
            }

            int       retryAttemptCurrent = 1;
            Exception lastException       = null;
            while (retryAttemptCurrent < retryAttempt)
            {
                fromToken.ThrowIfCancellationRequested();
                CancellationTokenSource innerCancellationToken = null;
                CancellationTokenSource consolidatedToken      = null;

                try
                {
                    innerCancellationToken =
                        new CancellationTokenSource(TimeSpan.FromSeconds(timeout ?? DefaultTimeoutSec));
                    consolidatedToken =
                        CancellationTokenSource.CreateLinkedTokenSource(innerCancellationToken.Token, fromToken);

                    ActionTimeoutValueTaskCallback<TResult> delegateCallback = funcCallback();
                    return await delegateCallback(consolidatedToken.Token);
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
                    actionOnRetry?.Invoke(retryAttemptCurrent, retryAttempt ?? 0, timeout ?? 0, timeoutStep ?? 0);

                    if (ex is TimeoutException)
                    {
                        string msg =
                            $"The operation has timed out! Retrying attempt left: {retryAttemptCurrent}/{retryAttempt}";
                        Logger.PushLogWarning(null, msg);
                    }
                    else
                    {
                        string msg =
                            $"The operation has thrown an exception! Retrying attempt left: {retryAttemptCurrent}/{retryAttempt}\r\n{ex}";
                        Logger.PushLogError(null, msg);
                    }

                    retryAttemptCurrent++;
                    timeout += timeoutStep;
                }
                finally
                {
                    innerCancellationToken?.Dispose();
                    consolidatedToken?.Dispose();
                }
            }

            if (lastException != null
                && !fromToken.IsCancellationRequested)
            {
                throw lastException is TaskCanceledException
                    ? new TimeoutException("The operation has timed out with inner exception!", lastException)
                    : lastException;
            }

            throw new TimeoutException("The operation has timed out!");
        }
    }
}