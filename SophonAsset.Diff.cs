// ReSharper disable IdentifierTypo

#if !NET6_0_OR_GREATER
using System.Threading.Tasks.Dataflow;
#endif
using Hi3Helper.Sophon.Helper;
using Hi3Helper.Sophon.Structs;
using System;
using System.Buffers;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using TaskExtensions = Hi3Helper.Sophon.Helper.TaskExtensions;
using ZstdStream = ZstdNet.DecompressionStream;
// ReSharper disable InvalidXmlDocComment

namespace Hi3Helper.Sophon
{
    public partial class SophonAsset
    {
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
        /// <param name="readInfoDelegate">
        ///     <inheritdoc cref="DelegateReadStreamInfo" />
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
            DownloadDiffChunksAsync(HttpClient                    client,
                                    string                        chunkDirOutput,
                                    ParallelOptions               parallelOptions          = null,
                                    DelegateReadStreamInfo        readInfoDelegate         = null,
                                    DelegateDownloadAssetComplete downloadCompleteDelegate = null)
        {
            this.EnsureOrThrowChunksState();
            this.EnsureOrThrowOutputDirectoryExistence(chunkDirOutput);

            if (parallelOptions == null)
            {
                parallelOptions = new ParallelOptions
                {
                    CancellationToken      = default,
                    MaxDegreeOfParallelism = Environment.ProcessorCount
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
                                                                     readInfoDelegate);
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
                                                                             readInfoDelegate);
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
            PerformWriteDiffChunksThreadAsync(HttpClient             client,
                                              string                 chunkDirOutput,
                                              SophonChunk            chunk,
                                              CancellationToken      token            = default,
                                              DelegateReadStreamInfo readInfoDelegate = null)
        {
            string chunkNameHashed     = chunk.GetChunkStagingFilenameHash(this);
            string chunkFilePathHashed = Path.Combine(chunkDirOutput, chunkNameHashed);

            using (FileStream fileStream = new FileStream(chunkFilePathHashed, FileMode.OpenOrCreate,
                                                          FileAccess.ReadWrite, FileShare.ReadWrite))
            {
                bool isChunkUnmatch = fileStream.Length != chunk.ChunkSize;
                if (!isChunkUnmatch)
                {
                    isChunkUnmatch = !(chunk.TryGetChunkXxh64Hash(out byte[] hash)
                                       && await chunk.CheckChunkXxh64HashAsync(this, fileStream, hash, true, token));
                }

                if (!isChunkUnmatch)
                {
                    this.PushLogDebug($"Skipping chunk 0x{chunk.ChunkOffset:x8} -> L: 0x{chunk.ChunkSizeDecompressed:x8} for: {AssetName}");
                    readInfoDelegate?.Invoke(chunk.ChunkSize);
                    return;
                }

                fileStream.Position = 0;
                await InnerWriteChunkCopyAsync(client, fileStream, chunk, token, readInfoDelegate);
            }
        }

        private async
        #if NET6_0_OR_GREATER
            ValueTask
        #else
            Task
        #endif
            InnerWriteChunkCopyAsync(HttpClient             client,
                                     Stream                 outStream,
                                     SophonChunk            chunk,
                                     CancellationToken      token,
                                     DelegateReadStreamInfo readInfoDelegate = null)
        {
            const int retryCount   = TaskExtensions.DefaultRetryAttempt;
            int       currentRetry = 0;

            long currentWriteOffset = 0;

            string url = SophonChunksInfo.ChunksBaseUrl.TrimEnd('/') + '/' + chunk.ChunkName;

        #if !NOSTREAMLOCK
            if (outStream is FileStream fs)
            {
                fs.Lock(chunk.ChunkOffset, chunk.ChunkSizeDecompressed);
                this.PushLogDebug($"Locked data stream from pos: 0x{chunk.ChunkOffset:x8} -> L: 0x{chunk.ChunkSizeDecompressed:x8} for chunk: {chunk.ChunkName} by asset: {AssetName}");
            }
        #endif

            while (true)
            {
                bool   allowDispose       = false;
                Stream httpResponseStream = null;
                Stream sourceStream       = null;

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
                        this.PushLogDebug($"Init. by offset: 0x{chunk.ChunkOffset:x8} -> L: 0x{chunk.ChunkSizeDecompressed:x8} for chunk: {chunk.ChunkName}");

                    #endif
                        outStream.SetLength(chunk.ChunkSize);
                        outStream.Position = 0;
                        httpResponseStream =
                            await SophonAssetStream.CreateStreamAsync(client, url, 0, null, cooperatedToken.Token);

                        sourceStream = httpResponseStream ??
                                       throw new HttpRequestException("Response stream returns an empty stream!");
                    #if DEBUG
                        this.PushLogDebug($"[Complete init.] by offset: 0x{chunk.ChunkOffset:x8} -> L: 0x{chunk.ChunkSizeDecompressed:x8} for chunk: {chunk.ChunkName}");
                    #endif

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
                            readInfoDelegate?.Invoke(read);

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
                        }

                        outStream.Position = 0;
                        Stream checkHashStream = outStream;

                        bool isHashVerified = true;
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
                            readInfoDelegate?.Invoke(-chunk.ChunkSizeDecompressed);
                            this.PushLogWarning($"Output data seems to be corrupted at transport.\r\nRestarting download for chunk: {chunk.ChunkName} | 0x{chunk.ChunkOffset:x8} -> L: 0x{chunk.ChunkSizeDecompressed:x8} for: {AssetName}");
                            continue;
                        }

                        this.PushLogDebug($"Download completed! Chunk: {chunk.ChunkName} | 0x{chunk.ChunkOffset:x8} -> L: 0x{chunk.ChunkSizeDecompressed:x8} for: {AssetName}");
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
                            readInfoDelegate?.Invoke(-currentWriteOffset);
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

                        ArrayPool<byte>.Shared.Return(buffer);
                    }
                }
            }
        }
    }
}