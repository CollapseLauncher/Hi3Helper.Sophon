using Hi3Helper.Sophon.Helper;
using Hi3Helper.Sophon.Structs;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
#if !NET6_0_OR_GREATER
using System.Threading.Tasks.Dataflow;
#endif

// ReSharper disable InvalidXmlDocComment
// ReSharper disable PossibleNullReferenceException
// ReSharper disable AccessToDisposedClosure
// ReSharper disable IdentifierTypo
// ReSharper disable ConvertIfStatementToSwitchStatement

namespace Hi3Helper.Sophon
{
    public partial class SophonAsset
    {
        /// <summary>
        ///     Perform an update process to an existing or new file and run each chunk download sequentially.
        /// </summary>
        /// <param name="client">
        ///     The <see cref="HttpClient" /> to be used for downloading process.<br />Ensure that the maximum connection for the
        ///     <see cref="HttpClient" /> has been set to at least (Number of Threads/CPU core * 25%) or == Number of Threads/CPU
        ///     core
        /// </param>
        /// <param name="oldInputDir">
        ///     The directory of the old input file.
        /// </param>
        /// <param name="newOutputDir">
        ///     The directory of the new output file to be written.
        /// </param>
        /// <param name="chunkDir">
        ///     The directory of the staged chunk.
        /// </param>
        /// <param name="removeChunkAfterApply">
        ///     Remove chunk file after applying update
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
            WriteUpdateAsync(HttpClient                    client,
                             string                        oldInputDir,
                             string                        newOutputDir,
                             string                        chunkDir,
                             bool                          removeChunkAfterApply    = false,
                             DelegateReadStreamInfo        readInfoDelegate         = null,
                             DelegateDownloadAssetComplete downloadCompleteDelegate = null,
                             CancellationToken             token                    = default)
        {
            const string tempExt = "_tempUpdate";

            this.EnsureOrThrowChunksState();
            this.EnsureOrThrowOutputDirectoryExistence(oldInputDir);
            this.EnsureOrThrowOutputDirectoryExistence(newOutputDir);
            this.EnsureOrThrowOutputDirectoryExistence(chunkDir);

            string outputOldPath     = Path.Combine(oldInputDir,  AssetName);
            string outputNewPath     = Path.Combine(newOutputDir, AssetName);
            string outputNewTempPath = outputNewPath + tempExt;
            string outputNewDir      = Path.GetDirectoryName(outputNewPath);

            if (!Directory.Exists(outputNewDir) && outputNewDir != null)
            {
                Directory.CreateDirectory(outputNewDir);
            }

            FileInfo outputOldFileInfo     = new FileInfo(outputOldPath);
            FileInfo outputNewFileInfo     = new FileInfo(outputNewPath);
            FileInfo outputNewTempFileInfo = new FileInfo(outputNewTempPath);

            foreach (SophonChunk chunk in Chunks)
            {
                await InnerWriteUpdateAsync(client,                chunkDir, readInfoDelegate,      outputOldFileInfo,
                                            outputNewTempFileInfo, chunk,    removeChunkAfterApply, token);
            }

            if (outputNewTempFileInfo.FullName != outputNewFileInfo.FullName)
            {
            #if NET6_0_OR_GREATER
                outputNewTempFileInfo.MoveTo(outputNewFileInfo.FullName, true);
            #else
                outputNewFileInfo.Delete();
                outputNewTempFileInfo.MoveTo(outputNewFileInfo.FullName);
            #endif
            }

            this.PushLogInfo($"Asset: {AssetName} | (Hash: {AssetHash} -> {AssetSize} bytes) has been completely downloaded!");
            downloadCompleteDelegate?.Invoke(this);
        }

        /// <summary>
        ///     Perform an update process to an existing or new file and run each chunk download in parallel instead of
        ///     sequentially.
        /// </summary>
        /// <param name="client">
        ///     The <see cref="HttpClient" /> to be used for downloading process.<br />Ensure that the maximum connection for the
        ///     <see cref="HttpClient" /> has been set to at least (Number of Threads/CPU core * 25%) or == Number of Threads/CPU
        ///     core
        /// </param>
        /// <param name="oldInputDir">
        ///     The directory of the old input file.
        /// </param>
        /// <param name="newOutputDir">
        ///     The directory of the new output file to be written.
        /// </param>
        /// <param name="chunkDir">
        ///     The directory of the staged chunk.
        /// </param>
        /// <param name="removeChunkAfterApply">
        ///     Remove chunk file after applying update
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
            WriteUpdateAsync(HttpClient                    client,
                             string                        oldInputDir,
                             string                        newOutputDir,
                             string                        chunkDir,
                             bool                          removeChunkAfterApply    = false,
                             ParallelOptions               parallelOptions          = null,
                             DelegateReadStreamInfo        readInfoDelegate         = null,
                             DelegateDownloadAssetComplete downloadCompleteDelegate = null)
        {
            const string tempExt = "_tempUpdate";

            this.EnsureOrThrowChunksState();
            this.EnsureOrThrowOutputDirectoryExistence(oldInputDir);
            this.EnsureOrThrowOutputDirectoryExistence(newOutputDir);
            this.EnsureOrThrowOutputDirectoryExistence(chunkDir);

            string outputOldPath     = Path.Combine(oldInputDir,  AssetName);
            string outputNewPath     = Path.Combine(newOutputDir, AssetName);
            string outputNewTempPath = outputNewPath + tempExt;
            string outputNewDir      = Path.GetDirectoryName(outputNewPath);

            if (!Directory.Exists(outputNewDir) && outputNewDir != null)
            {
                Directory.CreateDirectory(outputNewDir);
            }

            FileInfo outputOldFileInfo     = new FileInfo(outputOldPath);
            FileInfo outputNewFileInfo     = new FileInfo(outputNewPath);
            FileInfo outputNewTempFileInfo = new FileInfo(outputNewTempPath);
            if (outputNewFileInfo.Exists && outputNewFileInfo.Length == AssetSize)
            {
                outputNewTempFileInfo = outputNewFileInfo;
            }

        #if !NET6_0_OR_GREATER
            using (CancellationTokenSource actionToken = new CancellationTokenSource())
            {
                using (CancellationTokenSource linkedToken = CancellationTokenSource
                          .CreateLinkedTokenSource(actionToken.Token, parallelOptions.CancellationToken))
                {
                    ActionBlock<SophonChunk> actionBlock = new ActionBlock<SophonChunk>(
                     async chunk =>
                     {
                         await InnerWriteUpdateAsync(client, chunkDir, readInfoDelegate, outputOldFileInfo,
                                                     outputNewTempFileInfo, chunk, linkedToken.Token);
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
            if (parallelOptions != null)
            {
                await Parallel.ForEachAsync(Chunks, parallelOptions,
                                            async (chunk, threadToken) =>
                                            {
                                                await InnerWriteUpdateAsync(client, chunkDir, readInfoDelegate,
                                                                            outputOldFileInfo, outputNewTempFileInfo,
                                                                            chunk, removeChunkAfterApply,
                                                                            threadToken);
                                            });
            }
        #endif

            if (outputNewTempFileInfo.FullName != outputNewFileInfo.FullName)
            {
            #if NET6_0_OR_GREATER
                outputNewTempFileInfo.MoveTo(outputNewFileInfo.FullName, true);
            #else
                outputNewFileInfo.Delete();
                outputNewTempFileInfo.MoveTo(outputNewFileInfo.FullName);
            #endif
            }

            this.PushLogInfo($"Asset: {AssetName} | (Hash: {AssetHash} -> {AssetSize} bytes) has been completely downloaded!");
            downloadCompleteDelegate?.Invoke(this);
        }

        private async Task InnerWriteUpdateAsync(HttpClient             client,
                                                 string                 chunkDir,
                                                 DelegateReadStreamInfo readInfoDelegate,
                                                 FileInfo               outputOldFileInfo,
                                                 FileInfo               outputNewFileInfo,
                                                 SophonChunk            chunk,
                                                 bool                   removeChunkAfterApply,
                                                 CancellationToken      token)
        {
            Stream           inputStream  = null;
            Stream           outputStream = null;
            SourceStreamType streamType   = SourceStreamType.Internet;

            try
            {
                bool isUseOldFile = chunk.ChunkOldOffset != -1 &&
                                    outputOldFileInfo.Exists &&
                                    outputOldFileInfo.Length >= chunk.ChunkOldOffset + chunk.ChunkSizeDecompressed;

                if (isUseOldFile)
                {
                    inputStream = outputOldFileInfo.Open(FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
                    streamType  = SourceStreamType.OldReference;
                    this.PushLogDebug($"Using old file as reference at offset: 0x{chunk.ChunkOldOffset:x8} -> 0x{chunk.ChunkSizeDecompressed:x8} for: {AssetName}");
                }
                else
                {
                    string   cachedChunkName = chunk.GetChunkStagingFilenameHash(this);
                    string   cachedChunkPath = Path.Combine(chunkDir, cachedChunkName);
                    FileInfo cachedChunkInfo = new FileInfo(cachedChunkPath);
                    if (cachedChunkInfo.Exists && cachedChunkInfo.Length != chunk.ChunkSize)
                    {
                        cachedChunkInfo.Delete();
                        this.PushLogDebug($"Cached/preloaded chunk has invalid size for: {AssetName}. Expecting: 0x{chunk.ChunkSize:x8} but get: 0x{cachedChunkInfo.Length:x8} instead. Fallback to download it instead!");
                    }
                    else if (cachedChunkInfo.Exists)
                    {
                        inputStream = new FileStream(cachedChunkInfo.FullName, FileMode.Open, FileAccess.Read,
                                                     FileShare.Read, 4 << 10, removeChunkAfterApply ?
                                                     FileOptions.DeleteOnClose : FileOptions.None);
                        streamType = SourceStreamType.CachedLocal;
                        this.PushLogDebug($"Using cached/preloaded chunk as reference at offset: 0x{chunk.ChunkOffset:x8} -> 0x{chunk.ChunkSizeDecompressed:x8} for: {AssetName}");
                    }
                }

                outputStream = outputNewFileInfo.Open(FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite);
                await PerformWriteStreamThreadAsync(client, inputStream, streamType, outputStream, chunk, token,
                                                    readInfoDelegate);
            }
            finally
            {
            #if NET6_0_OR_GREATER
                if (inputStream != null)
                {
                    await inputStream.DisposeAsync();
                }

                if (outputStream != null)
                {
                    await outputStream.DisposeAsync();
                }
            #else
                inputStream?.Dispose();
                outputStream?.Dispose();
            #endif
            }
        }
    }
}