// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace osu.Game.Screens.Edit.Verify
{
    /// <summary>
    /// Runs the ranking criteria checks of Mapset Verifier (https://github.com/Naxesss/MapsetVerifier) on a folder.
    /// </summary>
    /// <remarks>
    /// Mapset Verifier is GPL-3.0, so it is never linked or copied: its backend (the same one its own UI talks to) is started as a separate process and used over HTTP.
    /// It's started on first use, on its own port (its own UI uses 5005), and stopped when this client exits.
    /// </remarks>
    public static class MapsetVerifierServer
    {
        private const int port = 51005;

        private static readonly HttpClient http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}"), Timeout = TimeSpan.FromMinutes(2) };
        private static readonly SemaphoreSlim start_lock = new SemaphoreSlim(1, 1);

        private static Process? process;

        /// <summary>
        /// Finds Mapset Verifier: the configured path, or the one the Linux package puts in <c>/usr/bin</c>.
        /// </summary>
        /// <param name="configured">
        /// Either the backend executable (<c>MapsetVerifier</c> in <c>resources/bin/server/dist/&lt;platform&gt;</c> of the installation), or the packaged app (an AppImage), which the backend is extracted from.
        /// </param>
        public static string? FindApp(string? configured)
            // ponytail: only the Linux package is detected on its own, other platforms set the path to the backend executable.
            => new[] { configured, "/usr/bin/mapset-verifier" }.FirstOrDefault(p => !string.IsNullOrEmpty(p) && File.Exists(p));

        /// <summary>
        /// Runs all checks on the beatmap set in a folder.
        /// </summary>
        /// <param name="app">The path from <see cref="FindApp"/>.</param>
        /// <param name="cacheDirectory">Where the backend is extracted to, if <paramref name="app"/> is the packaged app.</param>
        /// <param name="folder">The absolute path of the folder with the files of the set.</param>
        /// <param name="cancellationToken">Stops waiting for the result.</param>
        /// <returns>The JSON of the result (<c>general</c>, <c>difficulties</c> and <c>checks</c>).</returns>
        public static async Task<string> RunChecksAsync(string app, string cacheDirectory, string folder, CancellationToken cancellationToken)
        {
            await ensureRunningAsync(app, cacheDirectory, cancellationToken).ConfigureAwait(false);

            // createSnapshot would fill Mapset Verifier's own snapshot history with runs on temporary folders.
            using var content = new StringContent(Newtonsoft.Json.JsonConvert.SerializeObject(new { folder, createSnapshot = false }), Encoding.UTF8, "application/json");
            using var response = await http.PostAsync("beatmap/runChecks", content, cancellationToken).ConfigureAwait(false);
            string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException(describeError(body) ?? $"HTTP {(int)response.StatusCode}");

            return body;
        }

        /// <summary>
        /// Stops the backend if this client started it.
        /// </summary>
        public static void Stop()
        {
            try
            {
                process?.Kill(true);
            }
            catch
            {
            }

            process = null;
        }

        private static async Task ensureRunningAsync(string app, string cacheDirectory, CancellationToken cancellationToken)
        {
            await start_lock.WaitAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                // also picks up a backend which an earlier crash of this client left running.
                if (await isUpAsync(cancellationToken).ConfigureAwait(false))
                    return;

                string server = await resolveServerAsync(app, cacheDirectory, cancellationToken).ConfigureAwait(false);

                process = Process.Start(new ProcessStartInfo(server)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    ArgumentList = { $"--urls=http://127.0.0.1:{port}" },
                }) ?? throw new InvalidOperationException("couldn't start the Mapset Verifier backend");

                // nobody reads the output, a full pipe would stall the backend.
                process.OutputDataReceived += (_, _) => { };
                process.ErrorDataReceived += (_, _) => { };
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                AppDomain.CurrentDomain.ProcessExit += (_, _) => Stop();

                for (int i = 0; i < 100; i++)
                {
                    if (process.HasExited)
                        throw new InvalidOperationException("the Mapset Verifier backend exited right after starting");

                    if (await isUpAsync(cancellationToken).ConfigureAwait(false))
                        return;

                    await Task.Delay(100, cancellationToken).ConfigureAwait(false);
                }

                throw new TimeoutException("the Mapset Verifier backend didn't come up");
            }
            finally
            {
                start_lock.Release();
            }
        }

        private static async Task<bool> isUpAsync(CancellationToken cancellationToken)
        {
            try
            {
                using var response = await http.GetAsync("health", cancellationToken).ConfigureAwait(false);
                return response.IsSuccessStatusCode;
            }
            catch (HttpRequestException)
            {
                return false;
            }
        }

        /// <summary>
        /// The backend executable itself is used as is, the packaged app is an AppImage which the backend is extracted from once (per version of the app).
        /// </summary>
        private static async Task<string> resolveServerAsync(string app, string cacheDirectory, CancellationToken cancellationToken)
        {
            if (Path.GetFileName(app).StartsWith("MapsetVerifier", StringComparison.OrdinalIgnoreCase))
                return app;

            var info = new FileInfo(app);
            string directory = Directory.CreateDirectory(Path.Combine(cacheDirectory, $"{info.Length}-{info.LastWriteTimeUtc.Ticks}")).FullName;

            string? server = findServer(directory);

            if (server != null)
                return server;

            using var extract = Process.Start(new ProcessStartInfo(app)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = directory,
                ArgumentList = { "--appimage-extract", "resources/bin/*" },
            }) ?? throw new InvalidOperationException("couldn't extract the Mapset Verifier backend");

            await extract.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

            return findServer(directory) ?? throw new InvalidOperationException($"couldn't find the backend in {app}, set the path of the backend executable instead");
        }

        private static string? findServer(string directory)
            => Directory.EnumerateFiles(directory, "MapsetVerifier*", SearchOption.AllDirectories).FirstOrDefault(f => Path.GetFileName(f) is "MapsetVerifier" or "MapsetVerifier.exe");

        private static string? describeError(string body)
        {
            try
            {
                var error = Newtonsoft.Json.Linq.JObject.Parse(body);
                return error.Value<string>("details") ?? error.Value<string>("message");
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Writes the files of a set to an empty folder (it's emptied first, files left over from earlier runs would be reported as unused).
        /// </summary>
        public static void WriteFolder(string directory, IEnumerable<(string Name, Func<Stream> Open)> files)
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);

            directory = Directory.CreateDirectory(directory).FullName;

            foreach (var (name, open) in files)
            {
                string path = Path.GetFullPath(Path.Combine(directory, name));

                // file names come from the archive the set was imported from.
                if (!path.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                    continue;

                Directory.CreateDirectory(Path.GetDirectoryName(path)!);

                using (var source = open())
                using (var target = File.Create(path))
                    source.CopyTo(target);
            }
        }
    }
}
