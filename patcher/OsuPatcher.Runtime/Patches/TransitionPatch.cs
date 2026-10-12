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
    /// Patches the game transition time to speed up screen transitions.
    /// </summary>
    [HarmonyPatch]
    internal class PatchTransition
    {
        [HarmonyTargetMethod]
        private static MethodBase Target() => ILPatch.FindMethodBySignature(Patterns.PatchTransition_Target);

        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var codes = new List<CodeInstruction>(instructions);

            // Reverted from 0.3.11's tail-shape anchor to a strict codes[127] guard. The shape
            // anchor could fire on the wrong method when FindMethodBySignature's enumeration
            // picked a same-shape candidate, producing invalid IL at runtime. Pre-check the exact
            // opcode at the anchor and bail if it's not the Ldc_R4 we expect — transition
            // speed-up stays off on that build, but the game doesn't crash.
            const int ANCHOR_INDEX = 127;
            if (codes.Count <= ANCHOR_INDEX || codes[ANCHOR_INDEX].opcode != OpCodes.Ldc_R4)
            {
                OsuPatcher.Runtime.Utils.Logger.log($"[TransitionPatch] IL anchor shifted (codes[{ANCHOR_INDEX}] is {(codes.Count > ANCHOR_INDEX ? codes[ANCHOR_INDEX].opcode.ToString() : "oob")}), skipping transpile");
                return instructions;
            }

            codes.RemoveAt(ANCHOR_INDEX);
            codes.Insert(ANCHOR_INDEX, new CodeInstruction(OpCodes.Call,
                typeof(PatchTransition)
                .GetMethod(nameof(TransitionTime), BindingFlags.Public | BindingFlags.Static)));

            return codes.AsEnumerable();
        }

        /// <summary>
        /// Returns the transition time based on configuration.
        /// </summary>
        /// <returns>200 if faster transition is enabled, otherwise 100.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float TransitionTime()
        {
            if (!Options.Options.Config.TransitionTime)
                return 100;

            return 200;
        }
    }
}
