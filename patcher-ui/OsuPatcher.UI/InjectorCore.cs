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
        public const string DevServer = "somtum.fun";

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
                return Process.Start(psi) ?? throw new Exception("Failed to start the osu!somtum injector.");
            }

            // Fallback (no AC next to us): the previous patcher-only flow. Still works, but osu! runs
            // without pkms, so bancho drops the session once the handshake grace expires.
            var plain = new ProcessStartInfo
            {
                FileName = osuExePath,
                WorkingDirectory = Path.GetDirectoryName(osuExePath),
                Arguments = $"-devserver {DevServer}",
                UseShellExecute = false,
            };
            return Process.Start(plain) ?? throw new Exception("Failed to start osu!.");
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

        /// <summary>True when the AC injector is bundled: used by the UI to show "with pkms" status.</summary>
        public static bool AcAvailable => FindAcInjector() != null;

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
            using (var proc = new InjectableProcess(pid))
                proc.Inject(runtimePath, RuntimeEntryType, "Initialize");
        }
    }
}
