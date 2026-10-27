// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Framework;
using osu.Framework.Development;
using osu.Framework.Logging;
using osu.Framework.Platform;
using SDL;

namespace osu.Desktop
{
    public static class Program
    {
        // The name used for this client's storage and IPC.
        // This intentionally differs from the official client, so that both can run alongside each other without sharing any state.
#if DEBUG
        private const string game_name = @"osu-tryhard-development";
#else
        private const string game_name = @"osu-tryhard";
#endif

        public const string FRIENDLY_GAME_NAME = @"osu!tryhard editor";

        [STAThread]
        public static void Main(string[] args)
        {
            if (OperatingSystem.IsWindows())
            {
                var windowsVersion = Environment.OSVersion.Version;

                // While .NET 8 only supports Windows 10 and above, running on Windows 7/8.1 may still work. We are limited by realm currently, as they choose to only support 8.1 and higher.
                // See https://www.mongodb.com/docs/realm/sdk/dotnet/compatibility/
                if (windowsVersion.Major < 6 || (windowsVersion.Major == 6 && windowsVersion.Minor <= 2))
                {
                    unsafe
                    {
                        SDL3.SDL_ShowSimpleMessageBox(SDL_MessageBoxFlags.SDL_MESSAGEBOX_ERROR,
                            "Your operating system is too old to run osu!"u8,
                            "This version of osu! requires at least Windows 8.1 to run.\n"u8
                            + "Please upgrade your operating system or consider using an older version of osu!.\n\n"u8
                            + "If you are running a newer version of windows, please check you don't have \"Compatibility mode\" turned on for osu!"u8, null);
                        return;
                    }
                }
            }

            // This is a safe default. Localised usages should specify lower values as required.
            AppDomain.CurrentDomain.SetData("REGEX_DEFAULT_MATCH_TIMEOUT", TimeSpan.FromMilliseconds(1000));

            var hostOptions = new HostOptions
            {
                // Only used to detect other running instances of this client. Never shared with the official client.
                IPCPipeName = game_name,
                FriendlyGameName = FRIENDLY_GAME_NAME,
            };

            using (DesktopGameHost host = Host.GetSuitableDesktopHost(game_name, hostOptions))
            {
                // we want to allow multiple instances to be started when in debug.
                if (!host.IsPrimaryInstance && !DebugUtils.IsDebugBuild)
                {
                    Logger.Log(@"Another instance of this client is already running.", LoggingTarget.Runtime, LogLevel.Error);
                    return;
                }

                host.Run(new OsuGameDesktop());
            }
        }
    }
}
