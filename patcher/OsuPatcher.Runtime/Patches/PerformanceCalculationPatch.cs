using System.Threading;
using OsuPatcher.Runtime.Play;

namespace OsuPatcher.Runtime.Patches
{
    /// <summary>The live play's pp counter. Replacing it disposes the previous one (its native map).</summary>
    internal static class PerformanceCalculationPatch
    {
        private static Performance _performance;

        public static Performance Current => _performance;

        public static void SetPerformance(Performance performance)
        {
            var old = Interlocked.Exchange(ref _performance, performance);
            old?.Dispose();
        }
    }
}
