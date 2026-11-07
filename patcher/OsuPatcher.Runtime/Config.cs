using System;
using System.IO;
using OsuPatcher.Runtime.Utils;

namespace OsuPatcher.Runtime
{
    internal class Config : BaseConfig
    {
        private const string ConfigFileName = "config.ini";
        private static readonly string ConfigPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "osuPatcher");

        public delegate void ConfigChangedHandler();
        public event ConfigChangedHandler OnConfigChanged;

        public bool PatchRelax { get; set; } = true;
        public bool PatchAutopilot { get; set; } = true;
        public bool TransitionTime { get; set; } = true;
        public bool PerformanceCalculator { get; set; } = true;

        internal static Config _load()
        {
            Directory.CreateDirectory(ConfigPath);
            string fullPath = Path.Combine(ConfigPath, ConfigFileName);
            Config config = new Config();

            if (File.Exists(fullPath))
                using (var reader = new StreamReader(fullPath))
                    config._loadConfig(reader);
            else
                config._save();

            return config;
        }

        private void _save()
        {
            string fullPath = Path.Combine(ConfigPath, ConfigFileName);
            using (var writer = new StreamWriter(fullPath, false))
                _saveConfig(writer);
        }

        private void ToggleSetting(string propName)
        {
            // HACK: FLIP the value of the auto property directly
            var prop = GetType()
                .GetProperty(propName);
            
            if (prop == null) return;

            var curr = (bool)prop.GetValue(this);
            prop.SetValue(this, !curr);

            _save();
            
            OnConfigChanged?.Invoke();
        }

        public void TogglePatchRelax(object sender, EventArgs e) => ToggleSetting(nameof(PatchRelax));
        public void TogglePatchAutopilot(object sender, EventArgs e) => ToggleSetting(nameof(PatchAutopilot));
        public void ToggleTransitionTime(object sender, EventArgs e) => ToggleSetting(nameof(TransitionTime));
        public void TogglePerformanceCalculator(object sender, EventArgs e) => ToggleSetting(nameof(PerformanceCalculator));

        /// <summary>Flips both PatchRelax and PatchAutopilot together so the single "Patch RX/AP"
        /// UI checkbox has a decisive on/off effect. The IL rewrites in RelaxMissPatch /
        /// AutoSaveRelaxScoresPatch check `!(PatchRelax || PatchAutopilot)`, so both must flip
        /// in lockstep to disable the patch — flipping only one leaves the other holding the
        /// guard on, which is the exact bug players saw ("toggle off but RX/AP still patched").</summary>
        public void ToggleRxAp(object sender, EventArgs e)
        {
            bool target = !(PatchRelax || PatchAutopilot);
            PatchRelax = target;
            PatchAutopilot = target;
            _save();
            OnConfigChanged?.Invoke();
        }
    }
}
