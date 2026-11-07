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

        // True when running under Wine (osu! starts slower there, so the install watchdog waits longer).
        public static bool IsWine()
        {
            try
            {
                var h = GetModuleHandle("ntdll.dll");
                return h != IntPtr.Zero && GetProcAddress(h, "wine_get_version") != IntPtr.Zero;
            }
            catch { return false; }
        }

        /// <summary>Starts osu! on -devserver; the caller waits for its window and injects the runtime.</summary>
        public static Process LaunchOsu(string osuExePath)
        {
            RemoveRequireUpdateMarker(osuExePath);
            var psi = new ProcessStartInfo
            {
                FileName = osuExePath,
                WorkingDirectory = Path.GetDirectoryName(osuExePath),
                Arguments = $"-devserver {DevServer}",
                UseShellExecute = false,
            };
            return Process.Start(psi) ?? throw new Exception("Failed to start osu!.");
        }

        /// <summary>
        /// osu!stable drops a <c>.require_update</c> marker next to osu!.exe when it wants ppy's
        /// updater to run on the next launch (triggered by a corrupt file, a changed file hash, or
        /// a stream switch like cuttingedge→stable). Running that against our prepared install
        /// would clobber the patched files and pull an unexpected build from ppy's CDN, possibly
        /// one we don't support. We delete the marker before every launch so osu! stays on the
        /// files we laid down. Safe to call more than once.
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
            foreach (var name in new[] { RuntimeDllName, "0Harmony.dll", NativePpDllName })
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
