using System;
using System.IO;
using System.Reflection;
using System.Windows;

namespace OsuPatcher.UI
{
    public partial class App : Application
    {
        private const string InjectOnlyPrefix = "--inject-only=";
        private const string LaunchAndInjectPrefix = "--launch-and-inject=";
        private const string LaunchPrefix = "--launch=";
        private const string WithPatcherFlag = "--with-patcher";

        static App()
        {
            AppDomain.CurrentDomain.AssemblyResolve += (_, args) =>
            {
                var name = new AssemblyName(args.Name).Name + ".dll";
                var asm  = Assembly.GetExecutingAssembly();
                // Embedded as "OsuPatcher.UI.<name>" (default manifest resource name)
                foreach (var resource in asm.GetManifestResourceNames())
                {
                    if (!resource.EndsWith(name, StringComparison.OrdinalIgnoreCase)) continue;
                    using (var stream = asm.GetManifestResourceStream(resource))
                    {
                        var bytes = new byte[stream.Length];
                        stream.Read(bytes, 0, bytes.Length);
                        return Assembly.Load(bytes);
                    }
                }
                return null;
            };
        }

        // Headless entry: --inject-only=<pid>
        //   The combined pad-krapow injector launches osu! with pkms.dll attached,
        //   waits for the AC subsystems to initialise, then spawns us in this mode
        //   to attach the managed patcher runtime without ever showing a window.
        //   Prints "ok" / "error: ..." so the parent can surface failures.
        private void OnStartup(object sender, StartupEventArgs e)
        {
            // Single-exe deployment: before any UI work or headless injection, extract the
            // native AC files (pkms.dll + pkms_injector.exe) next to this exe. They're
            // embedded as manifest resources at build time by OsuPatcher.UI.csproj; the
            // launcher and the headless injector both expect them on disk. We extract only
            // when the on-disk file is missing or smaller than the embedded copy, so
            // nothing races between concurrent launches of the same patcher.
            ExtractNativePayload();

            string launchOsuPath = null;
            string startOsuPath = null;
            bool withPatcher = false;
            foreach (var arg in e.Args)
            {
                if (arg == null) continue;

                if (arg.StartsWith(InjectOnlyPrefix, StringComparison.Ordinal))
                {
                    var pidText = arg.Substring(InjectOnlyPrefix.Length);
                    if (!uint.TryParse(pidText, out var pid) || pid == 0)
                    {
                        Console.Error.WriteLine($"error: --inject-only expects a positive decimal pid, got '{pidText}'");
                        Shutdown(1);
                        return;
                    }

                    try
                    {
                        InjectorCore.Inject(pid);
                        Console.Out.WriteLine("ok");
                        Shutdown(0);
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"error: {ex.Message}");
                        ShowIfFrozen(ex);
                        Shutdown(1);
                    }
                    return;
                }

                if (arg.StartsWith(LaunchAndInjectPrefix, StringComparison.Ordinal))
                    launchOsuPath = arg.Substring(LaunchAndInjectPrefix.Length);
                else if (arg.StartsWith(LaunchPrefix, StringComparison.Ordinal))
                    startOsuPath = arg.Substring(LaunchPrefix.Length);
                else if (string.Equals(arg, WithPatcherFlag, StringComparison.Ordinal))
                    withPatcher = true;
            }

            // Headless --launch-and-inject=<osu! path> [--with-patcher]:
            //   Launches osu!, waits for its window, then HoLLy-injects the patcher runtime (which
            //   LoadLibrary's pkms in-process). This is the Wine-friendly route the Somtum Launcher
            //   uses on Linux in place of pkms_injector.exe (whose CreateRemoteThread → LoadLibraryW
            //   deadlocks against osu!'s loader on Wine). --with-patcher is accepted but currently
            //   a no-op: when the Launcher asks for the AC alone we still load the runtime so pkms
            //   gets attached — the Harmony patches it installs are the "patcher" part; staff can
            //   wire a tri-state later if there's demand to skip them.
            // Headless --launch=<osu! path> [--with-patcher]: starts osu! the way the Play button
            // does, for launchers. On Wine (or without the native injector) it is --launch-and-inject.
            if (startOsuPath != null && InjectorCore.AcAvailable)
            {
                try
                {
                    using (var injector = InjectorCore.LaunchOsuWithInjector(startOsuPath, withPatcher))
                    {
                        if (!injector.WaitForExit((int)TimeSpan.FromMinutes(3).TotalMilliseconds))
                        {
                            Console.Error.WriteLine("error: timed out");
                            Shutdown(1);
                            return;
                        }
                        Shutdown(injector.ExitCode);
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"error: {ex.Message}");
                    Shutdown(1);
                }
                return;
            }
            if (startOsuPath != null)
                launchOsuPath = startOsuPath;

            _ = withPatcher;
            if (launchOsuPath != null)
            {
                try
                {
                    var proc = InjectorCore.LaunchOsuPlain(launchOsuPath, out var pipeDone);
                    var pid = InjectorCore.WaitForInjectable(proc, System.Threading.CancellationToken.None, TimeSpan.FromMinutes(2));
                    InjectorCore.Inject(pid);
                    // Headless mode: wait for the token handoff before shutting down.
                    pipeDone?.Wait(TimeSpan.FromSeconds(12));
                    Console.Out.WriteLine("ok");
                    Shutdown(0);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"error: {ex.Message}");
                    ShowIfFrozen(ex);
                    Shutdown(1);
                }
                return;
            }

            // Interactive mode: original behaviour.
            new MainWindow().Show();
        }

        // The command-line modes have no window, and the injector only prints our stderr: a frozen osu!
        // that was closed is worth a dialog, so the player knows why the game vanished.
        private static void ShowIfFrozen(Exception ex)
        {
            if (ex is InjectorCore.OsuFrozenException)
                MessageBox.Show(ex.Message, "Somtum Patcher", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private static void ExtractNativePayload()
        {
            foreach (var name in new[] { "pkms.dll", "pkms_injector.exe" })
            {
                try
                {
                    var dest = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, name);
                    var asm = Assembly.GetExecutingAssembly();
                    using (var stream = asm.GetManifestResourceStream(name))
                    {
                        if (stream == null) continue; // dev build without the native payload
                        // Skip rewriting when the on-disk file already matches the embedded size,
                        // so repeated launches don't thrash the AC files (and don't fail if the
                        // DLL is currently loaded by a running osu!).
                        if (File.Exists(dest) && new FileInfo(dest).Length == stream.Length)
                            continue;
                        var tmp = dest + ".tmp";
                        using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
                            stream.CopyTo(fs);
                        // Move-with-replace. If dest is locked (osu! running pkms.dll), fall back
                        // to the tmp file this run — ok, we were going to use the on-disk copy
                        // anyway. Next clean launch will finish the swap.
                        try { File.Delete(dest); } catch { }
                        try { File.Move(tmp, dest); } catch { try { File.Delete(tmp); } catch { } }
                    }
                }
                catch (Exception ex)
                {
                    // Non-fatal: we fall back to looking for a loose copy next to the exe.
                    Console.Error.WriteLine($"warning: could not extract {name}: {ex.Message}");
                }
            }
        }
    }
}
