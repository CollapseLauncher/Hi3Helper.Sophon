using Hi3Helper.Sophon.Helper;
using Hi3Helper.Sophon.Infos;
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
            Console.WriteLine($"{executableName} [Sophon Build URL] [Matching field name (usually, you can set \"game\" as the value)] [Download Output Path] [OPTIONAL: Amount of threads to be used (Default: {Environment.ProcessorCount})] [OPTIONAL: Amount of max. connection used for Http Client (Default: 128)]");
            return 1;
        }

        public static async Task<int> Main(params string[] args)
        {
            int threads = Environment.ProcessorCount;
            int maxHttpHandle = 128;

            if (args.Length < 3)
                return UsageHelp();

            if (args.Length > 3 && int.TryParse(args[3], out threads))
                Console.WriteLine($"Thread count has been set to: {threads} for downloading!");

            if (args.Length > 4 && int.TryParse(args[4], out maxHttpHandle))
                Console.WriteLine($"HTTP Client maximum connection has been set to: {maxHttpHandle} handles!");

            string outputDir = args[2];

            Logger.LogHandler += Logger_LogHandler;

        StartDownload:
            using (CancellationTokenSource tokenSource = new CancellationTokenSource())
            {
                _cancelMessage = "[\"C\"] Stop or [\"R\"] Restart";
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
                    SophonChunkManifestInfoPair manifestPair = await SophonManifest.CreateSophonChunkManifestInfoPair(httpClient, args[0], args[1], tokenSource.Token);
                    SophonChunksInfo sophonChunksInfo = manifestPair.ChunksInfo;

                    await foreach (SophonAsset sophonAsset in SophonManifest.EnumerateAsync(
                                       httpClient,
                                       manifestPair,
                                       null,
                                       tokenSource.Token))
                    {
                        // If the asset is a directory, skip
                        if (sophonAsset.IsDirectory)
                        {
                            continue;
                        }

                        sophonAssets.Add(sophonAsset);
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

                        string totalSizeUnit = SummarizeSizeSimple(sophonChunksInfo.TotalSize);
                        string totalSizeDiffUnit = "0 B";

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

                                await asset.WriteToStreamAsync(
                                    client,
                                    () => new FileStream(outputAssetPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite),
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