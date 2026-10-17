using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using HarmonyLib;
using OsuPatcher.Runtime.Utils;
using OsuPatcher.Runtime.Wrappers;

namespace OsuPatcher.Runtime
{
    public class Main
    {
        private static readonly Harmony Harmony = new Harmony("osu_patcher.ano");
#if SOMTUM_DEV
        private const string DefaultServer = "blueskychan.dev";
#else
        private const string DefaultServer = "somtum.fun";
#endif

        /// <summary>The server this build is for (the credit line in the options).</summary>
        internal const string DefaultServerName = DefaultServer;

        internal static string Server { get; private set; } = DefaultServer;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibraryExW(string path, IntPtr hReservedNull, uint flags);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetEnvironmentVariableW(string name, string value);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr GetModuleHandleW(string lpModuleName);
        private const uint LOAD_WITH_ALTERED_SEARCH_PATH = 0x00000008;
        private const string PkmsLaunchTokenEnv = "PKMS_LAUNCH_TOKEN";

        // Loads pkms.dll in-process. patcher-ui extracts it next to our DLL in Config.Directory;
        // on Wine (and on Windows when pkms_injector.exe timed out) this is how the AC reaches
        // osu! — a plain in-process LoadLibrary, no cross-process CreateRemoteThread. Silent if
        // pkms.dll is missing (dev build without AC) or already loaded (re-inject).
        //
        // Launch token: pkms refuses to run without PKMS_LAUNCH_TOKEN set in the environment
        // (pkms_injector.exe seeds it before CreateProcessW on the native path). We generate the
        // same shape of random hex here and set it on osu!'s process env block before loading
        // the DLL so verify_launch_token() in net.c passes. pkms clears the var itself right
        // after, so children osu! spawns don't inherit it.
        private static void LoadPkmsIfPresent()
        {
            try
            {
                // Must match patcher-ui's Config.Directory: the dev build keeps its own folder so a
                // dev pkms.dll is never picked up by the production patcher, or the reverse.
                var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
#if SOMTUM_DEV
                    "osu-patcher-dev");
#else
                    "osu-patcher");
#endif
                var dll = Path.Combine(dir, "pkms.dll");
                if (!File.Exists(dll))
                    return;

                // Already in osu! (the Somtum Launcher's or pkms_injector.exe's copy, from another
                // folder): a second copy would run beside it, each reporting the other as foreign.
                if (GetModuleHandleW("pkms.dll") != IntPtr.Zero)
                {
                    Logger.logFile("[pkms] already loaded");
                    return;
                }

                var bytes = new byte[32];
                using (var rng = new System.Security.Cryptography.RNGCryptoServiceProvider())
                    rng.GetBytes(bytes);
                var hex = new System.Text.StringBuilder(bytes.Length * 2);
                foreach (var b in bytes) hex.Append(b.ToString("x2"));
                SetEnvironmentVariableW(PkmsLaunchTokenEnv, hex.ToString());

                var h = LoadLibraryExW(dll, IntPtr.Zero, LOAD_WITH_ALTERED_SEARCH_PATH);
                Logger.logFile(h == IntPtr.Zero
                    ? $"[pkms] LoadLibraryExW failed (err={Marshal.GetLastWin32Error()})"
                    : "[pkms] loaded");
            }
            catch (Exception ex)
            {
                Logger.logFile($"[pkms] load error: {ex.Message}");
            }
        }

        public static int Initialize(string st)
        {
            try
            {
                LoadPkmsIfPresent();
                Server = ResolveServer(st);

                // Per-type patching: one broken patch (shifted IL from a new obfuscation pass, a
                // removed method, a changed signature) throws SEHException inside Harmony's
                // UpdateWrapper and would, under PatchAll, abort the whole batch — the player loses
                // every patch. Iterating [HarmonyPatch] types individually with a try/catch means
                // the working patches still install and the broken ones get logged for the next
                // rebase pass. Partial RX/AP beats a crashed game.
                int installed = 0, skipped = 0;
                foreach (var type in typeof(Main).Assembly.GetTypes())
                {
                    if (type.GetCustomAttributes(typeof(HarmonyPatch), inherit: true).Length == 0)
                        continue;
                    try
                    {
                        new PatchClassProcessor(Harmony, type).Patch();
                        installed++;
                    }
                    catch (Exception patchEx)
                    {
                        skipped++;
                        // File-only: these are developer-level diagnostics (obfuscation shifted the
                        // IL, our Harmony pattern needs a rebase). Players don't need the stack
                        // trace in a toast — they get a friendly summary below instead.
                        Logger.logFile($"[patcher] skipped {type.Name}: {patchEx.GetType().Name}: {patchEx.Message}");
                    }
                }
                Logger.logFile($"[patcher] installed {installed} patch(es), skipped {skipped}");
                SignalReady();

                NotificationManager.ShowMessageMassive(
                    skipped == 0
                        ? "somtum patcher is now initialized!"
                        : "Some patcher features aren't compatible with this osu! build. "
                          + "Try: launch normal osu! first, then relaunch with the Launcher; "
                          + "or switch osu! to the cuttingedge build. "
                          + "Still not working? Ask us on Discord.",
                    skipped == 0 ? 5000 : 12000,
                    NotificationManager.NotificationType.Warning
                );
            }
            catch (Exception e)
            {
                // File-only: full exception trace is noise for players.
                Logger.logFile(e.ToString());
            }

            return 0;
        }

        // Held for the whole session: the named event lives as long as one handle to it is open.
        private static System.Threading.EventWaitHandle _ready;

        /// <summary>
        /// Tells the anti-cheat our patches are in, so it can take its osu!.exe baselines now: it
        /// waits for this (the patcher's managed DLLs load from bytes and never show up in osu!'s
        /// module list, where it used to look for them). Either side may create the event first.
        /// </summary>
        private static void SignalReady()
        {
            try
            {
                var name = "Local\\SomtumPatcherReady-" + System.Diagnostics.Process.GetCurrentProcess().Id;
                _ready = new System.Threading.EventWaitHandle(false, System.Threading.EventResetMode.ManualReset, name);
                _ready.Set();
                Logger.logFile("[patcher] ready signalled");
            }
            catch (Exception e)
            {
                Logger.logFile($"[patcher] ready signal failed: {e.Message}");
            }
        }

        private static string ResolveServer(string injectedArgument)
        {
            var args = Environment.GetCommandLineArgs()
                .Concat((injectedArgument ?? string.Empty).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                .ToArray();

            for (int i = 0; i < args.Length; i++)
            {
                if (!string.Equals(args[i], "-devserver", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (i + 1 < args.Length && !string.IsNullOrWhiteSpace(args[i + 1]))
                    return args[i + 1].Trim();
            }

            return DefaultServer;
        }
    }
}
