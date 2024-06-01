// ReSharper disable IdentifierTypo

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
using TaskExtensions = Hi3Helper.Sophon.Helper.TaskExtensions;
using ZstdStream = ZstdNet.DecompressionStream;

namespace Hi3Helper.Sophon
{
    public class SophonAsset
    {
        private const int BufferSize     = 4 << 10;
        private const int ZstdBufferSize = 0; // Default

        public string        AssetName   { get; internal set; }
        public long          AssetSize   { get; internal set; }
        public string        AssetHash   { get; internal set; }
        public bool          IsDirectory { get; internal set; }
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
        /// <param name="readInfoDelegate">
        ///     <inheritdoc cref="DelegateReadStreamInfo" />
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
            WriteToStreamAsync(HttpClient                    client, Stream outStream,
                               DelegateReadStreamInfo        readInfoDelegate         = null,
                               DelegateDownloadAssetComplete downloadCompleteDelegate = null,
                               CancellationToken             token                    = default)
        {
            EnsureOrThrowChunksState();
            EnsureOrThrowStreamState(outStream);

            if (outStream.Length > AssetSize)
            {
                outStream.SetLength(AssetSize);
            }

            foreach (SophonChunk chunk in Chunks)
            {
                await PerformWriteStreamThreadAsync(client, outStream, chunk, token, readInfoDelegate);
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
            WriteToStreamAsync(HttpClient client, Func<Stream> outStreamFunc,
                               ParallelOptions parallelOptions = null, DelegateReadStreamInfo readInfoDelegate = null,
                               DelegateDownloadAssetComplete downloadCompleteDelegate = null)
        {
            EnsureOrThrowChunksState();

            using (Stream initStream = outStreamFunc())
            {
                EnsureOrThrowStreamState(initStream);

                if (initStream.Length > AssetSize)
                {
                    initStream.SetLength(AssetSize);
                }
            }

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
                             using (Stream outStream = outStreamFunc())
                             {
                                 await PerformWriteStreamThreadAsync(client, outStream, chunk, linkedToken.Token,
                                                                     readInfoDelegate);
                             }
                         },
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
                    }
                }
#else
                await Parallel.ForEachAsync(Chunks, parallelOptions, async (chunk, threadToken) =>
                {
                    await using Stream outStream = outStreamFunc();
                    await PerformWriteStreamThreadAsync(client,
                        outStream, chunk, threadToken,
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
            PerformWriteStreamThreadAsync(HttpClient             client, Stream outStream, SophonChunk chunk,
                                          CancellationToken      token            = default,
                                          DelegateReadStreamInfo readInfoDelegate = null)
        {
            long totalSizeFromOffset = chunk.ChunkOffset + chunk.ChunkSizeDecompressed;
            bool isSkipChunk = !(outStream.Length < totalSizeFromOffset ||
                                 !await CheckMd5HashAsync(outStream, chunk, token));

            if (isSkipChunk)
            {
                this.PushLogDebug($"Skipping chunk 0x{chunk.ChunkOffset:x8} -> L: 0x{chunk.ChunkSizeDecompressed:x8} for: {AssetName}");
                readInfoDelegate?.Invoke(chunk.ChunkSizeDecompressed);
                return;
            }

            await InnerWriteStreamToAsync(client, outStream, chunk, token, readInfoDelegate);
        }

        private static async
#if NET6_0_OR_GREATER
            ValueTask<bool>
#else
            Task<bool>
#endif
            CheckMd5HashAsync(Stream outStream, SophonChunk chunk, CancellationToken token)
        {
            byte[] buffer     = ArrayPool<byte>.Shared.Rent(BufferSize);
            int    bufferSize = buffer.Length;

            try
            {
                MD5 hash = MD5.Create();

                outStream.Position = chunk.ChunkOffset;
                long remain = chunk.ChunkSizeDecompressed;

                while (remain > 0)
                {
                    int toRead = (int)Math.Min(bufferSize, remain);
                    int read   = await outStream.ReadAsync(buffer, 0, toRead, token);
                    hash.TransformBlock(buffer, 0, read, buffer, 0);

                    remain -= read;
                }

                hash.TransformFinalBlock(buffer, 0, (int)remain);
                bool   isHashMatch = hash.Hash
                    .AsSpan()
                    .SequenceEqual(chunk.ChunkHashDecompressed);

                return isHashMatch;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        private async
#if NET6_0_OR_GREATER
            ValueTask
#else
            Task
#endif
            InnerWriteStreamToAsync(HttpClient client, Stream outStream, SophonChunk chunk, CancellationToken token,
                                    DelegateReadStreamInfo readInfoDelegate = null)
        {
            int retryCount   = TaskExtensions.DefaultRetryAttempt;
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

            while (true)
            {
                bool   allowDispose       = false;
                Stream httpResponseStream = null;
                Stream sourceStream       = null;

                MD5    hashInstance = MD5.Create();
                byte[] buffer       = ArrayPool<byte>.Shared.Rent(BufferSize);

                {
                    try
                    {
                        CancellationTokenSource innerTimeoutToken = new CancellationTokenSource(TimeSpan.FromSeconds(TaskExtensions.DefaultTimeoutSec), TimeProvider.System);
                        CancellationTokenSource cooperatedToken   = CancellationTokenSource.CreateLinkedTokenSource(token, innerTimeoutToken.Token);

#if DEBUG
                        this.PushLogDebug($"Init. by offset: 0x{chunk.ChunkOffset:x8} -> L: 0x{chunk.ChunkSizeDecompressed:x8} for chunk: {chunk.ChunkName}");

#endif
                        outStream.Position = chunk.ChunkOffset;
                        httpResponseStream =
                            await SophonAssetStream.CreateStreamAsync(client, url, 0, null, cooperatedToken.Token);

                        sourceStream = httpResponseStream ?? throw new HttpRequestException("Response stream returns an empty stream!");
                        if (SophonChunksInfo.IsUseCompression)
                        {
                            sourceStream = new ZstdStream(httpResponseStream, ZstdBufferSize);
                        }
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
                            outStream.Write(buffer, 0, read);
                            currentWriteOffset += read;
                            hashInstance.TransformBlock(buffer, 0, read, buffer, 0);
                            readInfoDelegate?.Invoke(read);

                            currentRetry = 0;
                            innerTimeoutToken.Dispose();
                            cooperatedToken.Dispose();

                            innerTimeoutToken =
                                new CancellationTokenSource(TimeSpan.FromSeconds(TaskExtensions.DefaultTimeoutSec), TimeProvider.System);
                            cooperatedToken =
                                CancellationTokenSource.CreateLinkedTokenSource(token, innerTimeoutToken.Token);
                        }

                        hashInstance.TransformFinalBlock(buffer, 0, read);
                        bool isHashVerified = hashInstance.Hash.AsSpan().SequenceEqual(chunk.ChunkHashDecompressed);

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
                            if (httpResponseStream != null) await httpResponseStream.DisposeAsync();
                            if (sourceStream != null) await sourceStream.DisposeAsync();
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

        private void EnsureOrThrowChunksState()
        {
            if (Chunks == null)
            {
                throw new NullReferenceException("This asset does not have chunk(s)!");
            }
        }

        private static void EnsureOrThrowStreamState(Stream outStream)
        {
            if (outStream == null)
            {
                throw new NullReferenceException("Output stream cannot be null!");
            }

            if (!outStream.CanRead)
            {
                throw new NotSupportedException("Output stream must be readable!");
            }

            if (!outStream.CanWrite)
            {
                throw new NotSupportedException("Output stream must be writable!");
            }

            if (!outStream.CanSeek)
            {
                throw new NotSupportedException("Output stream must be seekable!");
            }
        }
    }
}