using OsuPatcher.Runtime.Constants;
using OsuPatcher.Runtime.Helpers;
using OsuPatcher.Runtime.Resolver;
using HarmonyLib;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace OsuPatcher.Runtime.Patches
{
    [HarmonyPatch]
    internal class PlayerUpdatePatch
    {
        private static FieldInfo _currentScore;
        private static MethodInfo _getAccuracy;
        private static MethodInfo _getLegacyScore;
        private static FieldInfo _maxCombo;
        private static FieldInfo _playMode;

        [HarmonyTargetMethod]
        private static MethodBase Target()
            => ILPatch.FindMethodBySignature(Patterns.PlayerUpdate_Target);

        // Harmony Postfix instead of a Transpiler: the old implementation inserted Ldarg_0 + Call
        // at a hardcoded codes[775] anchor, which was derived empirically from one obfuscator
        // pass and shifts the moment osu! moves. A Postfix runs after the method returns, with
        // the game's own state already committed, so there's no IL layout to track. OnUpdate's
        // internal guards (null-score / replay-watcher / config disabled) make the extra calls
        // on early-return ticks harmless.
        [HarmonyPostfix]
        private static void Postfix(object __instance) => OnUpdate(__instance);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void OnUpdate(object playerInstance)
        {
            if (!Options.Options.Config.PerformanceCalculator)
                return;

            // Skip realtime pp while watching a replay / spectating (the counter isn't shown then).
            if (GameState.IsWatchingReplay(playerInstance))
                return;

            EnsureScoreFieldResolved(playerInstance);

            if (_currentScore == null)
                return;

            var score = _currentScore.GetValue(null);
            if (score == null)
                return;

            var scoreType = score.GetType();

            if (_getAccuracy == null)
                _getAccuracy = scoreType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .FirstOrDefault(m =>
                        m.ReturnType == typeof(float) &&
                        m.GetParameters().Length == 0 &&
                        m.IsVirtual);

            if (_getAccuracy == null)
                return;

            if (_getLegacyScore == null)
                _getLegacyScore = scoreType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .FirstOrDefault(m =>
                        m.ReturnType == typeof(int) &&
                        m.GetParameters().Length == 0 &&
                        m.IsVirtual);

            if (_getLegacyScore == null)
                return;

            if (_maxCombo == null)
                _maxCombo = scoreType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .FirstOrDefault(f => f.FieldType == typeof(int));
            if (_maxCombo == null)
                return;

            if (_playMode == null)
                _playMode = scoreType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .FirstOrDefault(f => f.FieldType.IsEnum);
            if (_playMode == null)
                return;

            float accuracy = (float)_getAccuracy.Invoke(score, null) * 100f;
            int legacyScore = (int)_getLegacyScore.Invoke(score, null);
            int maxCombo = (int)_maxCombo.GetValue(score);
            int playMode = (int)_playMode.GetValue(score);

            PerformanceCalculationPatch.QueueCalculation(score, accuracy, legacyScore, maxCombo, playMode);
        }

        private static void EnsureScoreFieldResolved(object playerInstance)
        {
            if (_currentScore != null)
                return;

            _currentScore = Score.GetCurrentScoreField(playerInstance);
        }
    }
}
