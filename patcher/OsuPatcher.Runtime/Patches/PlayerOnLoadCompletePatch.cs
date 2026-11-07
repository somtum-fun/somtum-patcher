using OsuPatcher.Runtime.Constants;
using OsuPatcher.Runtime.Helpers;
using OsuPatcher.Runtime.Graphics;
using OsuPatcher.Runtime.Graphics.Sprites;
using OsuPatcher.Runtime.Graphics.Skinning;
using OsuPatcher.Runtime.Resolver;
using OsuPatcher.Runtime.Wrappers;
using OsuPatcher.Runtime.Play;
using OsuPatcher.Runtime.Rosu;
using OsuPatcher.Runtime.Utils;
using HarmonyLib;
using System;
using System.IO;
using System.Reflection;

namespace OsuPatcher.Runtime.Patches
{
    [HarmonyPatch]
    internal class PlayerOnLoadCompletePatch
    {
        [HarmonyTargetMethod]
        private static MethodBase Target() => ILPatch.FindMethodBySignature(Patterns.PlayerOnLoadComplete_Target);

        // Postfix on Player.OnLoadComplete(bool success), which returns true when loading failed.
        // Runs for every kind of play: a normal play, a replay or spectating (ReplayWatcher),
        // multiplayer (PlayerVs), the editor's test play (PlayerTest); they all call the base.
        // __originalMethod.DeclaringType is osu!'s Player class itself: everything is resolved on it.
        [HarmonyPostfix]
        private static void Postfix(object __instance, bool __result, MethodBase __originalMethod)
        {
            // Whatever happens next, the previous play's counter is done.
            PerformanceCalculationPatch.SetPerformance(null);
            try
            {
                if (__result || !Options.Options.Config.PerformanceCalculator)
                    return;
                OnComplete(__instance, __originalMethod.DeclaringType);
            }
            catch (Exception e)
            {
                Logger.logFile($"[pp] setup failed: {e}");
            }
        }

        private static void OnComplete(object player, Type playerType)
        {
            var state = PlayState.For(playerType);
            if (state == null)
                return;

            var score = state.Score();
            var beatmap = state.Beatmap.GetValue(player);
            if (score == null || beatmap == null)
                return;

            // The play's own mods: for a replay or a spectated player, osu! has already put theirs
            // on the current score (never ModManager's, which are the local player's).
            uint mods = (uint)state.Mods(score);

            Calculator calculator;
            using (var stream = (Stream)state.BeatmapStream.Invoke(beatmap, null))
                calculator = new Calculator(stream);
            if (!calculator.IsValid)
            {
                calculator.Dispose();
                return;
            }

            var ppText = new pSpriteText("0", "score", 0,
                Fields.TopLeft, Origins.Custom, Clocks.Game, 0, 130, 0.92f, true, Color.White, true, SkinSource.All);
            ppText.TextConstantSpacing = true;
            ppText.Scale = 1.1f;
            ppText.RefreshTexture();
            ppText.Text = "0"; // resolves the text setter here, on the main thread
            SpriteManager.AddToWidescreen(player, playerType, ppText.Instance);

            PerformanceCalculationPatch.SetPerformance(new Performance(player, state, calculator, ppText, mods));
        }
    }
}
