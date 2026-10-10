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
            // also be injected on launch. "no pkms" means they are running the bare patcher without
            // the AC bundle; bancho will drop the session once the handshake grace runs out.
            string acSuffix = InjectorCore.AcAvailable ? " (with pkms)" : " (no pkms)";
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

        private string KnownOsuPath() =>
            !string.IsNullOrEmpty(_config.OsuPath) && File.Exists(_config.OsuPath) ? _config.OsuPath : null;

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
