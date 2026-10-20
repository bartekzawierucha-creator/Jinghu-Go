using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Android.Content;
using JianghuGo.Api;
using JianghuGo.Infrastructure.InMemory;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace JianghuGo.Android
{
    /// <summary>
    /// Offline single-player mode: the real game server (same code, same rules) runs inside the app on the loopback
    /// interface only. State is kept in memory and saved to a snapshot file in the app's private storage, so progress
    /// survives restarts. The client talks to it over HTTP exactly as it would to a remote server.
    /// </summary>
    public static class EmbeddedServer
    {
        private static WebApplication _app;
        private static Task<string> _starting;

        public static string Url { get; private set; }

        public static Task<string> StartAsync(Context context) => _starting ??= Task.Run(() => StartCoreAsync(context));

        private static async Task<string> StartCoreAsync(Context context)
        {
            var root = Path.Combine(context.FilesDir!.AbsolutePath, "server");
            var config = Path.Combine(root, "game-config");
            Directory.CreateDirectory(config);
            Extract(context, "appsettings.json", Path.Combine(root, "appsettings.json"));
            var settings = new Dictionary<string, string>
            {
                ["Storage:Provider"] = "InMemory",
                ["Storage:InMemorySnapshotPath"] = Path.Combine(root, "data", "world.json"),
                ["Worker:RunInProcess"] = "true",
                ["World:Seed"] = SeedFor(context),
                ["Logging:LogLevel:Default"] = "Warning",
                ["RateLimiting:AuthPermitsPerMinute"] = "60",
                // 2.1: real weather and real places from open data (only the ~3 km region is sent, never the position)
                ["RealWorld:Enabled"] = "true",
            };
            foreach (var asset in context.Assets!.List("game-config")!)
            {
                var target = Path.Combine(config, asset);
                Extract(context, "game-config/" + asset, target);
                // "dao-lab.json" -> GameConfig:DaoLabPath (absolute, so the modules don't look next to the binaries)
                var key = string.Concat(Path.GetFileNameWithoutExtension(asset).Split('-').Select(p => char.ToUpperInvariant(p[0]) + p.Substring(1)));
                settings["GameConfig:" + key + "Path"] = target;
            }

            var app = JianghuApi.Create(new[] { "--urls", "http://127.0.0.1:0", "--environment", "Offline", "--preventHostingStartup", "true" },
                b => b.Configuration.AddInMemoryCollection(settings), root);
            await app.StartAsync().ConfigureAwait(false);
            _app = app;
            Url = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
            return Url;
        }

        /// <summary>Saves the latest state now (called when the app goes to the background).</summary>
        public static void Flush()
        {
            try
            {
                _app?.Services.GetService<InMemoryDatabase>()?.Flush();
            }
            catch (IOException)
            {
                // the background writer retries on the next commit
            }
        }

        private static string SeedFor(Context context)
        {
            var prefs = context.GetSharedPreferences("jianghu", FileCreationMode.Private)!;
            var seed = prefs.GetString("jianghu.offline_seed", null);
            if (seed != null) return seed;
            seed = (System.Security.Cryptography.RandomNumberGenerator.GetInt32(1, int.MaxValue) * 1000L + System.Security.Cryptography.RandomNumberGenerator.GetInt32(0, 1000)).ToString(System.Globalization.CultureInfo.InvariantCulture);
            using (var e = prefs.Edit()!)
            {
                e.PutString("jianghu.offline_seed", seed);
                e.Commit();
            }

            return seed;
        }

        private static void Extract(Context context, string asset, string target)
        {
            using var input = context.Assets!.Open(asset);
            using var output = File.Create(target);
            input.CopyTo(output);
        }
    }
}
