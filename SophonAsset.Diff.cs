﻿using Hi3Helper.Sophon.Helper;
using Hi3Helper.Sophon.Structs;
using System;
using System.Buffers;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
#if !NET6_0_OR_GREATER
using System.Threading.Tasks.Dataflow;
#endif
using TaskExtensions = Hi3Helper.Sophon.Helper.TaskExtensions;
using ZstdStream = ZstdNet.DecompressionStream;

// ReSharper disable InvalidXmlDocComment
// ReSharper disable IdentifierTypo

namespace Hi3Helper.Sophon
{
    public partial class SophonAsset
    {
        private int _countChunksDownload;
        private int _currentChunksDownloadPos;
        private int _currentChunksDownloadQueue;

        /// <summary>
        ///     Perform a download for staged chunks used as a new data or data diff. for preload and update.
        /// </summary>
        /// <param name="client">
        ///     The <see cref="HttpClient" /> to be used for downloading process.<br />Ensure that the maximum connection for the
        ///     <see cref="HttpClient" /> has been set to at least (Number of Threads/CPU core * 25%) or == Number of Threads/CPU
        ///     core
        /// </param>
        /// <param name="chunkDirOutput">
        ///     The directory of the staged chunk.
        /// </param>
        /// <param name="parallelOptions">
        ///     Parallelization settings to be used for downloading chunks and data hashing.
        ///     Remember that while using this method, the <seealso cref="CancellationToken" /> needs to be passed with
        ///     <c>CancellationToken</c> property.<br />
        ///     If it's being set to <c>null</c>, a default setting will be used as below:
        ///     <code>
        ///     CancellationToken = <paramref name="token" />,
        ///     MaxDegreeOfParallelism = [Number of CPU threads/cores available]
        ///     </code>
        /// </param>
        /// <param name="writeInfoDelegate">
        ///     <inheritdoc cref="DelegateWriteStreamInfo" />
        /// </param>
        /// <param name="downloadInfoDelegate">
        ///     <inheritdoc cref="DelegateWriteDownloadInfo" />
        /// </param>
        /// <param name="downloadCompleteDelegate">
        ///     <inheritdoc cref="DelegateDownloadAssetComplete" />
        /// </param>
        public async
#if NET6_0_OR_GREATER
            ValueTask
#else
            Task
#endif
            DownloadDiffChunksAsync(HttpClient                      client,
                                    string                          chunkDirOutput,
                                    ParallelOptions                 parallelOptions             = null,
                                    DelegateWriteStreamInfo         writeInfoDelegate           = null,
                                    DelegateWriteDownloadInfo       downloadInfoDelegate        = null,
                                    DelegateDownloadAssetComplete   downloadCompleteDelegate    = null,
                                    bool                            forceVerification           = false)
        {
            this.EnsureOrThrowChunksState();
            this.EnsureOrThrowOutputDirectoryExistence(chunkDirOutput);

            _currentChunksDownloadPos = 0;
            _countChunksDownload = Chunks.Length;

            if (parallelOptions == null)
            {
                int maxChunksTask = Math.Min(8, Environment.ProcessorCount);
                parallelOptions = new ParallelOptions
                {
                    CancellationToken = default,
                    MaxDegreeOfParallelism = maxChunksTask
                };
            }

            try
            {
#if !NET6_0_OR_GREATER
                using (CancellationTokenSource actionToken = new CancellationTokenSource())
                {
                    using (CancellationTokenSource linkedToken = CancellationTokenSource
                              .CreateLinkedTokenSource(actionToken.Token, parallelOptions.CancellationToken))
                    {
                        ActionBlock<SophonChunk> actionBlock = new ActionBlock<SophonChunk>(
                         async chunk =>
                         {
                             await PerformWriteDiffChunksThreadAsync(client,
                                                                     chunkDirOutput, chunk, linkedToken.Token,
                                                                     writeInfoDelegate, downloadInfoDelegate, forceVerification);
                         },
                         new ExecutionDataflowBlockOptions
                         {
                             MaxDegreeOfParallelism = parallelOptions.MaxDegreeOfParallelism,
                             CancellationToken      = linkedToken.Token
                         });

                        foreach (SophonChunk chunk in Chunks)
                        {
                            if (chunk.ChunkOldOffset > -1)
                            {
                                return;
                            }

                            await actionBlock.SendAsync(chunk, linkedToken.Token);
                        }

                        actionBlock.Complete();
                        await actionBlock.Completion;
                    }
                }
#else
                await Parallel.ForEachAsync(Chunks, parallelOptions, async (chunk, threadToken) =>
                                                                     {
                                                                         if (chunk.ChunkOldOffset > -1)
                                                                         {
                                                                             return;
                                                                         }

                                                                         await PerformWriteDiffChunksThreadAsync(client,
                                                                             chunkDirOutput, chunk, threadToken,
                                                                             writeInfoDelegate, downloadInfoDelegate, DownloadSpeedLimiter,
                                                                             forceVerification);
                                                                     });
#endif
            }
            catch (AggregateException ex)
            {
                throw ex.Flatten().InnerExceptions.First();
            }
            // Throw all other exceptions

            this.PushLogInfo($"Asset: {AssetName} | (Hash: {AssetHash} -> {AssetSize} bytes) has been completely downloaded!");
            downloadCompleteDelegate?.Invoke(this);
        }

        private async
#if NET6_0_OR_GREATER
            ValueTask
#else
            Task
#endif
            PerformWriteDiffChunksThreadAsync(HttpClient                 client,
                                              string                     chunkDirOutput,
                                              SophonChunk                chunk,
                                              CancellationToken          token,
                                              DelegateWriteStreamInfo    writeInfoDelegate,
                                              DelegateWriteDownloadInfo  downloadInfoDelegate,
                                              SophonDownloadSpeedLimiter downloadSpeedLimiter,
                                              bool                       forceVerification)
        {
            string chunkNameHashed = chunk.GetChunkStagingFilenameHash(this);
            string chunkFilePathHashed = Path.Combine(chunkDirOutput, chunkNameHashed);
            string chunkFileCheckedPath = chunkFilePathHashed + ".verified";

            try
            {
                Interlocked.Increment(ref _currentChunksDownloadPos);
                Interlocked.Increment(ref _currentChunksDownloadQueue);
                using (FileStream fileStream = new FileStream(chunkFilePathHashed, FileMode.OpenOrCreate,
                                                              FileAccess.ReadWrite, FileShare.ReadWrite))
                {
                    bool isChunkUnmatch = fileStream.Length != chunk.ChunkSize;
                    bool isChunkVerified = File.Exists(chunkFileCheckedPath) && !isChunkUnmatch;
                    if (forceVerification || !isChunkVerified)
                    {
                        isChunkUnmatch = !(chunk.TryGetChunkXxh64Hash(out byte[] hash)
                                           && await chunk.CheckChunkXxh64HashAsync(this, fileStream, hash, true, token));
                        if (File.Exists(chunkFileCheckedPath))
                            File.Delete(chunkFileCheckedPath);
                    }

                    if (!isChunkUnmatch)
                    {
                        this.PushLogDebug($"[{_currentChunksDownloadPos}/{_countChunksDownload} Queue: {_currentChunksDownloadQueue}] Skipping chunk 0x{chunk.ChunkOffset:x8} -> L: 0x{chunk.ChunkSizeDecompressed:x8} for: {AssetName}");
                        writeInfoDelegate?.Invoke(chunk.ChunkSize);
                        downloadInfoDelegate?.Invoke(chunk.ChunkSize, 0);
                        if (!File.Exists(chunkFileCheckedPath))
                            File.Create(chunkFileCheckedPath).Dispose();
                        return;
                    }

                    fileStream.Position = 0;
                    await InnerWriteChunkCopyAsync(client, fileStream, chunk, token, writeInfoDelegate, downloadInfoDelegate, downloadSpeedLimiter);
                    File.Create(chunkFileCheckedPath).Dispose();
                }
            }
            finally
            {
                Interlocked.Decrement(ref _currentChunksDownloadQueue);
            }
        }

        private async
#if NET6_0_OR_GREATER
            ValueTask
#else
            Task
#endif
            InnerWriteChunkCopyAsync(HttpClient                 client,
                                     Stream                     outStream,
                                     SophonChunk                chunk,
                                     CancellationToken          token,
                                     DelegateWriteStreamInfo    writeInfoDelegate,
                                     DelegateWriteDownloadInfo  downloadInfoDelegate,
                                     SophonDownloadSpeedLimiter downloadSpeedLimiter)
        {
            const int retryCount = TaskExtensions.DefaultRetryAttempt;
            int currentRetry = 0;

            long currentWriteOffset = 0;

            string url = SophonChunksInfo.ChunksBaseUrl.TrimEnd('/') + '/' + chunk.ChunkName;

#if !NOSTREAMLOCK
            if (outStream is FileStream fs)
            {
                fs.Lock(chunk.ChunkOffset, chunk.ChunkSizeDecompressed);
                this.PushLogDebug($"Locked data stream from pos: 0x{chunk.ChunkOffset:x8} -> L: 0x{chunk.ChunkSizeDecompressed:x8} for chunk: {chunk.ChunkName} by asset: {AssetName}");
            }
#endif

            long      written                       = 0;
            long      thisInstanceDownloadLimitBase = downloadSpeedLimiter?.InitialRequestedSpeed ?? -1;
            Stopwatch currentStopwatch              = Stopwatch.StartNew();

            double maximumBytesPerSecond;
            double bitPerUnit;

            CalculateBps();

            if (downloadSpeedLimiter != null)
            {
                downloadSpeedLimiter.CurrentChunkProcessingChangedEvent += UpdateChunkRangesCountEvent;
                downloadSpeedLimiter.DownloadSpeedChangedEvent += DownloadClient_DownloadSpeedLimitChanged;
            }

            while (true)
            {
                bool allowDispose = false;
                Stream httpResponseStream = null;
                Stream sourceStream = null;

                byte[] buffer = ArrayPool<byte>.Shared.Rent(BufferSize);

                {
                    try
                    {
                        CancellationTokenSource innerTimeoutToken =
                            new CancellationTokenSource(TimeSpan.FromSeconds(TaskExtensions.DefaultTimeoutSec)
#if NET8_0_OR_GREATER
                                                      , TimeProvider.System
#endif
                                                       );
                        CancellationTokenSource cooperatedToken =
                            CancellationTokenSource.CreateLinkedTokenSource(token, innerTimeoutToken.Token);

#if DEBUG
                        this.PushLogDebug($"[{_currentChunksDownloadPos}/{_countChunksDownload} Queue: {_currentChunksDownloadQueue}] Init. by offset: 0x{chunk.ChunkOffset:x8} -> L: 0x{chunk.ChunkSizeDecompressed:x8} for chunk: {chunk.ChunkName}");

#endif
                        outStream.SetLength(chunk.ChunkSize);
                        outStream.Position = 0;
                        httpResponseStream =
                            await SophonAssetStream.CreateStreamAsync(client, url, 0, null, cooperatedToken.Token);

                        sourceStream = httpResponseStream ??
                                       throw new HttpRequestException("Response stream returns an empty stream!");
#if DEBUG
                        this.PushLogDebug($"[{_currentChunksDownloadPos}/{_countChunksDownload} Queue: {_currentChunksDownloadQueue}] [Complete init.] by offset: 0x{chunk.ChunkOffset:x8} -> L: 0x{chunk.ChunkSizeDecompressed:x8} for chunk: {chunk.ChunkName}");
#endif

                        downloadSpeedLimiter?.IncrementChunkProcessedCount();
                        int read;
                        while ((read = await sourceStream.ReadAsync(
#if NET6_0_OR_GREATER
                                                                    buffer
#else
                                                                    buffer, 0, buffer.Length
#endif
                                                                  , cooperatedToken.Token)) >
                               0)
                        {
                            await outStream.WriteAsync(buffer, 0, read, cooperatedToken.Token);
                            currentWriteOffset += read;
                            writeInfoDelegate?.Invoke(read);
                            downloadInfoDelegate?.Invoke(read, read);
                            written += read;

                            currentRetry = 0;
                            innerTimeoutToken.Dispose();
                            cooperatedToken.Dispose();

                            innerTimeoutToken =
                                new CancellationTokenSource(TimeSpan.FromSeconds(TaskExtensions.DefaultTimeoutSec)
#if NET8_0_OR_GREATER
                                                          , TimeProvider.System
#endif
                                                           );
                            cooperatedToken =
                                CancellationTokenSource.CreateLinkedTokenSource(token, innerTimeoutToken.Token);

                            await ThrottleAsync();
                        }

                        outStream.Position = 0;
                        Stream checkHashStream = outStream;

                        bool isHashVerified;
                        if (chunk.TryGetChunkXxh64Hash(out byte[] outHash))
                        {
                            isHashVerified =
                                await chunk.CheckChunkXxh64HashAsync(this, checkHashStream, outHash, true,
                                                                     cooperatedToken.Token);
                        }
                        else
                        {
                            if (SophonChunksInfo.IsUseCompression)
                            {
                                checkHashStream = new ZstdStream(checkHashStream);
                            }

                            isHashVerified =
                                await chunk.CheckChunkMd5HashAsync(checkHashStream, true, cooperatedToken.Token);
                        }

                        if (!isHashVerified)
                        {
                            writeInfoDelegate?.Invoke(-chunk.ChunkSizeDecompressed);
                            downloadInfoDelegate?.Invoke(-chunk.ChunkSizeDecompressed, 0);
                            this.PushLogWarning($"Output data seems to be corrupted at transport.\r\nRestarting download for chunk: {chunk.ChunkName} | 0x{chunk.ChunkOffset:x8} -> L: 0x{chunk.ChunkSizeDecompressed:x8} for: {AssetName}");
                            continue;
                        }

                        this.PushLogDebug($"[{_currentChunksDownloadPos}/{_countChunksDownload} Queue: {_currentChunksDownloadQueue}] Download completed! Chunk: {chunk.ChunkName} | 0x{chunk.ChunkOffset:x8} -> L: 0x{chunk.ChunkSizeDecompressed:x8} for: {AssetName}");
                        return;
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                        allowDispose = true;
                        throw;
                    }
                    catch (Exception ex)
                    {
                        if (currentRetry < retryCount)
                        {
                            writeInfoDelegate?.Invoke(-currentWriteOffset);
                            downloadInfoDelegate?.Invoke(-currentWriteOffset, 0);
                            currentWriteOffset = 0;
                            currentRetry++;

                            this.PushLogWarning($"An error has occurred while downloading chunk: {chunk.ChunkName} | 0x{chunk.ChunkOffset:x8} -> L: 0x{chunk.ChunkSizeDecompressed:x8} for: {AssetName}\r\n{ex}");
                            await Task.Delay(TimeSpan.FromSeconds(1), token);
                            continue;
                        }

                        allowDispose = true;
                        this.PushLogError($"An unhandled error has occurred while downloading chunk: {chunk.ChunkName} | 0x{chunk.ChunkOffset:x8} -> L: 0x{chunk.ChunkSizeDecompressed:x8} for: {AssetName}\r\n{ex}");
                        throw;
                    }
                    finally
                    {
                        if (allowDispose)
                        {
#if NET6_0_OR_GREATER
                            if (httpResponseStream != null)
                            {
                                await httpResponseStream.DisposeAsync();
                            }

                            if (sourceStream != null)
                            {
                                await sourceStream.DisposeAsync();
                            }
#else
                            sourceStream?.Dispose();
                            httpResponseStream?.Dispose();
#endif
                        }

                        downloadSpeedLimiter?.DecrementChunkProcessedCount();
                        ArrayPool<byte>.Shared.Return(buffer);
                    }
                }
            }

            void CalculateBps()
            {
                if (thisInstanceDownloadLimitBase <= 0)
                    thisInstanceDownloadLimitBase = -1;
                else
                    thisInstanceDownloadLimitBase = Math.Max(64 << 10, thisInstanceDownloadLimitBase);

                double threadNum = Math.Clamp(downloadSpeedLimiter?.CurrentChunkProcessing ?? 1, 1, 16 << 10);
                maximumBytesPerSecond = thisInstanceDownloadLimitBase / threadNum;
                bitPerUnit = 940 - (threadNum - 2) / (16 - 2) * 400;
            }

            void DownloadClient_DownloadSpeedLimitChanged(object sender, long e)
            {
                thisInstanceDownloadLimitBase = e == 0 ? -1 : e;
                CalculateBps();
            }

            void UpdateChunkRangesCountEvent(object sender, int e)
            {
                CalculateBps();
            }

            async Task ThrottleAsync()
            {
                // Make sure the buffer isn't empty.
                if (maximumBytesPerSecond <= 0 || written <= 0)
                {
                    return;
                }

                long elapsedMilliseconds = currentStopwatch.ElapsedMilliseconds;

                if (elapsedMilliseconds > 0)
                {
                    // Calculate the current bps.
                    double bps = written * bitPerUnit / elapsedMilliseconds;

                    // If the bps are more then the maximum bps, try to throttle.
                    if (bps > maximumBytesPerSecond)
                    {
                        // Calculate the time to sleep.
                        double wakeElapsed = written * bitPerUnit / maximumBytesPerSecond;
                        double toSleep = wakeElapsed - elapsedMilliseconds;

                        if (toSleep > 1)
                        {
                            // The time to sleep is more than a millisecond, so sleep.
                            await Task.Delay(TimeSpan.FromMilliseconds(toSleep), token);

                            // A sleep has been done, reset.
                            currentStopwatch.Restart();

                            written = 0;
                        }
                    }
                }
            }
        }
    }
}