using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using OsuPatcher.Runtime.Constants;
using OsuPatcher.Runtime.Helpers;

namespace OsuPatcher.Runtime.Patches
{
    /// <summary>
    /// Patch to allow saving scores even when Relax mod is active.
    /// </summary>
    [HarmonyPatch]
    internal class PatchAutoSaveRelaxScores
    {
        [HarmonyTargetMethod]
        private static MethodBase Target() => ILPatch.FindMethodBySignature(Patterns.PatchAutoSaveRelaxScores_Target);

        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var codes = new List<CodeInstruction>(instructions);

            // Semantic injection: wherever the method does `(mods & relaxBit) > 0 → Brtrue(skip
            // save)`, we rewrite the condition to `((mods & relaxBit) > 0) & !PatchRelax` so the
            // skip only fires when the player hasn't enabled the Relax/Autopilot override. The
            // old transpiler hard-coded InsertRange at indices 21 and 33 — those positions have
            // shifted every osu! obfuscator pass (20260924 removed two Ldc_I4/Stloc intermediaries,
            // so the layout moved). Anchor by shape instead: find each Cgt → Brtrue pair and
            // inject `Call(PatchRelax); And` between them. Only the first two matches are the
            // relax/autopilot checks our pattern locked onto — any later Cgt pairs belong to
            // unrelated logic in the same method, so we stop there.
            var patchRelax = typeof(PatchAutoSaveRelaxScores)
                .GetMethod(nameof(PatchRelax), BindingFlags.Public | BindingFlags.Static);

            int injected = 0;
            for (int i = 0; i < codes.Count - 1 && injected < 2; i++)
            {
                if (codes[i].opcode != OpCodes.Cgt) continue;
                if (codes[i + 1].opcode != OpCodes.Brtrue && codes[i + 1].opcode != OpCodes.Brtrue_S)
                    continue;

                codes.Insert(i + 1, new CodeInstruction(OpCodes.And));
                codes.Insert(i + 1, new CodeInstruction(OpCodes.Call, patchRelax));
                i += 2;  // skip over what we just inserted
                injected++;
            }

            if (injected != 2)
                OsuPatcher.Runtime.Utils.Logger.log($"[AutoSaveRelaxScoresPatch] expected 2 Cgt→Brtrue anchors, found {injected}");

            return codes.AsEnumerable();
        }

        /// <summary>
        /// Checks if Relax patch is enabled.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool PatchRelax() => !(Options.Options.Config.PatchRelax || Options.Options.Config.PatchAutopilot);
    }
}
