using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace OsuPatcher.UI
{
    [DataContract]
    internal sealed class Config
    {
        /// <summary>
        /// Where the patcher keeps its config and the payload it extracts (pkms.dll, the runtime).
        /// The dev build uses its own folder so both can be installed at once: they share the name
        /// otherwise, and whichever ran last would leave ITS pkms.dll behind for the other to load —
        /// a dev pkms talking to production, or the reverse. Must match the runtime's copy in
        /// patcher/OsuPatcher.Runtime/Main.cs.
        /// </summary>
        public static readonly string Directory =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
#if SOMTUM_DEV
                "osu-patcher-dev");
#else
                "osu-patcher");
#endif

        private static readonly string FilePath = Path.Combine(Directory, "config.json");

        [DataMember]
        public string OsuPath { get; set; }

        public static Config Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var ser = new DataContractJsonSerializer(typeof(Config));
                    using (var fs = File.OpenRead(FilePath))
                        return (Config)ser.ReadObject(fs) ?? new Config();
                }
            }
            catch { }
            return new Config();
        }

        public void Save()
        {
            System.IO.Directory.CreateDirectory(Directory);
            var ser = new DataContractJsonSerializer(typeof(Config));
            using (var fs = File.Create(FilePath))
                ser.WriteObject(fs, this);
        }
    }
}
