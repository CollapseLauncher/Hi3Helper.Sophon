using Hi3Helper.Sophon;
using Hi3Helper.Sophon.Structs;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Text.Json.Serialization;
using System.Text.Json;
using System.Threading.Tasks;
using System.IO;

// ReSharper disable IdentifierTypo

namespace SophonUpdatePreload
{
    public class SophonInformation
    {
        public int CategoryId { get; set; }
        public string CategoryName { get; set; }
        public string MatchingField { get; set; }
        public SophonManifestFileInfo ManifestFileInfo { get; set; }
        public SophonManifestUrlInfo ManifestUrlInfo { get; set; }
        public SophonManifestUrlInfo ChunksUrlInfo { get; set; }
        public SophonManifestChunkInfo ChunkInfo { get; set; }
        public SophonManifestChunkInfo DeduplicatedChunkInfo { get; set; }
        public List<SophonAsset> Assets { get; set; }
    }

    [JsonSerializable(typeof(List<SophonInformation>))]
    [JsonSourceGenerationOptions(WriteIndented = true, IndentSize = 2, IncludeFields = true)]
    public partial class SophonJsonContext : JsonSerializerContext { }

    public class MainApp
    {
        private static int UsageHelp()
        {
            string executableName = Process.GetCurrentProcess().ProcessName + ".exe";
            Console.WriteLine($"{executableName} [Sophon Build URL] [Path to Dumped JSON or - to stdout]\r\n");
            Console.WriteLine("""
                To get your Branch URL, you can either use Proxy or Sniffing tool. The format of the URL would be as follow:
                https://[domain_to_getBuild]/[path_to_getBuild]?plat_app=[your_plat_app_id]&branch=[main|predownload]&password=[your_password]&package_id=[your_package_id]&tag=[your_3dot_separated_game_version]
                """);
            return 1;
        }

        public static async Task<int> Main(params string[] args)
        {
            // Return error if arguments are less than 2
            if (args.Length < 2)
                return UsageHelp();

            // Get url and determine the output method
            string branchInfoUrl = args[0];
            string filePath = args[1];
            bool writeToConsole = filePath == "-";

            // Initialize the client
            using HttpClientHandler httpClientHandler = new HttpClientHandler();
            using HttpClient httpClient = new HttpClient(httpClientHandler);

            // Get the branch information and initialize output list
            SophonBranch sophonBranch = await SophonManifest.GetSophonBranchInfo(httpClient, branchInfoUrl, default);
            List<SophonInformation> sophonInformations = new();

            // If the return code is not OK, then return as error
            if (sophonBranch.ReturnCode != 0 || sophonBranch.Data == null)
            {
                Console.WriteLine($"Error: {sophonBranch.ReturnMessage}, Code: {sophonBranch.ReturnCode}");
                return 1;
            }

            // Enumerate the manifest identity
            foreach (SophonManifestIdentity manifestIdentity in sophonBranch.Data.ManifestIdentityList)
            {
                // Get Chunk Manifest information pair from the manifest identity's Matching Field
                SophonChunkManifestInfoPair sophonInfoPair = await SophonManifest.CreateSophonChunkManifestInfoPair(httpClient, branchInfoUrl, manifestIdentity.MatchingField, default);

                // Initialize Sophon Info class
                SophonInformation sophonInformation = new SophonInformation
                {
                    CategoryId = manifestIdentity.CategoryId,
                    CategoryName = manifestIdentity.CategoryName,
                    MatchingField = manifestIdentity.MatchingField,
                    ManifestFileInfo = manifestIdentity.ManifestFileInfo,
                    ManifestUrlInfo = manifestIdentity.ManifestUrlInfo,
                    ChunksUrlInfo = manifestIdentity.ChunksUrlInfo,
                    ChunkInfo = manifestIdentity.ChunkInfo,
                    DeduplicatedChunkInfo = manifestIdentity.DeduplicatedChunkInfo,
                    Assets = new List<SophonAsset>()
                };

                // Add the Sophon Info class into output list
                sophonInformations.Add(sophonInformation);

                // Enumerate the assets asynchronously and add it to sophonInformation->Assets list.
                await foreach (SophonAsset sophonAsset in SophonManifest.EnumerateAsync(httpClient, sophonInfoPair))
                {
                    sophonInformation.Assets.Add(sophonAsset);
                }
            }

            // Get the output stream whether it will be using file stream our Console StdOut
            using Stream outputStream = writeToConsole ? Console.OpenStandardOutput() : File.Create(filePath);

            // Write the result as JSON into the output stream
            return await PrintAsJson(sophonInformations, outputStream);
        }

        private static async Task<int> PrintAsJson(List<SophonInformation> sophonInformation, Stream outputStream)
        {
            // Serialize object as string into output stream
            await JsonSerializer.SerializeAsync(outputStream, sophonInformation, SophonJsonContext.Default.ListSophonInformation);
            return 0;
        }
    }
}