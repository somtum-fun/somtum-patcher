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
        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

        // Install watchdog: if the patches aren't all in within this long of Initialize starting, they're
        // undone and osu! carries on unpatched (1.5.27 hung osu! mid-install with nothing to show for it).
        // A cold Wine start is a lot slower, so it gets longer.
        private static readonly TimeSpan InstallTimeout = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan InstallTimeoutWine = TimeSpan.FromSeconds(30);
        private static readonly System.Threading.ManualResetEvent _installDone = new System.Threading.ManualResetEvent(false);
        private static volatile bool _installTimedOut;

        private static bool IsWine()
        {
            var ntdll = GetModuleHandleW("ntdll.dll");
            return ntdll != IntPtr.Zero && GetProcAddress(ntdll, "wine_get_version") != IntPtr.Zero;
        }
        private const uint LOAD_WITH_ALTERED_SEARCH_PATH = 0x00000008;
        private const string PkmsLaunchTokenEnv = "PKMS_LAUNCH_TOKEN";

        // Loads pkms.dll in-process. patcher-ui extracts it next to our DLL in Config.Directory;
        // on Wine (and on Windows when pkms_injector.exe timed out) this is how the AC reaches
        // osu! — a plain in-process LoadLibrary, no cross-process CreateRemoteThread. Silent if
        // pkms.dll is missing (dev build without AC) or already loaded (re-inject).
        //
        // Launch token: pkms refuses to run without PKMS_LAUNCH_TOKEN set in the environment.
        // patcher-ui (InjectorCore.ApplyLaunchToken) now fetches a server-signed one via
        // /anticheat/launch-token and plants it as a child-process env var before launch. If
        // that reached us (env var already set, server-signed shape nonce:unix:sig), KEEP IT —
        // overwriting with a random 32-byte-hex would downgrade pkms to the legacy accept path
        // and the server-side RequireLaunchToken flip could never catch skips. Only fall back
        // to the random form when the env is empty (patcher-ui couldn't reach the server, or
        // this isn't a patcher-UI launch).
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

                // Only seed a legacy random token if patcher-ui didn't plant a server-signed one.
                var existing = Environment.GetEnvironmentVariable(PkmsLaunchTokenEnv);
                if (string.IsNullOrEmpty(existing))
                {
                    var bytes = new byte[32];
                    using (var rng = new System.Security.Cryptography.RNGCryptoServiceProvider())
                        rng.GetBytes(bytes);
                    var hex = new System.Text.StringBuilder(bytes.Length * 2);
                    foreach (var b in bytes) hex.Append(b.ToString("x2"));
                    SetEnvironmentVariableW(PkmsLaunchTokenEnv, hex.ToString());
                    Logger.logFile("[pkms] seeded legacy launch token (no server-signed one in env)");
                }
                else
                {
                    Logger.logFile("[pkms] using server-signed launch token from patcher-ui");
                }

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
                StartInstallWatchdog();

                // Per-type patching: one broken patch (shifted IL from a new obfuscation pass, a
                // removed method, a changed signature) throws SEHException inside Harmony's
                // UpdateWrapper and would, under PatchAll, abort the whole batch — the player loses
                // every patch. Iterating [HarmonyPatch] types individually with a try/catch means
                // the working patches still install and the broken ones get logged for the next
                // rebase pass. Partial RX/AP beats a crashed game.
                int installed = 0, skipped = 0;
                // Names of patch classes that didn't install, shown later as per-feature toasts
                // so the player knows which thing is off instead of a vague "some features broke".
                var skippedFeatures = new System.Collections.Generic.List<string>();
                foreach (var type in typeof(Main).Assembly.GetTypes())
                {
                    // The watchdog gave up on us and undid what was in: install nothing more.
                    if (_installTimedOut)
                        break;
                    if (type.GetCustomAttributes(typeof(HarmonyPatch), inherit: true).Length == 0)
                        continue;

                    // Snapshot what WE have patched before this class attempts its install, so a
                    // throw mid-install can be rolled back cleanly. PatchClassProcessor.Patch()
                    // isn't guaranteed to be atomic across multiple [HarmonyPatch] members in one
                    // class — if one succeeds and the next throws, the first is left attached.
                    // Each of our classes currently targets a single method, so this is mostly
                    // belt-and-braces; new patches that cover more than one target get rollback
                    // for free. GetPatchedMethods() returns ALL harmony-patched methods across all
                    // instances in-process; we filter to ours via GetPatchInfo().Owners afterwards.
                    var beforeOurs = new System.Collections.Generic.HashSet<System.Reflection.MethodBase>(
                        Harmony.GetAllPatchedMethods().Where(m =>
                        {
                            var info = Harmony.GetPatchInfo(m);
                            return info != null && info.Owners.Contains(Harmony.Id);
                        }));

                    try
                    {
                        // One line per class before it installs: if osu! ever hangs while patching
                        // (as 1.5.27 did on stable, with no clue which patch), the last line names it.
                        Logger.logFile($"[patcher] installing {type.Name}");
                        new PatchClassProcessor(Harmony, type).Patch();
                        installed++;

                        // Attach a runtime safety finalizer to every target this class patched: if
                        // a Prefix/Postfix/original throws at runtime (e.g. a field reference we
                        // thought was stable actually drifted under a new obfuscation pass), the
                        // finalizer logs it, auto-unpatches THAT single method, and swallows the
                        // exception so osu! doesn't crash. Scope is per-method, not per-id: a
                        // false-positive from one patch doesn't disable the others, and a fresh
                        // launch re-installs the full set.
                        //
                        // Finalizers run AFTER prefix/original/postfix, so __exception may be from
                        // our code OR from osu!'s own method bubbling through. We can't tell them
                        // apart. We still unpatch: if the fault was ours, osu! runs clean next
                        // call; if it was osu!'s, we lose one feature until next launch while osu!
                        // continues to throw the same exception at the caller — net result is one
                        // disabled patch, which is strictly better than osu!'s "An error occurred"
                        // dialog from an unhandled exception in a prefix/postfix.
                        var finalizerMethod = typeof(Main).GetMethod(
                            nameof(SafetyFinalizer),
                            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
                        foreach (var m in Harmony.GetAllPatchedMethods().ToList())
                        {
                            if (beforeOurs.Contains(m))
                                continue;
                            var info = Harmony.GetPatchInfo(m);
                            if (info == null || !info.Owners.Contains(Harmony.Id))
                                continue;
                            // Skip if a finalizer owned by us is already present (defensive: don't
                            // stack two safety wrappers if a patch class ever declares its own).
                            if (info.Finalizers.Any(p => p.owner == Harmony.Id))
                                continue;
                            Harmony.Patch(m, finalizer: new HarmonyMethod(finalizerMethod));
                        }
                    }
                    catch (Exception patchEx)
                    {
                        skipped++;
                        skippedFeatures.Add(FriendlyFeatureName(type.Name));
                        // File-only: these are developer-level diagnostics (obfuscation shifted the
                        // IL, our Harmony pattern needs a rebase). Players don't need the stack
                        // trace in a toast — they get a friendly summary below instead.
                        Logger.logFile($"[patcher] skipped {type.Name}: {patchEx.GetType().Name}: {patchEx.Message}");

                        // Roll back anything this class's Patch() may have attached before it
                        // threw: leaves osu! with its untouched IL for every target this class
                        // was about to cover, rather than a half-installed prefix/postfix running
                        // against an un-prefixed partner method.
                        try
                        {
                            foreach (var m in Harmony.GetAllPatchedMethods().ToList())
                            {
                                if (beforeOurs.Contains(m))
                                    continue;
                                var info = Harmony.GetPatchInfo(m);
                                if (info == null || !info.Owners.Contains(Harmony.Id))
                                    continue;
                                Harmony.Unpatch(m, HarmonyPatchType.All, Harmony.Id);
                                Logger.logFile($"[patcher] rolled back partial install on {m.DeclaringType?.Name}.{m.Name}");
                            }
                        }
                        catch (Exception rollbackEx)
                        {
                            Logger.logFile($"[patcher] rollback after {type.Name} failure also threw: {rollbackEx.GetType().Name}: {rollbackEx.Message}");
                        }
                    }
                }
                if (_installTimedOut)
                {
                    // Finished after all: undo anything that went in after the watchdog's pass. The
                    // watchdog has signalled ready and told the player already.
                    UndoAllPatches("late finish after the install timeout");
                    return 0;
                }
                _installDone.Set();
                Logger.logFile($"[patcher] installed {installed} patch(es), skipped {skipped}");
                SignalReady();

                // Always show the big green/yellow "initialized" banner — a skipped non-critical
                // patch (one RX/AP transpiler whose anchor shifted) is not a reason to hide the
                // success state. Players reading "some features aren't compatible" interpret the
                // whole AC as broken when actually most things still work. Pair the massive banner
                // with per-feature in-game toasts so there's a specific name to report.
                NotificationManager.ShowMessageMassive(
                    "somtum patcher is now initialized!",
                    5000,
                    NotificationManager.NotificationType.Warning
                );
                foreach (var feature in skippedFeatures)
                {
                    try
                    {
                        var color = AccessTools.TypeByName("Microsoft.Xna.Framework.Graphics.Color")
                            ?.GetProperty("Yellow", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                            ?.GetValue(null);
                        NotificationManager.ShowMessage(
                            $"Feature: {feature} has been disabled due to your osu! build. Please tell us on our Discord.",
                            color,
                            15000);
                    }
                    catch (Exception toastEx)
                    {
                        // A missing XNA Color at toast time (odd build or stripped assembly) shouldn't
                        // abort the whole post-init path — log and skip the per-feature toast only.
                        Logger.logFile($"[patcher] skipped-feature toast for '{feature}' failed: {toastEx.GetType().Name}: {toastEx.Message}");
                    }
                }
            }
            catch (Exception e)
            {
                // File-only: full exception trace is noise for players.
                Logger.logFile(e.ToString());
            }

            return 0;
        }

        /// <summary>
        /// Starts the install watchdog (<see cref="InstallTimeout"/>, <see cref="InstallTimeoutWine"/> on Wine): when
        /// the patches aren't all in by then, it undoes the ones that are, tells the anti-cheat we're done (osu!'s
        /// code is its own again, so its baselines are right), and tells the player their features are off.
        /// </summary>
        private static void StartInstallWatchdog()
        {
            var timeout = IsWine() ? InstallTimeoutWine : InstallTimeout;
            var watchdog = new System.Threading.Thread(() =>
            {
                if (_installDone.WaitOne(timeout))
                    return;

                _installTimedOut = true;
                Logger.logFile($"[patcher] install didn't finish within {timeout.TotalSeconds:0} s; undoing the patches");

                // Undo on its own thread: if the install is stuck inside Harmony, unpatching may wait on it too.
                var undo = new System.Threading.Thread(() => UndoAllPatches("install timeout")) { IsBackground = true };
                undo.Start();
                undo.Join(TimeSpan.FromSeconds(5));

                SignalReady();
                try
                {
                    NotificationManager.ShowMessage(
                        $"The Somtum Patcher couldn't finish setting up within {timeout.TotalSeconds:0} seconds, so its changes were undone: "
                        + "Relax/Autopilot, the pp counter and the Somtum options are off for this session. Restart osu! from the Patcher "
                        + "to try again, or switch osu! to the Cutting Edge release stream (Options > General > Updates).",
                        AccessTools.TypeByName("Microsoft.Xna.Framework.Graphics.Color")
                            ?.GetProperty("Orange", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                            ?.GetValue(null),
                        20000);
                }
                catch (Exception e)
                {
                    Logger.logFile($"[patcher] install-timeout message failed: {e.GetType().Name}: {e.Message}");
                }
            })
            {
                IsBackground = true,
                Name = "Somtum Patcher install watchdog",
            };
            watchdog.Start();
        }

        private static void UndoAllPatches(string why)
        {
            try
            {
                Harmony.UnpatchAll(Harmony.Id);
                Logger.logFile($"[patcher] undid every patch ({why})");
            }
            catch (Exception e)
            {
                Logger.logFile($"[patcher] undoing the patches ({why}) threw: {e.GetType().Name}: {e.Message}");
            }
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

        /// <summary>
        /// Finalizer attached by <see cref="Initialize"/> to every method we successfully patch.
        /// On any exception escaping the Prefix/original/Postfix chain, logs the fault, unpatches
        /// just this target under our Harmony id on a background thread (so we don't mutate the
        /// detour we're currently inside), and swallows the exception so the game doesn't surface
        /// an unhandled-exception dialog. Returning null tells Harmony to clear the exception;
        /// returning __exception would rethrow it.
        /// </summary>
        private static Exception SafetyFinalizer(Exception __exception, System.Reflection.MethodBase __originalMethod)
        {
            if (__exception == null) return null;
            try
            {
                Logger.logFile($"[patcher] runtime exception in patched {__originalMethod?.DeclaringType?.Name}.{__originalMethod?.Name}, auto-unpatching: {__exception.GetType().Name}: {__exception.Message}");
                var target = __originalMethod;
                if (target != null)
                {
                    // Defer the unpatch: we're currently inside the detour for `target`. Harmony's
                    // Unpatch is safe to call reentrantly, but doing it on a background thread
                    // keeps the current call's return path trivial and avoids any surprise with
                    // JIT swapping the method body while a wrapper frame is on the stack.
                    System.Threading.ThreadPool.QueueUserWorkItem(_ =>
                    {
                        try
                        {
                            Harmony.Unpatch(target, HarmonyPatchType.All, Harmony.Id);
                            Logger.logFile($"[patcher] unpatched {target.DeclaringType?.Name}.{target.Name} after runtime fault");
                        }
                        catch (Exception unpatchEx)
                        {
                            Logger.logFile($"[patcher] deferred unpatch of {target.DeclaringType?.Name}.{target.Name} threw: {unpatchEx.GetType().Name}: {unpatchEx.Message}");
                        }
                    });
                }
            }
            catch
            {
                // Last-resort: we must not let a logging-side exception re-throw inside the
                // finalizer and defeat the swallow. Nothing to log it with anyway.
            }
            return null;
        }

        /// <summary>
        /// Human-friendly name for a patch class, for the per-feature "disabled on this build"
        /// toast. Hard-coded map (not a reflection trick) so the shown text stays obvious to
        /// read in the game and in bug reports: a player saying "Feature: Relax miss handling
        /// has been disabled" is actionable, "RelaxMissPatch" less so.
        /// </summary>
        private static string FriendlyFeatureName(string patchClassName)
        {
            switch (patchClassName)
            {
                case "PatchRelaxMiss":            return "Relax miss handling";
                case "PatchRelaxComboBreak":      return "Relax combo-break prevention";
                case "PatchAutoSaveRelaxScores":  return "RX/AP auto-save scores";
                case "PatchTransition":           return "Fast menu transitions";
                case "PkmsUsernamePatch":         return "Anti-cheat username handoff";
                case "LocalisationPatch":         return "In-game localisation";
                case "OptionsPatch":              return "Somtum options menu";
                case "PerformanceCalculationPatch": return "Live pp counter (calculator)";
                case "PlayerUpdatePatch":         return "Live pp counter (per-frame)";
                case "PlayerOnLoadCompletePatch": return "Live pp counter (on map load)";
                default:
                    // Fallback: strip trailing "Patch" and insert spaces at camel-case boundaries.
                    var name = patchClassName.EndsWith("Patch") ? patchClassName.Substring(0, patchClassName.Length - "Patch".Length) : patchClassName;
                    var sb = new System.Text.StringBuilder(name.Length + 4);
                    for (int i = 0; i < name.Length; i++)
                    {
                        if (i > 0 && char.IsUpper(name[i]) && !char.IsUpper(name[i - 1]))
                            sb.Append(' ');
                        sb.Append(name[i]);
                    }
                    return sb.ToString();
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
