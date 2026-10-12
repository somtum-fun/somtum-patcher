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
    /// Patch to prevent combo breaks when Relax mod is active.
    /// </summary>
    [HarmonyPatch]
    internal class PatchRelaxComboBreak
    {
        [HarmonyTargetMethod]
        private static MethodBase Target() => ILPatch.FindMethodBySignature(Patterns.PatchRelaxComboBreak_Target);

        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var codes = new List<CodeInstruction>(instructions);

            // Reverted from 0.3.11's shape-anchor back to the hardcoded codes[1558] because the
            // shape anchor was too greedy — it could fire on the wrong method entirely when the
            // live assembly's GetRuntimeMethods enumeration returned a different same-shape
            // candidate than the file-scan did at build time, which produced invalid IL and a
            // runtime "An error occurred somewhere in osu!" crash. Pre-check the exact index
            // instead and bail cleanly on any shape mismatch. See [[feedback-patcher-pattern-source]]
            // for why the obfuscated binary has brtrue_s at this position.
            const int REMOVE_INDEX = 1558;
            const int INSERT_INDEX = 1559;
            if (codes.Count <= INSERT_INDEX || codes[REMOVE_INDEX].opcode != OpCodes.Brtrue_S)
            {
                OsuPatcher.Runtime.Utils.Logger.log($"[RelaxComboBreakPatch] IL anchor shifted (codes[{REMOVE_INDEX}] is {(codes.Count > REMOVE_INDEX ? codes[REMOVE_INDEX].opcode.ToString() : "oob")}), skipping transpile");
                return instructions;
            }

            codes.RemoveAt(REMOVE_INDEX);
            codes.InsertRange(INSERT_INDEX, new CodeInstruction[]
            {
                new CodeInstruction(OpCodes.Or),
                new CodeInstruction(OpCodes.Call,
                    typeof(PatchRelaxComboBreak)
                    .GetMethod(nameof(PatchRelax), BindingFlags.Public | BindingFlags.Static)),
                new CodeInstruction(OpCodes.And)
            });

            return codes.AsEnumerable();
        }

        /// <summary>
        /// Checks if Relax patch is enabled.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool PatchRelax() => !(Options.Options.Config.PatchRelax || Options.Options.Config.PatchAutopilot);
    }
}
