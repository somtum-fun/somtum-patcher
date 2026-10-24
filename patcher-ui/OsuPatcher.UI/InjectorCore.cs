using System;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Threading;
using HoLLy.ManagedInjector;

namespace OsuPatcher.UI
{
    internal static class InjectorCore
    {
#if SOMTUM_DEV
        public const string DevServer = "blueskychan.dev";
#else
        public const string DevServer = "somtum.fun";
#endif

        private const string RuntimeDllName = "OsuPatcher.Runtime.dll";
        private const string NativePpDllName = "akatsuki_pp_ffi.dll";
        private const string RuntimeEntryType = "OsuPatcher.Runtime.Main";

        public enum OsuState { NotRunning, RunningValid, RunningInvalid }

        public struct OsuProcess
        {
            public OsuState State;
            public uint Pid;
            public string ExecutablePath;
        }

        public static OsuProcess FindRunningOsu()
        {
            using (var mgmt = new ManagementClass("Win32_Process"))
            using (var instances = mgmt.GetInstances())
            {
                foreach (ManagementObject proc in instances)
                {
                    using (proc)
                    {
                        if ((string)proc["Name"] != "osu!.exe") continue;

                        var pid = (uint)proc["ProcessId"];
                        var path = proc["ExecutablePath"] as string;
                        var cli = (proc["CommandLine"] as string) ?? "";
                        var parts = cli.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

                        // valid: launched with -devserver <non-bancho>
                        bool valid = parts.Length >= 3 &&
                                     string.Equals(parts[1], "-devserver", StringComparison.OrdinalIgnoreCase) &&
                                     parts[2].Length > 3 &&
                                     !string.Equals(parts[2], "ppy.sh", StringComparison.OrdinalIgnoreCase);

                        return new OsuProcess { State = valid ? OsuState.RunningValid : OsuState.RunningInvalid, Pid = pid, ExecutablePath = path };
                    }
                }
            }
            return new OsuProcess { State = OsuState.NotRunning };
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true, SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string procName);
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        // True when running under Wine. Native CreateRemoteThread → LoadLibraryW (pkms_injector's
        // mechanism) deadlocks against osu!'s loader on Wine, so on Wine we skip pkms_injector.exe
        // entirely and let HoLLy inject the managed patcher runtime, which in turn LoadLibrary's
        // pkms.dll in-process. Both AC and patcher still end up in osu! from one user action, just
        // via one code path instead of two.
        public static bool IsWine()
        {
            try
            {
                var h = GetModuleHandle("ntdll.dll");
                return h != IntPtr.Zero && GetProcAddress(h, "wine_get_version") != IntPtr.Zero;
            }
            catch { return false; }
        }

        public static Process LaunchOsu(string osuExePath)
        {
            RemoveRequireUpdateMarker(osuExePath);
            // Prefer pkms_injector.exe next to us: it starts osu!, injects pkms.dll (the anti-cheat),
            // and then re-launches us headlessly so the managed patcher is loaded next. Both the AC
            // and the patcher end up in one osu! process from one user action. Not on Wine though —
            // CreateRemoteThread(LoadLibraryW) deadlocks there, so Wine goes straight to the plain
            // launch path and relies on OsuPatcher.Runtime.Main.LoadPkmsIfPresent() to attach pkms.
            var acInjector = IsWine() ? null : FindAcInjector();
            if (acInjector != null)
            {
                var psi = new ProcessStartInfo
                {
                    FileName = acInjector,
                    Arguments = $"\"{osuExePath}\" --with-patcher",
                    WorkingDirectory = Path.GetDirectoryName(acInjector),
                    UseShellExecute = false,
                };
                ApplyLaunchToken(psi, osuExePath);
                return Process.Start(psi) ?? throw new Exception("Failed to start the osu!somtum injector.");
            }

            return LaunchOsuPlain(osuExePath);
        }

        /// <summary>
        /// Launches osu! with just -devserver, no cross-process native injector. pkms gets attached
        /// in-process by OsuPatcher.Runtime.Main.LoadPkmsIfPresent() after HoLLy injects the runtime.
        /// Used by the Wine path (both from LaunchOsu's own IsWine branch and from App.xaml.cs's
        /// --launch-and-inject headless entry).
        /// </summary>
        public static Process LaunchOsuPlain(string osuExePath) => LaunchOsuPlain(osuExePath, out _);

        /// <summary>
        /// LaunchOsuPlain plus a handle headless callers can wait on before Application.Shutdown:
        /// the pipe-server thread is background and would die with the process. See App.xaml.cs's
        /// --launch-and-inject entry for the pattern. <paramref name="pipeDone"/> is null when no
        /// token was issued (blank cfg / server offline — pkms will have nothing to receive anyway).
        /// Callers that always use interactive mode can keep calling the one-arg overload.
        /// </summary>
        public static Process LaunchOsuPlain(string osuExePath, out ManualResetEventSlim pipeDone)
        {
            RemoveRequireUpdateMarker(osuExePath);
            var plain = new ProcessStartInfo
            {
                FileName = osuExePath,
                WorkingDirectory = Path.GetDirectoryName(osuExePath),
                Arguments = $"-devserver {DevServer}",
                UseShellExecute = false,
            };
            var pendingToken = ApplyLaunchToken(plain, osuExePath, usePipeHandoff: true);
            var proc = Process.Start(plain) ?? throw new Exception("Failed to start osu!.");
            // Serve the token over a named pipe keyed on osu!'s pid so pkms can pick it up
            // without a long-lived env var in osu!'s PEB. pkms will fall through to the env
            // (also set by ApplyLaunchToken as a safety net) if the pipe never arrives.
            pipeDone = !string.IsNullOrEmpty(pendingToken)
                ? LaunchTokenPipeServer.StartOneShot(proc.Id, pendingToken, TimeSpan.FromSeconds(10))
                : null;
            return proc;
        }

        /// <summary>
        /// 0.9.0 §1: fetch a server-signed launch token and plant it in the child env so pkms can
        /// forward it in /anticheat/key. Also stamps PKMS_USERNAME so pkms doesn't race against
        /// osu!'s cfg rewrites and come up with a different name than the one we signed the token
        /// against. Both envs are no-ops without a running server, so the method is safe to call
        /// unconditionally on every launch path.
        ///
        /// 0.9.0 §3: when <paramref name="usePipeHandoff"/> is true, also sets PKMS_LAUNCH_PIPE=1
        /// so pkms knows to connect to the per-pid handoff pipe (served by the caller after
        /// Process.Start) instead of reading the token from the env. The caller is responsible
        /// for actually standing up the pipe — this method only returns the token so the caller
        /// can hand it off out-of-band. The env PKMS_LAUNCH_TOKEN is still set as a safety net:
        /// if the pipe fails to come up, pkms logs it and refuses to run (see verify_launch_token
        /// in net.c), so the env fallback here only matters for a legacy pkms that doesn't speak
        /// the pipe protocol.
        /// </summary>
        /// <returns>The token issued (so the caller can serve it over the pipe), or null when
        /// no token was obtained (blank cfg, server unreachable).</returns>
        private static string ApplyLaunchToken(ProcessStartInfo psi, string osuExePath, bool usePipeHandoff = false)
        {
            var username = AnticheatClient.ReadOsuUsername(osuExePath);
            if (string.IsNullOrEmpty(username))
                return null;   // first ever launch, blank cfg: no token to issue, pkms will go without.
            var token = AnticheatClient.IssueLaunchToken(username);
            if (string.IsNullOrEmpty(token))
                return null;   // server unreachable; fail open — RequireLaunchToken decides server-side.
            psi.EnvironmentVariables["PKMS_LAUNCH_TOKEN"] = token;
            psi.EnvironmentVariables["PKMS_USERNAME"] = username;
            if (usePipeHandoff)
                psi.EnvironmentVariables["PKMS_LAUNCH_PIPE"] = "1";
            return token;
        }

        /// <summary>
        /// osu!stable drops a <c>.require_update</c> marker next to osu!.exe when it wants ppy's
        /// updater to run on the next launch (triggered by a corrupt file, a changed file hash, or
        /// a stream switch like cuttingedge→stable). Running that against our prepared install
        /// would clobber the patched files and pull an unexpected build from ppy's CDN — possibly
        /// one we don't support, possibly without pkms.dll next to osu!.exe. We delete the marker
        /// before every launch so osu! stays on the files we laid down. Safe to call multiple
        /// times on the same path and from either the native-injector or the HoLLy branch.
        /// </summary>
        private static void RemoveRequireUpdateMarker(string osuExePath)
        {
            try
            {
                var osuDir = Path.GetDirectoryName(osuExePath);
                if (string.IsNullOrEmpty(osuDir)) return;
                var marker = Path.Combine(osuDir, ".require_update");
                if (File.Exists(marker))
                    File.Delete(marker);
            }
            catch
            {
                // Non-fatal: if the marker is locked (antivirus scanning, osu! currently open) we
                // press on — osu!'s updater won't take exclusive action while the file is still in
                // use, and we'll catch it on the next clean launch.
            }
        }

        /// <summary>pkms_injector.exe sitting next to this exe, or null when the AC isn't bundled.</summary>
        public static string FindAcInjector()
        {
            try
            {
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                var candidate = Path.Combine(baseDir, "pkms_injector.exe");
                if (File.Exists(candidate)) return candidate;
                // The combined bundle ships it as osu-ac-injector.exe (one friendlier filename for the folder).
                candidate = Path.Combine(baseDir, "osu-ac-injector.exe");
                if (File.Exists(candidate)) return candidate;
            }
            catch
            {
                // ignore; we only use this to decide between AC+patcher and patcher-only launches
            }
            return null;
        }

        /// <summary>True when the AC injector is bundled AND usable: on Wine we never run
        /// pkms_injector.exe (it deadlocks), so AcAvailable is false there and MainWindow falls
        /// through to the HoLLy Inject path (which attaches pkms in-process via the Runtime).
        /// This gates the launch BRANCH (native injector vs HoLLy); it does NOT describe whether
        /// pkms will attach — pkms attaches in both branches. Use <see cref="HasPkms"/> for that.</summary>
        public static bool AcAvailable => !IsWine() && FindAcInjector() != null;

        /// <summary>True when pkms.dll is reachable (either as a sibling file picked up by the
        /// native injector, OR as an embedded resource the Runtime's LoadPkmsIfPresent() will
        /// LoadLibrary in-process). The standalone single-exe artifact has it embedded, so this
        /// is true there even though <see cref="AcAvailable"/> is false. UI labels use this.</summary>
        public static bool HasPkms
        {
            get
            {
                try
                {
                    if (File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "pkms.dll")))
                        return true;
                    var asm = System.Reflection.Assembly.GetExecutingAssembly();
                    using (var s = asm.GetManifestResourceStream("pkms.dll"))
                        return s != null;
                }
                catch { return false; }
            }
        }

        public static uint WaitForInjectable(Process started, CancellationToken token, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                token.ThrowIfCancellationRequested();

                if (started.HasExited)
                {
                    var running = FindRunningOsu();
                    if (running.State == OsuState.RunningValid) return running.Pid;
                    if (running.State == OsuState.RunningInvalid)
                        throw new Exception($"osu! is already running but not on -devserver {DevServer}.");
                    throw new Exception("osu! exited before it could be injected.");
                }

                started.Refresh();
                if (started.MainWindowHandle != IntPtr.Zero)
                {
                    Thread.Sleep(1500);
                    return (uint)started.Id;
                }
                Thread.Sleep(200);
            }
            throw new Exception("Timed out waiting for osu! to start.");
        }

        /// <summary>
        /// Extracts the embedded runtime DLLs to %AppData%\osu-patcher\ and injects the runtime.
        /// </summary>
        public static void Inject(uint pid)
        {
            var dir = Config.Directory;
            System.IO.Directory.CreateDirectory(dir);

            var asm = System.Reflection.Assembly.GetExecutingAssembly();
            // pkms.dll is extracted here too so OsuPatcher.Runtime.Main.LoadPkmsIfPresent()
            // can pick it up by path. On the pkms_injector.exe path this write is harmless —
            // pkms is already in osu! from the injector run, and the Runtime's LoadLibrary of
            // an already-loaded DLL is a no-op (returns the existing handle).
            foreach (var name in new[] { RuntimeDllName, "0Harmony.dll", NativePpDllName, "pkms.dll" })
            {
                using (var stream = asm.GetManifestResourceStream(name))
                {
                    if (stream == null)
                        throw new Exception($"Embedded resource '{name}' not found.");
                    var dest = Path.Combine(dir, name);
                    try
                    {
                        using (var fs = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.Read))
                            stream.CopyTo(fs);
                    }
                    catch (IOException) { /* file locked by previous injection — existing copy is fine */ }
                }
            }

            var runtimePath = Path.Combine(dir, RuntimeDllName);

            // The runtime's Initialize runs inside osu! and Inject waits for it. The runtime has its own
            // install watchdog (10 s, 30 s on Wine) that undoes its patches when they don't all go in, so
            // past that, plus some slack, we stop waiting. If osu! itself has stopped responding by then,
            // it's closed and the player is told why (1.5.27 left a frozen osu! behind with no message).
            Exception injectError = null;
            var injecting = new Thread(() =>
            {
                try
                {
                    using (var proc = new InjectableProcess(pid))
                        proc.Inject(runtimePath, RuntimeEntryType, "Initialize");
                }
                catch (Exception e)
                {
                    injectError = e;
                }
            })
            {
                IsBackground = true,
                Name = "Somtum Patcher inject",
            };
            injecting.Start();

            var deadline = TimeSpan.FromSeconds(IsWine() ? 30 : 10) + TimeSpan.FromSeconds(10);
            if (injecting.Join(deadline))
            {
                if (injectError != null)
                    throw injectError;
                return;
            }

            if (OsuFrozen(pid))
            {
                try
                {
                    Process.GetProcessById((int)pid).Kill();
                }
                catch (Exception)
                {
                    // already gone
                }

                throw new OsuFrozenException("osu! froze while the Somtum Patcher was setting up, so it has been closed. Start it again from the Patcher. "
                                    + "If it keeps happening, switch osu! to the Cutting Edge release stream (Options > General > Updates) and tell us on Discord.");
            }

            // osu! is fine: the runtime's watchdog undid its patches and told the player in the game.
        }

        /// <summary>osu! stopped responding while the runtime was installing, and was closed.</summary>
        public sealed class OsuFrozenException : Exception
        {
            public OsuFrozenException(string message)
                : base(message)
            {
            }
        }

        // Not responding twice, a second apart (a single check can catch a busy moment).
        private static bool OsuFrozen(uint pid)
        {
            for (int i = 0; i < 2; i++)
            {
                try
                {
                    using (var osu = Process.GetProcessById((int)pid))
                    {
                        if (osu.HasExited || osu.Responding)
                            return false;
                    }
                }
                catch (Exception)
                {
                    return false;
                }

                if (i == 0)
                    Thread.Sleep(1000);
            }

            return true;
        }
    }
}
