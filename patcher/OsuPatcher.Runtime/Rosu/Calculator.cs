using System;
using System.IO;
using System.Runtime.InteropServices;

namespace OsuPatcher.Runtime.Rosu
{
    /// <summary>
    /// One play's beatmap, parsed once by the native pp library (akatsuki_pp_ffi.dll: ppy's pp via
    /// rosu-pp, akatsuki's formula for osu! Relax). Every native call catches its own panics and
    /// returns 0, so a bad map or input can't take osu! down.
    /// </summary>
    public sealed class Calculator : IDisposable
    {
        private const string NativeLibraryFileName = "akatsuki_pp_ffi.dll";
        private const uint LOAD_WITH_ALTERED_SEARCH_PATH = 0x00000008;

        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibraryExW(string fileName, IntPtr reserved, uint flags);

        [DllImport("akatsuki_pp_ffi", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr pp_map_new(byte[] bytes, UIntPtr len);

        [DllImport("akatsuki_pp_ffi", CallingConvention = CallingConvention.Cdecl)]
        private static extern void pp_map_free(IntPtr map);

        [DllImport("akatsuki_pp_ffi", CallingConvention = CallingConvention.Cdecl)]
        private static extern CalculateResult pp_calculate(IntPtr map, ref PpScore score);

        private static readonly bool NativeLoaded = LoadNativeLibrary();

        private readonly object _lock = new object();
        private IntPtr _map;

        /// <summary>Parses the .osu file; <see cref="IsValid"/> is false if it couldn't be.</summary>
        public Calculator(Stream beatmap)
        {
            if (!NativeLoaded || beatmap == null)
                return;

            byte[] bytes;
            using (var memory = new MemoryStream())
            {
                beatmap.CopyTo(memory);
                bytes = memory.ToArray();
            }
            if (bytes.Length != 0)
                _map = pp_map_new(bytes, (UIntPtr)bytes.Length);
        }

        public bool IsValid => _map != IntPtr.Zero;

        /// <summary>
        /// The library lives next to this DLL. Loaded by full path (no SetDllDirectory, which would
        /// change the DLL search path for the whole osu! process); the [DllImport]s then bind to
        /// the already loaded module by name.
        /// </summary>
        private static bool LoadNativeLibrary()
        {
            string dir = Path.GetDirectoryName(typeof(Calculator).Assembly.Location);
            if (string.IsNullOrEmpty(dir))
                return false;
            string path = Path.Combine(dir, NativeLibraryFileName);
            return File.Exists(path) && LoadLibraryExW(path, IntPtr.Zero, LOAD_WITH_ALTERED_SEARCH_PATH) != IntPtr.Zero;
        }

        /// <summary>pp for the score so far (0 if anything is off). Thread-safe; never throws for bad input.</summary>
        public double Calculate(PpScore score)
        {
            lock (_lock)
            {
                if (_map == IntPtr.Zero)
                    return 0.0;
                double pp = pp_calculate(_map, ref score).pp;
                return double.IsNaN(pp) || double.IsInfinity(pp) || pp < 0.0 ? 0.0 : pp;
            }
        }

        public void Dispose()
        {
            lock (_lock)
            {
                if (_map != IntPtr.Zero)
                {
                    pp_map_free(_map);
                    _map = IntPtr.Zero;
                }
            }
            GC.SuppressFinalize(this);
        }

        ~Calculator()
        {
            Dispose();
        }
    }

    /// <summary>One score in osu!stable's terms; mirrors PpScore in akatsuki-pp-ffi/src/lib.rs.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct PpScore
    {
        public uint Mode;
        public uint Mods;
        public uint MaxCombo;
        public uint N300;
        public uint N100;
        public uint N50;
        public uint NGeki;
        public uint NKatu;
        public uint Misses;
        /// <summary>Objects judged so far; uint.MaxValue = the whole map.</summary>
        public uint PassedObjects;
        /// <summary>osu!stable's total score, 0 = unknown. Left 0: the website's pp doesn't use it.</summary>
        public uint LegacyTotalScore;
        /// <summary>0..100, only for osu! Relax.</summary>
        public float Accuracy;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct CalculateResult
    {
        public double pp;
        public double stars;
    }
}
