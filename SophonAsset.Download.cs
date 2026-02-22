using Hi3Helper.Sophon.Helper;
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

// ReSharper disable AccessToModifiedClosure
// ReSharper disable ConvertIfStatementToNullCoalescingAssignment
// ReSharper disable UseAwaitUsing
// ReSharper disable InvalidXmlDocComment
// ReSharper disable IdentifierTypo
// ReSharper disable UnusedAutoPropertyAccessor.Global

using TaskExtensions = Hi3Helper.Sophon.Helper.TaskExtensions;
using ZstdStream = ZstdNet.DecompressionStream;

namespace Hi3Helper.Sophon
{
    public partial class SophonAsset : SophonIdentifiableProperty
    {
        internal SophonAsset(string assetName, bool isDirectory)
            : this(assetName,
                   0,
                   null,
                   isDirectory,
                   false,
                   []) { }

        internal SophonAsset(string        assetName,
                             long          assetSize,
                             string        assetHash,
                             bool          isDirectory,
                             bool          isHasPatch,
                             SophonChunk[] chunks)
        {
            AssetName   = assetName;
            AssetSize   = assetSize;
            AssetHash   = assetHash;
            IsDirectory = isDirectory;
            IsHasPatch  = isHasPatch;
            Chunks      = chunks;
        }

        private enum SourceStreamType
        {
            Internet,
            CachedLocal,
            OldReference
        }

        internal const int BufferSize     = 4 << 10;
        private const  int ZstdBufferSize = 0; // Default

        public   string                     AssetName            { get; }
        public   long                       AssetSize            { get; }
        public   string                     AssetHash            { get; }
        public   bool                       IsDirectory          { get; }
        public   bool                       IsHasPatch           { get; }
        public   SophonChunk[]              Chunks               { get; }
        internal SophonDownloadSpeedLimiter DownloadSpeedLimiter { get; set; }
        internal SophonChunksInfo           SophonChunksInfo     { get; set; }
        internal SophonChunksInfo           SophonChunksInfoAlt  { get; set; }

        public override string ToString() => AssetName;

        public override int GetHashCode()
        {
#if NET6_0_OR_GREATER
            return HashCode.Combine(IsHasPatch, AssetName, AssetHash);
#else
            return IsHasPatch.GetHashCode() ^
                   (AssetName?.GetHashCode() ?? 0)
                   (AssetHash?.GetHashCode() ?? 0);
#endif
        }

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
        ///     <inheritdoc cref="DelegateWriteDownloadInfo" />
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
                               DelegateWriteDownloadInfo     downloadInfoDelegate     = null,
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
                await PerformWriteStreamThreadAsync(client,
                                                    null,
                                                    SourceStreamType.Internet,
                                                    outStream,
                                                    chunk,
                                                    writeInfoDelegate,
                                                    downloadInfoDelegate,
                                                    DownloadSpeedLimiter,
                                                    token);
            }

#if DEBUG
            this.PushLogInfo($"Asset: {AssetName} | (Hash: {AssetHash} -> {AssetSize} bytes)" +
                " has been completely downloaded!");
#endif
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
            WriteToStreamAsync(HttpClient                    client,
                               Func<Stream>                  outStreamFunc,
                               ParallelOptions               parallelOptions          = null,
                               DelegateWriteStreamInfo       writeInfoDelegate        = null,
                               DelegateWriteDownloadInfo     downloadInfoDelegate     = null,
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
                    CancellationToken      = CancellationToken.None,
                    MaxDegreeOfParallelism = maxChunksTask
                };
            }

            try
            {
#if !NET6_0_OR_GREATER
                using CancellationTokenSource actionToken = new CancellationTokenSource();
                using CancellationTokenSource linkedToken = CancellationTokenSource
                    .CreateLinkedTokenSource(actionToken.Token,
                                             parallelOptions.CancellationToken);
                ActionBlock<SophonChunk> actionBlock = new(
                    // ReSharper disable once AccessToDisposedClosure
                    async chunk => await Impl(chunk, linkedToken.Token),
                    new ExecutionDataflowBlockOptions
                    {
                        MaxDegreeOfParallelism = parallelOptions.MaxDegreeOfParallelism,
                        CancellationToken      = linkedToken.Token
                    });

                foreach (SophonChunk chunk in Chunks)
                {
                    await actionBlock.SendAsync(chunk, linkedToken.Token);
                }

                actionBlock.Complete();
                await actionBlock.Completion;
#else
                await Parallel.ForEachAsync(Chunks,
                                            parallelOptions,
                                            Impl);
#endif
            }
            catch (AggregateException ex)
            {
                // Throw all other exceptions
                throw ex.Flatten().InnerExceptions.First();
            }

#if DEBUG
            this.PushLogInfo($"Asset: {AssetName} | (Hash: {AssetHash} -> {AssetSize} bytes)" +
                $" has been completely downloaded!");
#endif
            downloadCompleteDelegate?.Invoke(this);

            return;

            async ValueTask Impl(SophonChunk chunk, CancellationToken threadToken)
            {
#if NET6_0_OR_GREATER
                await
#endif
                using Stream outStream = outStreamFunc();
                await PerformWriteStreamThreadAsync(client,
                                                    null,
                                                    SourceStreamType.Internet,
                                                    outStream,
                                                    chunk,
                                                    writeInfoDelegate,
                                                    downloadInfoDelegate,
                                                    DownloadSpeedLimiter,
                                                    threadToken);
            }
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
        ///     Output <see cref="Stream" /> to write the file into by passing the pre-allocated size of the file.<br />
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
            WriteToStreamAsync(HttpClient                    client,
                               Func<long, Stream>            outStreamFunc,
                               ParallelOptions               parallelOptions          = null,
                               DelegateWriteStreamInfo       writeInfoDelegate        = null,
                               DelegateWriteDownloadInfo     downloadInfoDelegate     = null,
                               DelegateDownloadAssetComplete downloadCompleteDelegate = null)
        {
            this.EnsureOrThrowChunksState();

            using (Stream initStream = outStreamFunc(AssetSize))
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
                    CancellationToken = CancellationToken.None,
                    MaxDegreeOfParallelism = maxChunksTask
                };
            }

            try
            {
#if !NET6_0_OR_GREATER
                using CancellationTokenSource actionToken = new CancellationTokenSource();
                using CancellationTokenSource linkedToken = CancellationTokenSource
                    .CreateLinkedTokenSource(actionToken.Token,
                                             parallelOptions.CancellationToken);
                ActionBlock<SophonChunk> actionBlock = new(
                    // ReSharper disable once AccessToDisposedClosure
                    async chunk => await Impl(chunk, linkedToken.Token),
                    new ExecutionDataflowBlockOptions
                    {
                        MaxDegreeOfParallelism = parallelOptions.MaxDegreeOfParallelism,
                        CancellationToken      = linkedToken.Token
                    });

                foreach (SophonChunk chunk in Chunks)
                {
                    await actionBlock.SendAsync(chunk, linkedToken.Token);
                }

                actionBlock.Complete();
                await actionBlock.Completion;
#else
                await Parallel.ForEachAsync(Chunks,
                                            parallelOptions,
                                            Impl);
#endif
            }
            catch (AggregateException ex)
            {
                // Throw all other exceptions
                throw ex.Flatten().InnerExceptions.First();
            }

#if DEBUG
            this.PushLogInfo($"Asset: {AssetName} | (Hash: {AssetHash} -> {AssetSize} bytes)" +
                $" has been completely downloaded!");
#endif
            downloadCompleteDelegate?.Invoke(this);

            return;

            async ValueTask Impl(SophonChunk chunk, CancellationToken threadToken)
            {
#if NET6_0_OR_GREATER
                await
#endif
                using Stream outStream = outStreamFunc(AssetSize);
                await PerformWriteStreamThreadAsync(client,
                                                    null,
                                                    SourceStreamType.Internet,
                                                    outStream,
                                                    chunk,
                                                    writeInfoDelegate,
                                                    downloadInfoDelegate,
                                                    DownloadSpeedLimiter,
                                                    threadToken);
            }
        }

        private async
#if NET6_0_OR_GREATER
            ValueTask
#else
            Task
#endif
            PerformWriteStreamThreadAsync(HttpClient                 client,
                                          Stream                     sourceStream,
                                          SourceStreamType           sourceStreamType,
                                          Stream                     outStream,
                                          SophonChunk                chunk,
                                          DelegateWriteStreamInfo    writeInfoDelegate,
                                          DelegateWriteDownloadInfo  downloadInfoDelegate,
                                          SophonDownloadSpeedLimiter downloadSpeedLimiter,
                                          CancellationToken          token)
        {
            long totalSizeFromOffset = chunk.ChunkOffset + chunk.ChunkSizeDecompressed;
            bool isSkipChunk         = outStream.Length >= totalSizeFromOffset;

            if (isSkipChunk)
            {
                outStream.Position = chunk.ChunkOffset;
                isSkipChunk        = await chunk.CheckChunkMd5HashAsync(outStream,
                                                                        false,
                                                                        token);
            }

            if (isSkipChunk)
            {
#if DEBUG
                this.PushLogDebug($"Skipping chunk 0x{chunk.ChunkOffset:x8}" +
                    $" -> L: 0x{chunk.ChunkSizeDecompressed:x8} for: {AssetName}");
#endif
                writeInfoDelegate?.Invoke(chunk.ChunkSizeDecompressed);
                downloadInfoDelegate?.Invoke(chunk.ChunkOldOffset != -1 ?
                                                 0 :
                                                 chunk.ChunkSizeDecompressed,
                                             0);
                return;
            }

            await InnerWriteStreamToAsync(client,
                                          sourceStream,
                                          sourceStreamType,
                                          outStream,
                                          chunk,
                                          writeInfoDelegate,
                                          downloadInfoDelegate,
                                          downloadSpeedLimiter,
                                          token);
        }

        private async
#if NET6_0_OR_GREATER
            ValueTask
#else
            Task
#endif
            InnerWriteStreamToAsync(HttpClient                 client,
                                    Stream                     sourceStream,
                                    SourceStreamType           sourceStreamType,
                                    Stream                     outStream,
                                    SophonChunk                chunk,
                                    DelegateWriteStreamInfo    writeInfoDelegate,
                                    DelegateWriteDownloadInfo  downloadInfoDelegate,
                                    SophonDownloadSpeedLimiter downloadSpeedLimiter,
                                    CancellationToken          token)
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

            const int retryCount         = TaskExtensions.DefaultRetryAttempt;
            int       currentRetry       = 0;
            long      currentWriteOffset = 0;


#if !NOSTREAMLOCK
            if (outStream is FileStream fs)
            {
                fs.Lock(chunk.ChunkOffset, chunk.ChunkSizeDecompressed);
                this.PushLogDebug($"Locked data stream from pos: 0x{chunk.ChunkOffset:x8}" +
                $" -> L: 0x{chunk.ChunkSizeDecompressed:x8} for chunk: {chunk.ChunkName} by asset: {AssetName}");
            }
#endif

            while (true)
            {
                bool                allowDispose        = false;
                HttpResponseMessage httpResponseMessage = null;
                Stream              httpResponseStream  = null;

                using MD5 hashInstance = MD5.Create();
                byte[] buffer = ArrayPool<byte>.Shared.Rent(BufferSize);

                SourceStreamType currentSourceStreamType = sourceStreamType;
                {
                    try
                    {
                        CancellationTokenSource innerTimeoutToken =
                            new(TimeSpan.FromSeconds(TaskExtensions.DefaultTimeoutSec)
                            #if NET8_0_OR_GREATER
                              , TimeProvider.System
                            #endif
                               );
                        CancellationTokenSource cooperatedToken =
                            CancellationTokenSource.CreateLinkedTokenSource(token, innerTimeoutToken.Token);

#if DEBUG
                        this.PushLogDebug($"Init. by offset: 0x{chunk.ChunkOffset:x8}" +
                            $" -> L: 0x{chunk.ChunkSizeDecompressed:x8} for chunk: {chunk.ChunkName}");

#endif
                        outStream.Position = chunk.ChunkOffset;

                        switch (sourceStreamType)
                        {
                            case SourceStreamType.Internet:
                            {
                                httpResponseMessage = await client
                                        .GetChunkAndIfAltAsync(chunk.ChunkName,
                                                               SophonChunksInfo,
                                                               SophonChunksInfoAlt,
                                                               cooperatedToken.Token);

                                    httpResponseStream = await httpResponseMessage
                                                          .EnsureSuccessStatusCode()
                                                          .Content
                                                          .ReadAsStreamAsync(
#if NET6_0_OR_GREATER
                                                                             cooperatedToken.Token
#endif
                                                                            );

                                sourceStream = httpResponseStream;

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
                        this.PushLogDebug($"[Complete init.] by offset: 0x{chunk.ChunkOffset:x8}" +
                            $" -> L: 0x{chunk.ChunkSizeDecompressed:x8} for chunk: {chunk.ChunkName}");
#endif

                        long remain = chunk.ChunkSizeDecompressed;
                        while (remain > 0)
                        {
                            int toRead = Math.Min((int)remain, buffer.Length);

                            int read = await sourceStream.ReadAsync(
#if NET6_0_OR_GREATER
                                                                    buffer.AsMemory(0, toRead)
#else
                                                                    buffer, 0, toRead
#endif
                                                                    , cooperatedToken.Token);

                            if (currentSourceStreamType == SourceStreamType.Internet)
                            {
                                await (downloadSpeedLimiter?.AddBytesOrWaitAsync(read, token) ??
                                       ValueTask.CompletedTask);
                            }

#if NET6_0_OR_GREATER
                            outStream.Write(buffer.AsSpan(0, read));
#else
                            outStream.Write(buffer, 0, read);
#endif

                            currentWriteOffset += read;
                            remain             -= read;
                            hashInstance.TransformBlock(buffer, 0, read, buffer, 0);
                            writeInfoDelegate?.Invoke(read);

                            if (currentSourceStreamType == SourceStreamType.Internet)
                            {
                            }

                            // Add network activity read indicator
                            if (sourceStreamType != SourceStreamType.OldReference)
                            {
                                downloadInfoDelegate?.Invoke(read,
                                                             sourceStreamType == SourceStreamType.Internet ? read : 0);
                            }

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
                                    InvalidDataException("Chunk has remained data while the read is" +
                                    " already 0 due to corrupted compressed data. Remained data:" +
                                    $" {remain} bytes");
                            }
                        }

                        hashInstance.TransformFinalBlock(buffer, 0, 0);
                        bool isHashVerified = hashInstance.Hash.AsSpan().SequenceEqual(chunk.ChunkHashDecompressed);

                        if (!isHashVerified)
                        {
                            writeInfoDelegate?.Invoke(-currentWriteOffset);
                            if (sourceStreamType != SourceStreamType.OldReference)
                            {
                                downloadInfoDelegate?.Invoke(-currentWriteOffset, 0);
                            }

                            allowDispose = true;
                            this.PushLogWarning($"Source data from type: {sourceStreamType}" +
                                $" seems to be corrupted at transport.\r\nRestarting download for chunk: {chunk.ChunkName}" +
                                $" | 0x{chunk.ChunkOffset:x8} -> L: 0x{chunk.ChunkSizeDecompressed:x8} for: {AssetName}");
                            sourceStreamType = SourceStreamType.Internet;
                            continue;
                        }

#if DEBUG
                        this.PushLogDebug($"Download completed! Chunk: {chunk.ChunkName}" +
                            $" | 0x{chunk.ChunkOffset:x8} ->" +
                            $" L: 0x{chunk.ChunkSizeDecompressed:x8} for: {AssetName}");
#endif
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
                        httpResponseMessage?.Dispose();
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
                        sourceStream = null;
                        SourceStreamType lastSourceStreamType = sourceStreamType;
                        sourceStreamType = SourceStreamType.Internet;

                        if (currentRetry < retryCount)
                        {
                            writeInfoDelegate?.Invoke(-currentWriteOffset);
                            if (lastSourceStreamType != SourceStreamType.OldReference)
                            {
                                downloadInfoDelegate?.Invoke(-currentWriteOffset, 0);
                            }

                            currentWriteOffset = 0;
                            currentRetry++;

                            this.PushLogWarning("An error has occurred while downloading chunk:" +
                                $" {chunk.ChunkName} | 0x{chunk.ChunkOffset:x8} ->" +
                                $" L: 0x{chunk.ChunkSizeDecompressed:x8} for: {AssetName}\r\n{ex}");
                            await Task.Delay(TimeSpan.FromSeconds(1), token);
                            continue;
                        }

                        allowDispose = true;
                        this.PushLogError("An unhandled error has occurred while downloading" +
                            $" chunk: {chunk.ChunkName} | 0x{chunk.ChunkOffset:x8} ->" +
                            $" L: 0x{chunk.ChunkSizeDecompressed:x8} for: {AssetName}\r\n{ex}");
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