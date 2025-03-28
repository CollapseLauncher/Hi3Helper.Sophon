using Hi3Helper.Sophon.Helper;
using Hi3Helper.Sophon.Structs;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
#if NET6_0_OR_GREATER
using System.Net;
#endif
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Hi3Helper.Sophon;
using System.Linq;

// ReSharper disable IdentifierTypo

namespace SophonUpdatePreload
{
    public class MainApp
    {
        private static string _cancelMessage = "";
        private static bool _isRetry;
        private static readonly string[] SizeSuffixes = new string[] { "B", "KB", "MB", "GB", "TB", "PB", "EB", "ZB", "YB" };

        private static int UsageHelp()
        {
            string executableName = Process.GetCurrentProcess().ProcessName + ".exe";
            Console.WriteLine($"{executableName} [Preload/Update] [Sophon Build URL From] [Sophon Build URL To] [Matching field name (usually, you can set \"game\" as the value)] [Old Directory Path] [New Directory Path] [OPTIONAL: Amount of threads to be used (Default: {Environment.ProcessorCount})] [OPTIONAL: Amount of max. connection used for Http Client (Default: 128)]");
            Console.WriteLine($"{executableName} [PreloadPatch/UpdatePatch] [Sophon Patch URL] [ScatteredFiles URL] [Matching field name (usually, you can set \"game\" as the value)] [Version to update from (For example: \"5.4.0\"] [Old Directory Path] [New Directory Path] [OPTIONAL: Amount of threads to be used (Default: {Environment.ProcessorCount})] [OPTIONAL: Amount of max. connection used for Http Client (Default: 128)]");
            return 1;
        }

        public static async Task<int> Main(params string[] args)
        {
            int threads = Environment.ProcessorCount;
            int maxHttpHandle = 128;

            bool isPreloadMode;
            _cancelMessage = "[\"C\"] Stop or [\"R\"] Restart";

            if (args.Length != 0 && args[0].EndsWith("Patch", StringComparison.OrdinalIgnoreCase))
                return await RunPatchMode(args);

            if (args.Length < 6)
                return UsageHelp();

            if (!((isPreloadMode = args[0].Equals("Preload", StringComparison.OrdinalIgnoreCase)) ||
                args[0].Equals("Update", StringComparison.OrdinalIgnoreCase) ||
                args[0].Equals("PreloadPatch", StringComparison.OrdinalIgnoreCase) ||
                args[0].Equals("UpdatePatch", StringComparison.OrdinalIgnoreCase)))
                return UsageHelp();

            if (args.Length > 6 && int.TryParse(args[6], out threads))
                Console.WriteLine($"Thread count has been set to: {threads} for downloading!");

            if (args.Length > 7 && int.TryParse(args[7], out maxHttpHandle))
                Console.WriteLine($"HTTP Client maximum connection has been set to: {maxHttpHandle} handles!");

            string outputDir = args[4];

            Logger.LogHandler += Logger_LogHandler;

        StartDownload:
            using (CancellationTokenSource tokenSource = new CancellationTokenSource())
            {
                using (HttpClientHandler httpHandler = new HttpClientHandler
                {
                    MaxConnectionsPerServer = maxHttpHandle
                })
                using (HttpClient httpClient = new HttpClient(httpHandler)
                {
#if NET6_0_OR_GREATER
                    DefaultRequestVersion = HttpVersion.Version30,
                    DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower
#endif
                })
                {
                    List<SophonAsset> sophonAssets = new List<SophonAsset>();
                    string outputNewDir = args[5];

                    SophonChunkManifestInfoPair manifestPairFrom = await SophonManifest.CreateSophonChunkManifestInfoPair(httpClient, args[1], args[3], tokenSource.Token);
                    SophonChunkManifestInfoPair manifestPairTo = await SophonManifest.CreateSophonChunkManifestInfoPair(httpClient, args[2], args[3], tokenSource.Token);

                    await foreach (SophonAsset asset in SophonUpdate.EnumerateUpdateAsync(
                        httpClient,
                        manifestPairFrom,
                        manifestPairTo,
                        true,
                        null,
                        tokenSource.Token))
                    {
                        sophonAssets.Add(asset);
                    }

                    long currentRead = 0;
                    Task.Run(() => AppExitKeyTrigger(tokenSource));

                    ParallelOptions parallelOptions =
                        new ParallelOptions
                        {
                            CancellationToken = tokenSource.Token,
                            MaxDegreeOfParallelism = threads
                        };

                    Stopwatch stopwatch = Stopwatch.StartNew();

                    try
                    {
                        string chunkOutPath = Path.Combine(outputDir, "chunk_collapse");
                        if (!Directory.Exists(chunkOutPath))
                            Directory.CreateDirectory(chunkOutPath);

                        if (!Directory.Exists(outputNewDir))
                            Directory.CreateDirectory(outputNewDir);

                        long totalSizeDiff = sophonAssets.GetCalculatedDiffSize(!isPreloadMode);
                        string totalSizeDiffUnit = SummarizeSizeSimple(totalSizeDiff);
                        string totalSizeUnit = SummarizeSizeSimple(manifestPairTo.ChunksInfo.TotalSize);

                        foreach (string fileTemp in Directory.EnumerateFiles(outputDir, "*_tempUpdate", SearchOption.AllDirectories))
                        {
                            File.Delete(fileTemp);
                        }

                        _isRetry = false;

                        ActionBlock<Tuple<SophonAsset, HttpClient>> downloadTaskQueues = new(
                            async ctx =>
                            {
                                SophonAsset asset = ctx.Item1;
                                HttpClient client = ctx.Item2;

                                if (asset.IsDirectory)
                                    return;

                                string outputAssetPath = Path.Combine(outputDir, asset.AssetName);
                                string outputAssetDir = Path.GetDirectoryName(outputAssetPath);

                                if (!string.IsNullOrEmpty(outputAssetDir))
                                    Directory.CreateDirectory(outputAssetDir);

                                if (isPreloadMode)
                                {
                                    await asset.DownloadDiffChunksAsync(
                                        client,
                                        chunkOutPath,
                                        parallelOptions,
                                        read =>
                                        {
                                            Interlocked.Add(ref currentRead, read);
                                            string sizeUnit = SummarizeSizeSimple(currentRead);
                                            string speedUnit = SummarizeSizeSimple(currentRead / stopwatch.Elapsed.TotalSeconds);
                                            Console.Write($"{_cancelMessage} | {sizeUnit}/{totalSizeUnit} ({totalSizeDiffUnit} diff) -> {currentRead} ({speedUnit}/s)    \r");
                                        });
                                }
                                else
                                {
                                    await asset.WriteUpdateAsync(
                                        client,
                                        outputDir,
                                        outputNewDir,
                                        chunkOutPath,
                                        false,
                                        parallelOptions,
                                        read =>
                                        {
                                            Interlocked.Add(ref currentRead, read);
                                            string sizeUnit = SummarizeSizeSimple(currentRead);
                                            string speedUnit = SummarizeSizeSimple(currentRead / stopwatch.Elapsed.TotalSeconds);
                                            Console.Write($"{_cancelMessage} | {sizeUnit}/{totalSizeUnit} ({totalSizeDiffUnit} diff) -> {currentRead} ({speedUnit}/s)    \r");
                                        },
                                        (_, _) =>
                                        {
                                            Console.WriteLine($"Downloaded: {asset.AssetName}");
                                        });
                                }
                            },
                            new ExecutionDataflowBlockOptions
                            {
                                CancellationToken = tokenSource.Token,
                                MaxDegreeOfParallelism = threads,
                                MaxMessagesPerTask = threads
                            });

                        foreach (SophonAsset asset in sophonAssets)
                        {
                            await downloadTaskQueues.SendAsync(new Tuple<SophonAsset, HttpClient>(asset, httpClient), tokenSource.Token);
                        }

                        downloadTaskQueues.Complete();
                        await downloadTaskQueues.Completion;
                    }
                    catch (OperationCanceledException)
                    {
                        Console.WriteLine("Download has been cancelled!");
                    }
                    finally
                    {
                        stopwatch.Stop();
                    }
                }
            }

            if (_isRetry)
                goto StartDownload;

            return 0;
        }

        private static string[] ParseMultipleMatchingFields(string matchingFieldSource)
        {
            if (string.IsNullOrEmpty(matchingFieldSource))
            {
                throw new ArgumentNullException(nameof(matchingFieldSource), "Matching field cannot be left undefined!");
            }

            // ReSharper disable once UseIndexFromEndExpression
            if (matchingFieldSource[0] == '[' && matchingFieldSource[matchingFieldSource.Length - 1] == ']')
            {
                return ParseMultipleMatchingFieldsInner(matchingFieldSource);
            }

            return new[] { matchingFieldSource };
        }

        private static string[] ParseMultipleMatchingFieldsInner(string matchingFieldSource)
        {
            matchingFieldSource = matchingFieldSource.TrimStart('[').TrimEnd(']');
            return matchingFieldSource.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        }

        private static async Task<int> RunPatchMode(string[] args)
        {
            int threads = Environment.ProcessorCount;
            int maxHttpHandle = 128;

            if (args.Length > 7 && int.TryParse(args[7], out threads))
                Console.WriteLine($"Thread count has been set to: {threads} for downloading!");

            if (args.Length > 8 && int.TryParse(args[8], out maxHttpHandle))
                Console.WriteLine($"HTTP Client maximum connection has been set to: {maxHttpHandle} handles!");

            if (args.Length < 7)
                return UsageHelp();

            string oldDir = args[5];
            string newDir = args[6];
            string patchesDir = Path.Combine(oldDir, "chunk_collapse");

            string sophonPatchUrl = args[1];
            string scatteredFilesUrl = args[2];
            string sophonMatchingField = args[3];
            string sophonVersionUpdateFrom = args[4];

            Logger.LogHandler += Logger_LogHandler;
            bool isPreloadMode = args[0].Equals("PreloadPatch", StringComparison.OrdinalIgnoreCase);

        StartDownload:
            using (CancellationTokenSource tokenSource = new CancellationTokenSource())
            using (HttpClientHandler httpHandler = new HttpClientHandler())
            {
                httpHandler.MaxConnectionsPerServer = maxHttpHandle;
                using (HttpClient httpClient = new HttpClient(httpHandler)
                {
                    Timeout = TimeSpan.FromSeconds(5)
                })
                {

                    long currentRead = 0;
                    _ = Task.Run(() => AppExitKeyTrigger(tokenSource));

                    string[] tags = ParseMultipleMatchingFields(sophonMatchingField);

                    long totalRemoteCompressedSize = 0;
                    List<SophonPatchAsset> patchAssetList = new();
                    foreach (string matchingField in tags)
                    {
                        SophonChunkManifestInfoPair patchInfoPair = await SophonPatch.CreateSophonChunkManifestInfoPair(httpClient, sophonPatchUrl, sophonVersionUpdateFrom, matchingField, tokenSource.Token);

                        if (!patchInfoPair.IsFound)
                        {
                            Console.Error.WriteLine($"An error has occurred! -> ({patchInfoPair.ReturnCode}) {patchInfoPair.ReturnMessage}");
                            return patchInfoPair.ReturnCode;
                        }

                        string baseConsoleTitle = $"SophonUpdatePreload - {(isPreloadMode ? "Preloading" : "Updating")}: {sophonVersionUpdateFrom} -> {patchInfoPair.OtherSophonPatchData.TagName} for MatchingFields: [{string.Join(", ", tags)}]";
                        Console.Title = baseConsoleTitle;

                        totalRemoteCompressedSize += patchInfoPair.ChunksInfo.TotalCompressedSize;

                        await foreach (SophonPatchAsset patchAsset in SophonPatch.EnumerateUpdateAsync(httpClient,
                                           patchInfoPair,
                                           sophonVersionUpdateFrom,
                                           scatteredFilesUrl,
                                           null,
                                           tokenSource.Token))
                        {
                            patchAssetList.Add(patchAsset);
                        }
                    }

                    long totalAssetSize = patchAssetList
                        .Where(x => x.PatchMethod != SophonPatchMethod.Remove)
                        .Sum(x => x.TargetFileSize);

                    long totalAssetPatchSize = patchAssetList
                        .Where(x => x.PatchMethod != SophonPatchMethod.Remove && x.PatchMethod != SophonPatchMethod.DownloadOver)
                        .Sum(x => x.PatchChunkLength);

                    string totalAssetSizeSizeUnit = SummarizeSizeSimple(totalAssetSize);
                    string totalAssetPatchSizeUnit = SummarizeSizeSimple(totalAssetPatchSize);

                    Debug.Assert(totalAssetPatchSize == totalRemoteCompressedSize);

                    Stopwatch stopwatch = Stopwatch.StartNew();

                    _isRetry = false;

                    try
                    {
                        ExecutionDataflowBlockOptions dataflowBlockOpt = new ExecutionDataflowBlockOptions
                        {
                            CancellationToken = tokenSource.Token,
                            MaxDegreeOfParallelism = threads,
                            MaxMessagesPerTask = threads,
                            BoundedCapacity = threads
                        };

                        long currentDownloaded = 0;
                        long lastDownloaded = 0;

                        long scLastTick = Environment.TickCount;
                        long scLastReceivedBytes = 0;
                        double scLastSpeed = 0;

                        ActionBlock<ValueTuple<SophonPatchAsset, HttpClient, CancellationToken>> downloadTaskQueues = new(
                            async ctx =>
                            {
                                SophonPatchAsset asset = ctx.Item1;
                                HttpClient client = ctx.Item2;
                                CancellationToken token = ctx.Item3;

                                await asset.DownloadPatchAsync(client,
                                    patchesDir,
                                    true,
                                    downloadRead =>
                                    {
                                        Interlocked.Add(ref currentRead, downloadRead);
                                        string sizeUnit = SummarizeSizeSimple(currentRead);
                                        string speedUnit = SummarizeSizeSimple(CalculateSpeed(downloadRead));
                                        Console.Write($"{_cancelMessage} | {sizeUnit}/{totalAssetPatchSizeUnit} -> {currentRead} (Download: {speedUnit}/s)    \r");
                                    },
                                    null,
                                    token);
                            }, dataflowBlockOpt);

                        object currentLock = new();

                        ActionBlock<ValueTuple<SophonPatchAsset, HttpClient, CancellationToken>> patchTaskQueues = new(
                            async ctx =>
                            {
                                SophonPatchAsset asset = ctx.Item1;
                                HttpClient client = ctx.Item2;
                                CancellationToken token = ctx.Item3;

                                await asset.ApplyPatchUpdateAsync(client,
                                                                  oldDir,
                                                                  patchesDir,
                                                                  true,
                                                                  downloadRead =>
                                                                  {
                                                                      lock (currentLock)
                                                                      {
                                                                          Interlocked.Add(ref currentDownloaded, downloadRead);
                                                                      }
                                                                  },
                                                                  diskWrite =>
                                                                  {
                                                                      Interlocked.Add(ref currentRead, diskWrite);
                                                                      string sizeUnit = SummarizeSizeSimple(currentRead);
                                                                      string speedUnit = SummarizeSizeSimple(CalculateSpeed(diskWrite));

                                                                      lock (currentLock)
                                                                      {
                                                                          long downloadRead = currentDownloaded - lastDownloaded;
                                                                          string speedUnitDownload = SummarizeSizeSimple(CalculateSpeed(downloadRead, ref scLastSpeed, ref scLastReceivedBytes, ref scLastTick));
                                                                          Console.Write($"{_cancelMessage} | {sizeUnit}/{totalAssetSizeSizeUnit} -> {currentRead} (DiskWrite: {speedUnit}/s) (Download: {speedUnitDownload}/s)    \r");

                                                                          lastDownloaded = currentDownloaded;
                                                                      }
                                                                  },
                                                                  null,
                                                                  token);
                            }, dataflowBlockOpt);

                        foreach (SophonPatchAsset asset in patchAssetList.EnsureOnlyGetDedupPatchAssets())
                        {
                            await downloadTaskQueues.SendAsync(new ValueTuple<SophonPatchAsset, HttpClient, CancellationToken>(asset, httpClient, tokenSource.Token), tokenSource.Token);
                        }

                        downloadTaskQueues.Complete();
                        await downloadTaskQueues.Completion;

                        if (!isPreloadMode)
                        {
                            currentRead = 0;

                            foreach (SophonPatchAsset asset in patchAssetList)
                            {
                                await patchTaskQueues.SendAsync(new ValueTuple<SophonPatchAsset, HttpClient, CancellationToken>(asset, httpClient, tokenSource.Token), tokenSource.Token);
                            }

                            patchTaskQueues.Complete();
                            await patchTaskQueues.Completion;

                            patchAssetList.RemovePatches(patchesDir);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        Console.WriteLine("Download has been cancelled!");
                    }
                    finally
                    {
                        stopwatch.Stop();
                    }
                }
            }

            if (_isRetry)
                goto StartDownload;

            return 0;
        }

        private const double ScOneSecond = 1000;
        private static long _scLastTick = Environment.TickCount;
        private static long _scLastReceivedBytes;
        private static double _scLastSpeed;

        protected static double CalculateSpeed(long receivedBytes) => CalculateSpeed(receivedBytes, ref _scLastSpeed, ref _scLastReceivedBytes, ref _scLastTick);

        protected static double CalculateSpeed(long receivedBytes, ref double lastSpeedToUse, ref long lastReceivedBytesToUse, ref long lastTickToUse)
        {
            long currentTick = Environment.TickCount - lastTickToUse + 1;
            long totalReceivedInSecond = Interlocked.Add(ref lastReceivedBytesToUse, receivedBytes);
            double speed = totalReceivedInSecond * ScOneSecond / currentTick;

            if (!(currentTick > ScOneSecond))
            {
                return lastSpeedToUse;
            }

            lastSpeedToUse = speed;
            _ = Interlocked.Exchange(ref lastSpeedToUse, speed);
            _ = Interlocked.Exchange(ref lastReceivedBytesToUse, 0);
            _ = Interlocked.Exchange(ref lastTickToUse, Environment.TickCount);
            return lastSpeedToUse;
        }

        private static string SummarizeSizeSimple(double value, int decimalPlaces = 2)
        {
            byte mag = (byte)Math.Log(value, 1000);

            return $"{Math.Round(value / (1L << (mag * 10)), decimalPlaces)} {SizeSuffixes[mag]}";
        }

        private static void AppExitKeyTrigger(CancellationTokenSource tokenSource)
        {
            while (true)
            {
                ConsoleKeyInfo keyInfo = Console.ReadKey();
                switch (keyInfo.Key)
                {
                    case ConsoleKey.C:
                        _cancelMessage = "Cancelling download...";
                        tokenSource.Cancel();
                        return;
                    case ConsoleKey.R:
                        _isRetry = true;
                        _cancelMessage = "Retrying download...";
                        tokenSource.Cancel();
                        return;
                }
            }
        }

        private static void Logger_LogHandler(object sender, LogStruct e)
        {
#if !DEBUG
            if (e.LogLevel == LogLevel.Debug) return;
#endif

            Console.WriteLine($"[{e.LogLevel}] {e.Message}");
        }
    }
}