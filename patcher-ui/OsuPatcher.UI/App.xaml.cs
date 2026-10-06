using System;
using System.IO;
using System.Reflection;
using System.Windows;

namespace OsuPatcher.UI
{
    public partial class App : Application
    {
        private const string InjectOnlyPrefix = "--inject-only=";

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
            foreach (var arg in e.Args)
            {
                if (arg == null || !arg.StartsWith(InjectOnlyPrefix, StringComparison.Ordinal))
                    continue;

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
                    Shutdown(1);
                }
                return;
            }

            // Interactive mode: original behaviour.
            new MainWindow().Show();
        }
    }
}
