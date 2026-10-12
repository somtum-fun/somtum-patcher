using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

namespace OsuPatcher.UI
{
    public partial class MainWindow : Window
    {
        private static readonly Brush ErrorBrush   = new SolidColorBrush(Color.FromRgb(0xF2, 0x64, 0x5A));
        private static readonly Brush OkBrush      = new SolidColorBrush(Color.FromRgb(0x7E, 0xD3, 0x21));
        private static readonly Brush NeutralBrush = new SolidColorBrush(Color.FromRgb(0xB0, 0xB0, 0xB0));

        private readonly Config _config = Config.Load();
        private bool _busy;

        public MainWindow()
        {
            InitializeComponent();
            RefreshStatus();
        }

        private void RefreshStatus()
        {
            if (_busy) return;
            var osu = InjectorCore.FindRunningOsu();
            LocateLink.Visibility = Visibility.Collapsed;

            // One label suffix for both states — tells the user whether pkms (the anti-cheat) will
            // also be injected on launch. "no pkms" means the patcher was built without the AC
            // embedded; bancho will drop the session once the handshake grace runs out. The
            // standalone single-exe artifact has pkms embedded and loads it via the Runtime even
            // when AcAvailable is false (no sibling pkms_injector.exe), so HasPkms (not
            // AcAvailable) is the right signal for this label.
            string acSuffix = InjectorCore.HasPkms ? " (with pkms)" : " (no pkms)";
            switch (osu.State)
            {
                case InjectorCore.OsuState.RunningValid:
                    SetStatus("osu! is running — ready to inject" + acSuffix, OkBrush);
                    break;
                case InjectorCore.OsuState.RunningInvalid:
                    SetStatus($"osu! is running but not on -devserver {InjectorCore.DevServer}.\nClose it first.", ErrorBrush);
                    break;
                case InjectorCore.OsuState.NotRunning when KnownOsuPath() != null:
                    SetStatus("osu! is not running — Launch will start it" + acSuffix, NeutralBrush);
                    break;
                default:
                    SetStatus("osu! could not be found\nStart the game or locate it", ErrorBrush);
                    LocateLink.Visibility = Visibility.Visible;
                    break;
            }
        }

        private string KnownOsuPath()
        {
            if (!string.IsNullOrEmpty(_config.OsuPath) && File.Exists(_config.OsuPath))
                return _config.OsuPath;
            // Players who installed osu! on a different drive (e.g. E:\osu!) never get picked up
            // by the default-location lookup, but osu!'s installer registers its path under
            // HKEY_CLASSES_ROOT\osu!\shell\open\command as `"<drive>:\path\osu!.exe" "%1"`.
            // Reading that gives us the install path on whichever drive the player chose; we
            // persist it to _config so the next launch doesn't re-scan the registry.
            var registered = ResolveOsuPathFromRegistry();
            if (registered != null && File.Exists(registered))
            {
                _config.OsuPath = registered;
                _config.Save();
                return registered;
            }
            return null;
        }

        private static string ResolveOsuPathFromRegistry()
        {
            try
            {
                using (var key = Microsoft.Win32.Registry.ClassesRoot.OpenSubKey(@"osu!\shell\open\command"))
                {
                    if (key?.GetValue(null) is string cmd && !string.IsNullOrWhiteSpace(cmd))
                    {
                        // Format is `"C:\path\osu!.exe" "%1"` — pick the quoted first token.
                        if (cmd.Length > 0 && cmd[0] == '"')
                        {
                            int end = cmd.IndexOf('"', 1);
                            if (end > 1) return cmd.Substring(1, end - 1);
                        }
                        // Fallback: first whitespace-delimited token.
                        int sp = cmd.IndexOf(' ');
                        return sp > 0 ? cmd.Substring(0, sp) : cmd;
                    }
                }
            }
            catch { /* registry not accessible; fall back to the config entry */ }
            return null;
        }

        private async void OnInject(object sender, RoutedEventArgs e)
        {
            if (_busy) return;
            SetBusy(true);

            // Hide the window immediately after the user clicks Inject
            Hide();

            try
            {
                await Task.Run(() => RunInjectFlow(CancellationToken.None));
                Close();
            }
            catch (OperationCanceledException)
            {
                Show();
                SetBusy(false);
                RefreshStatus();
            }
            catch (Exception ex)
            {
                Show();
                SetBusy(false);
                SetStatus(ex.Message, ErrorBrush);
            }
        }

        private void RunInjectFlow(CancellationToken token)
        {
            var osu = InjectorCore.FindRunningOsu();

            if (osu.State == InjectorCore.OsuState.RunningInvalid)
                throw new Exception($"osu! is running but not on -devserver {InjectorCore.DevServer}. Close it first.");

            if (osu.State == InjectorCore.OsuState.RunningValid)
            {
                if (!string.IsNullOrEmpty(osu.ExecutablePath))
                {
                    _config.OsuPath = osu.ExecutablePath;
                    _config.Save();
                }
                InjectorCore.Inject(osu.Pid);
                return;
            }

            // Not running — need a path
            var path = KnownOsuPath() ?? AskForOsuPathOnUiThread();
            if (path == null) throw new OperationCanceledException();

            _config.OsuPath = path;
            _config.Save();

            // When pkms is bundled, LaunchOsu goes through pkms_injector.exe which handles osu!
            // startup, pkms injection AND the managed patcher injection in one native process. We
            // then only need to wait for it to exit; it has already injected both by then.
            var proc = InjectorCore.LaunchOsu(path);
            if (InjectorCore.AcAvailable)
            {
                proc.WaitForExit();
                if (proc.ExitCode != 0)
                    throw new Exception($"osu!somtum injector exited with code {proc.ExitCode}.");
                return;
            }
            var pid  = InjectorCore.WaitForInjectable(proc, token, TimeSpan.FromSeconds(60));
            InjectorCore.Inject(pid);
        }

        private string AskForOsuPathOnUiThread()
        {
            return Dispatcher.Invoke(() =>
            {
                var dialog = new OpenFileDialog
                {
                    Title  = "Locate osu!.exe",
                    Filter = "osu! executable (osu!.exe)|osu!.exe|Executables (*.exe)|*.exe",
                    FileName = "osu!.exe",
                };
                return dialog.ShowDialog(this) == true ? dialog.FileName : null;
            });
        }

        private void OnLocate(object sender, RoutedEventArgs e)
        {
            var path = AskForOsuPathOnUiThread();
            if (path == null) return;
            _config.OsuPath = path;
            _config.Save();
            RefreshStatus();
        }

        private void SetBusy(bool busy)
        {
            _busy = busy;
            InjectButton.IsEnabled = !busy;
        }

        private void SetStatus(string message, Brush brush)
        {
            StatusText.Text       = message;
            StatusText.Foreground = brush;
        }

        private void OnWindowDrag(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        }

        private void OnClose(object sender, RoutedEventArgs e) => Close();
    }
}
