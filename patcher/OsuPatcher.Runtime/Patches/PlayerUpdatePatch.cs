using OsuPatcher.Runtime.Constants;
using OsuPatcher.Runtime.Helpers;
using OsuPatcher.Runtime.Utils;
using HarmonyLib;
using System;
using System.Reflection;

namespace OsuPatcher.Runtime.Patches
{
    [HarmonyPatch]
    internal class PlayerUpdatePatch
    {
        private static int _errorsLogged;

        [HarmonyTargetMethod]
        private static MethodBase Target()
            => ILPatch.FindMethodBySignature(Patterns.PlayerUpdate_Target);

        // A Postfix on Player.Update: runs every frame after the game's own update, for every kind
        // of play (ReplayWatcher, PlayerVs and PlayerTest call base.Update()).
        [HarmonyPostfix]
        private static void Postfix(object __instance)
        {
            try
            {
                if (!Options.Options.Config.PerformanceCalculator)
                    return;

                // Only the counter built for this very Player: between a retry's new Player and its
                // load completing, the old counter must not be driven by the new instance.
                var performance = PerformanceCalculationPatch.Current;
                if (performance == null || !ReferenceEquals(performance.Owner, __instance))
                    return;

                performance.Tick();
            }
            catch (Exception e)
            {
                // Never let the counter break osu!'s own update.
                if (++_errorsLogged <= 5)
                    Logger.logFile($"[pp] update failed: {e}");
            }
        }
    }
}
