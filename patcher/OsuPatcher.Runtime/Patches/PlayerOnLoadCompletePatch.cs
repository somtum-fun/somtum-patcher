using OsuPatcher.Runtime.Constants;
using OsuPatcher.Runtime.Helpers;
using OsuPatcher.Runtime.Graphics;
using OsuPatcher.Runtime.Graphics.Sprites;
using OsuPatcher.Runtime.Graphics.Skinning;
using OsuPatcher.Runtime.Resolver;
using OsuPatcher.Runtime.Wrappers;
using OsuPatcher.Runtime.Play;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.IO;

namespace OsuPatcher.Runtime.Patches
{
    [HarmonyPatch]
    internal class PlayerOnLoadCompletePatch
    {
        [HarmonyTargetMethod]
        private static MethodBase Target() => ILPatch.FindMethodBySignature(Patterns.PlayerOnLoadComplete_Target);

        // Same simplification as PlayerUpdatePatch — use a Postfix so there's no IL layout to
        // track. OnComplete does one-shot setup (adds the pp sprite + the Performance resolver)
        // and relies on its own null-checks for every reflection lookup, so running it as a
        // postfix after the method's internal state is settled is safer than injecting mid-method.
        [HarmonyPostfix]
        private static void Postfix(object __instance) => OnComplete(__instance);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void OnComplete(object playerInstance)
        {
            if (!Options.Options.Config.PerformanceCalculator)
                return;

            // Don't show the realtime pp counter while watching a replay / spectating.
            if (GameState.IsWatchingReplay(playerInstance))
                return;

            var ppText = new pSpriteText("0", "score", 0, 
                Fields.TopLeft, Origins.Custom, Clocks.Game, 0, 130, 0.92f, true, Color.White, true, SkinSource.All);
            ppText.TextConstantSpacing = true;
            ppText.Scale = 1.1f;
            ppText.RefreshTexture();

            SpriteManager.AddToWidescreen(playerInstance, ppText.Instance);

            var beatmapField = playerInstance.GetType()
                .GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                .FirstOrDefault(f => typeof(Stream).IsAssignableFrom(f.FieldType) ||
                                    f.FieldType.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
                                    .Any(m => m.ReturnType == typeof(Stream) && m.GetParameters().Length == 0));

            var currentScoreField = Score.GetCurrentScoreField(playerInstance);
            if (currentScoreField == null || beatmapField == null)
                return;

            var currentScore = currentScoreField.GetValue(null);
            var beatmap = beatmapField.GetValue(playerInstance);

            if (currentScore == null || beatmap == null)
                return;

            var enabledModsField = currentScore.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
               .FirstOrDefault(f => f.FieldType.IsGenericType);

            if (enabledModsField == null)
                return;

            var enabledMods = enabledModsField.GetValue(currentScore);
            if (enabledMods == null)
                return;

            var obfuscatedType = enabledMods.GetType();
            var generic = obfuscatedType.GetGenericArguments()[0];

            var modsValue = obfuscatedType
                .GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .FirstOrDefault(m =>
                    m.ReturnType == generic &&
                    m.GetParameters().Length == 0);

            if (modsValue == null)
                return;

            int mods = (int)modsValue.Invoke(enabledMods, null);

            var getBeatmapStreamMethod = beatmap.GetType()
                .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
                .Where(m => m.ReturnType == typeof(Stream) && m.GetParameters().Length == 0)
                .Skip(1) // first one is for audio.
                .FirstOrDefault();

            if (getBeatmapStreamMethod == null)
                return;

            var performance = new Performance(beatmap, getBeatmapStreamMethod, (uint)mods, new[] { ppText });
            PerformanceCalculationPatch.SetPerformance(performance);
        }
    }
}
