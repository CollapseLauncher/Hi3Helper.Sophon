﻿using Hi3Helper.Sophon.Helper;
using Hi3Helper.Sophon.Infos;
using Hi3Helper.Sophon.Structs;
using System;
using System.Buffers;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
#if !NET6_0_OR_GREATER
using System.Threading.Tasks.Dataflow;
#endif
using TaskExtensions = Hi3Helper.Sophon.Helper.TaskExtensions;
using ZstdStream = ZstdNet.DecompressionStream;

// ReSharper disable ConvertIfStatementToNullCoalescingAssignment
// ReSharper disable UseAwaitUsing
// ReSharper disable InvalidXmlDocComment
// ReSharper disable IdentifierTypo

namespace Hi3Helper.Sophon
{
    public partial class SophonAsset
    {
        private enum SourceStreamType
        {
            Internet,
            CachedLocal,
            OldReference
        }

        internal const int BufferSize     = 256 << 10;
        private const  int ZstdBufferSize = 0; // Default

        public string        AssetName   { get; internal set; }
        public long          AssetSize   { get; internal set; }
        public string        AssetHash   { get; internal set; }
        public bool          IsDirectory { get; internal set; }
        public bool          IsHasPatch  { get; internal set; }
        public SophonChunk[] Chunks      { get; internal set; }

        internal SophonChunksInfo SophonChunksInfo;

        /// <summary>
        ///     Perform a download process by file and run each chunk download sequentially.
        /// </summary>
        /// <param name="client">
        ///     The <see cref="HttpClient" /> to be used for downloading process.<br />Ensure that the maximum connection for the
        ///     <see cref="HttpClient" /> has been set to at least (Number of Threads/CPU core * 25%) or == Number of Threads/CPU
        ///     core
        /// </param>
        /// <param name="outStream">
        ///     Output <see cref="Stream" /> to write the file into.<br />
        ///     The <see cref="Stream" /> must be readable, writeable and seekable, also be able to be shared both on Read and
        ///     Write operation.
        ///     It's recommended to use <see cref="FileStream" /> or other stream similar to that and please use
        ///     <see cref="FileMode.OpenOrCreate" />,
        ///     <see cref="FileAccess.ReadWrite" /> and <see cref="FileShare.ReadWrite" /> if you're using
        ///     <see cref="FileStream" />.
        /// </param>
        /// <param name="writeInfoDelegate">
        ///     <inheritdoc cref="DelegateWriteStreamInfo" />
        /// </param>
        /// <param name="downloadInfoDelegate">
        ///     <inheritdoc cref="DelegateWriteStreamInfo" />
        /// </param>
        /// <param name="downloadCompleteDelegate">
        ///     <inheritdoc cref="DelegateDownloadAssetComplete" />
        /// </param>
        /// <param name="token">
        ///     Cancellation token for handling cancellation while the routine is running.
        /// </param>
        public async
        #if NET6_0_OR_GREATER
            ValueTask
        #else
            Task
        #endif
            WriteToStreamAsync(HttpClient                    client,
                               Stream                        outStream,
                               DelegateWriteStreamInfo       writeInfoDelegate        = null,
                               DelegateWriteStreamInfo       downloadInfoDelegate     = null,
                               DelegateDownloadAssetComplete downloadCompleteDelegate = null,
                               CancellationToken             token                    = default)
        {
            this.EnsureOrThrowChunksState();
            this.EnsureOrThrowStreamState(outStream);

            if (outStream.Length > AssetSize)
            {
                outStream.SetLength(AssetSize);
            }

            foreach (SophonChunk chunk in Chunks)
            {
                await PerformWriteStreamThreadAsync(client, null, SourceStreamType.Internet, outStream, chunk, token,
                                                    writeInfoDelegate, downloadInfoDelegate);
            }

            this.PushLogInfo($"Asset: {AssetName} | (Hash: {AssetHash} -> {AssetSize} bytes) has been completely downloaded!");
            downloadCompleteDelegate?.Invoke(this);
        }

        /// <summary>
        ///     Perform a download process by file and run each chunk download in parallel instead of sequentially.
        /// </summary>
        /// <param name="client">
        ///     The <see cref="HttpClient" /> to be used for downloading process.<br />Ensure that the maximum connection for the
        ///     <see cref="HttpClient" /> has been set to at least (Number of Threads/CPU core * 25%) or == Number of Threads/CPU
        ///     core
        /// </param>
        /// <param name="outStreamFunc">
        ///     Output <see cref="Stream" /> to write the file into.<br />
        ///     The <see cref="Stream" /> must be readable, writeable and seekable, also be able to be shared both on Read and
        ///     Write operation.
        ///     It's recommended to use <see cref="FileStream" /> or other stream similar to that and please use
        ///     <see cref="FileMode.OpenOrCreate" />,
        ///     <see cref="FileAccess.ReadWrite" /> and <see cref="FileShare.ReadWrite" /> if you're using
        ///     <see cref="FileStream" />.
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
        ///     <inheritdoc cref="DelegateWriteStreamInfo" />
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
            WriteToStreamAsync(HttpClient                    client,
                               Func<Stream>                  outStreamFunc,
                               ParallelOptions               parallelOptions          = null,
                               DelegateWriteStreamInfo       writeInfoDelegate        = null,
                               DelegateWriteStreamInfo       downloadInfoDelegate     = null,
                               DelegateDownloadAssetComplete downloadCompleteDelegate = null)
        {
            this.EnsureOrThrowChunksState();

            using (Stream initStream = outStreamFunc())
            {
                this.EnsureOrThrowStreamState(initStream);

                if (initStream.Length > AssetSize)
                {
                    initStream.SetLength(AssetSize);
                }
            }

            if (parallelOptions == null)
            {
                int maxChunksTask = Math.Min(8, Environment.ProcessorCount);
                parallelOptions = new ParallelOptions
                {
                    CancellationToken       = default,
                    MaxDegreeOfParallelism  = maxChunksTask
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
                             using (Stream outStream = outStreamFunc())
                             {
                                 await PerformWriteStreamThreadAsync(client,    null,  SourceStreamType.Internet,
                                                                     outStream, chunk, linkedToken.Token,
                                                                     writeInfoDelegate, downloadInfoDelegate);
                             }
                         },
                         new ExecutionDataflowBlockOptions
                         {
                             MaxDegreeOfParallelism = parallelOptions.MaxDegreeOfParallelism,
                             CancellationToken = linkedToken.Token
                         });

                        foreach (SophonChunk chunk in Chunks)
                        {
                            await actionBlock.SendAsync(chunk, linkedToken.Token);
                        }

                        actionBlock.Complete();
                        await actionBlock.Completion;
                    }
                }
            #else
                await Parallel.ForEachAsync(Chunks, parallelOptions, async (chunk, threadToken) =>
                                                                     {
                                                                         await using Stream outStream = outStreamFunc();
                                                                         await PerformWriteStreamThreadAsync(client,
                                                                                  null,      SourceStreamType.Internet,
                                                                                  outStream, chunk, threadToken,
                                                                                  writeInfoDelegate, downloadInfoDelegate);
                                                                     });
            #endif
            }
            catch (AggregateException ex)
            {
                // Throw all other exceptions
                throw ex.Flatten().InnerExceptions.First();
            }

            this.PushLogInfo($"Asset: {AssetName} | (Hash: {AssetHash} -> {AssetSize} bytes) has been completely downloaded!");
            downloadCompleteDelegate?.Invoke(this);
        }

        private async
        #if NET6_0_OR_GREATER
            ValueTask
        #else
            Task
        #endif
            PerformWriteStreamThreadAsync(HttpClient                    client,
                                          Stream                        sourceStream,
                                          SourceStreamType              sourceStreamType,
                                          Stream                        outStream,
                                          SophonChunk                   chunk,
                                          CancellationToken             token                   = default,
                                          DelegateWriteStreamInfo       writeInfoDelegate       = null,
                                          DelegateWriteStreamInfo       downloadInfoDelegate    = null)
        {
            long totalSizeFromOffset = chunk.ChunkOffset + chunk.ChunkSizeDecompressed;
            bool isSkipChunk         = outStream.Length >= totalSizeFromOffset;

            if (isSkipChunk)
            {
                outStream.Position = chunk.ChunkOffset;
                isSkipChunk        = await chunk.CheckChunkMd5HashAsync(outStream, false, token);
            }

            if (isSkipChunk)
            {
                this.PushLogDebug($"Skipping chunk 0x{chunk.ChunkOffset:x8} -> L: 0x{chunk.ChunkSizeDecompressed:x8} for: {AssetName}");
                writeInfoDelegate?.Invoke(chunk.ChunkSizeDecompressed);
                return;
            }

            await InnerWriteStreamToAsync(client, sourceStream, sourceStreamType, outStream, chunk, token,
                                          writeInfoDelegate);
        }

        private async
        #if NET6_0_OR_GREATER
            ValueTask
        #else
            Task
        #endif
            InnerWriteStreamToAsync(HttpClient                  client,
                                    Stream                      sourceStream,
                                    SourceStreamType            sourceStreamType,
                                    Stream                      outStream,
                                    SophonChunk                 chunk,
                                    CancellationToken           token,
                                    DelegateWriteStreamInfo     writeInfoDelegate       = null,
                                    DelegateWriteStreamInfo     downloadInfoDelegate    = null)
        {
            if (sourceStreamType != SourceStreamType.Internet && sourceStream == null)
            {
                throw new ArgumentNullException(nameof(sourceStream),
                                                "Source stream cannot be null under OldReference or CachedLocal mode!");
            }

            if (sourceStreamType == SourceStreamType.OldReference && chunk.ChunkOldOffset < 0)
            {
                throw new
                    InvalidOperationException("SourceStreamType.OldReference cannot be used if chunk does not have chunk old offset reference!");
            }

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

                MD5    hashInstance = MD5.Create();
                byte[] buffer       = ArrayPool<byte>.Shared.Rent(BufferSize);

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
                        outStream.Position = chunk.ChunkOffset;

                        switch (sourceStreamType)
                        {
                            case SourceStreamType.Internet:
                            {
                                httpResponseStream =
                                    await SophonAssetStream.CreateStreamAsync(client, url, 0, null,
                                                                              cooperatedToken.Token) ??
                                    throw new HttpRequestException("Response stream returns an empty stream!");
                                if (SophonChunksInfo.IsUseCompression)
                                {
                                    sourceStream = new ZstdStream(httpResponseStream, ZstdBufferSize);
                                }
                            }
                                break;
                            case SourceStreamType.CachedLocal:
                            {
                                if (SophonChunksInfo.IsUseCompression)
                                {
                                    sourceStream = new ZstdStream(sourceStream, ZstdBufferSize);
                                }
                            }
                                break;
                            case SourceStreamType.OldReference:
                            {
                                sourceStream.Position = chunk.ChunkOldOffset;
                            }
                                break;
                            default:
                                throw new ArgumentOutOfRangeException(nameof(sourceStreamType), sourceStreamType, null);
                        }
                    #if DEBUG
                        this.PushLogDebug($"[Complete init.] by offset: 0x{chunk.ChunkOffset:x8} -> L: 0x{chunk.ChunkSizeDecompressed:x8} for chunk: {chunk.ChunkName}");
                    #endif

                        long remain = chunk.ChunkSizeDecompressed;
                        while (remain > 0)
                        {
                            int toRead = Math.Min((int)remain, buffer.Length);

                            int read = await sourceStream.ReadAsync(buffer, 0, toRead, cooperatedToken.Token);
                            await outStream.WriteAsync(buffer, 0, read, cooperatedToken.Token);
                            currentWriteOffset += read;
                            remain             -= read;
                            hashInstance.TransformBlock(buffer, 0, read, buffer, 0);
                            writeInfoDelegate?.Invoke(read);

                            // Add network activity read indicator
                            if (sourceStreamType == SourceStreamType.Internet)
                                downloadInfoDelegate?.Invoke(read);

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
                            if (read == 0 && remain > 0)
                            {
                                throw new
                                    InvalidDataException($"Chunk has remained data while the read is already 0 due to corrupted compressed data. Remained data: {remain} bytes");
                            }
                        }

                        hashInstance.TransformFinalBlock(buffer, 0, 0);
                        bool isHashVerified = hashInstance.Hash.AsSpan().SequenceEqual(chunk.ChunkHashDecompressed);

                        if (!isHashVerified)
                        {
                            writeInfoDelegate?.Invoke(-chunk.ChunkSizeDecompressed);
                            if (sourceStreamType == SourceStreamType.Internet)
                                downloadInfoDelegate?.Invoke(-chunk.ChunkSizeDecompressed);

                            allowDispose = true;
                            this.PushLogWarning($"Output data seems to be corrupted at transport.\r\nRestarting download for chunk: {chunk.ChunkName} | 0x{chunk.ChunkOffset:x8} -> L: 0x{chunk.ChunkSizeDecompressed:x8} for: {AssetName}");
                            continue;
                        }

                        this.PushLogDebug($"Download completed! Chunk: {chunk.ChunkName} | 0x{chunk.ChunkOffset:x8} -> L: 0x{chunk.ChunkSizeDecompressed:x8} for: {AssetName}");
                        allowDispose = true;
                        return;
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                        allowDispose = true;
                        throw;
                    }
                    catch (Exception ex)
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
                        sourceStream     = null;
                        SourceStreamType lastSourceStreamType = sourceStreamType;
                        sourceStreamType = SourceStreamType.Internet;

                        if (currentRetry < retryCount)
                        {
                            writeInfoDelegate?.Invoke(-currentWriteOffset);
                            if (lastSourceStreamType == SourceStreamType.Internet)
                                downloadInfoDelegate?.Invoke(-currentWriteOffset);

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