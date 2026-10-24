using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using OsuPatcher.Runtime.Constants;
using OsuPatcher.Runtime.Helpers;

namespace OsuPatcher.Runtime.Patches
{
    /// <summary>
    /// Shows the miss ("X") burst under Relax/Autopilot.
    ///
    /// osu! creates the miss sprite and adds it to the sprite manager unconditionally, but the
    /// fade/scale transformations that make it visible are only applied when
    /// <c>hitValue == miss &amp;&amp; !Replay &amp;&amp; !Watching</c>. Under relax osu drives input in a
    /// replay-like state, so those transformations are skipped and the sprite never appears.
    ///
    /// We anchor on the miss comparison, then rewrite each guard flag to <c>flag &amp; PatchRelax()</c>
    /// so the guard is neutralised while the patch is enabled. This is:
    ///   * anchored on the instruction SHAPE, not an absolute index (osu! updates shift offsets —
    ///     the old hard-coded index 664 had drifted onto an unrelated instruction), and
    ///   * insert-only — no instruction is removed, so no branch label can be orphaned and the
    ///     result is trivially valid IL.
    /// </summary>
    [HarmonyPatch]
    internal class PatchRelaxMiss
    {
        // osu! compares the hit result against this sentinel to detect a miss.
        private const int MissHitValue = -131072;

        [HarmonyTargetMethod]
        private static MethodBase Target() => ILPatch.FindMethodBySignature(Patterns.PatchRelaxMiss_Target);

        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var codes = new List<CodeInstruction>(instructions);

            // -131072 also appears as loc0's initialiser, so anchor on the one used in the miss
            // COMPARISON: `ldc.i4 -131072` immediately followed by `bne.un`.
            int miss = -1;
            for (int i = 0; i + 1 < codes.Count; i++)
            {
                if (codes[i].opcode == OpCodes.Ldc_I4 && codes[i].operand is int v && v == MissHitValue &&
                    (codes[i + 1].opcode == OpCodes.Bne_Un || codes[i + 1].opcode == OpCodes.Bne_Un_S))
                {
                    miss = i;
                    break;
                }
            }
            if (miss < 0)
                throw new System.InvalidOperationException(
                    "[RelaxMissPatch] miss-comparison anchor (ldc.i4 -131072; bne.un) not found — IL shape shifted, pattern needs a rebase");

            var check = typeof(PatchRelaxMiss).GetMethod(nameof(PatchRelax), BindingFlags.Public | BindingFlags.Static);

            // The `!Replay && !Watching` guard is two `ldsfld <flag>; brtrue SKIP` pairs directly
            // after the miss comparison. Rewrite each to `ldsfld <flag>; call PatchRelax(); and; brtrue`.
            // When the patch is on PatchRelax() is false, so `flag & false` is 0 and neither branch
            // is taken, letting the burst's transformations run. When off, `flag & true` is unchanged.
            int patched = 0;
            for (int i = miss; patched < 2 && i + 1 < codes.Count; i++)
            {
                if (codes[i].opcode != OpCodes.Ldsfld) continue;
                if (codes[i + 1].opcode != OpCodes.Brtrue && codes[i + 1].opcode != OpCodes.Brtrue_S) continue;

                codes.Insert(i + 1, new CodeInstruction(OpCodes.Call, check));
                codes.Insert(i + 2, new CodeInstruction(OpCodes.And));
                patched++;
                i += 2; // step past the inserted call/and (and this guard's brtrue)
            }

            if (patched != 2)
                throw new System.InvalidOperationException(
                    $"[RelaxMissPatch] expected 2 guard pairs (!Replay && !Watching) after miss comparison, patched {patched} — IL shape shifted, pattern needs a rebase");

            return codes;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool PatchRelax() => !(Options.Options.Config.PatchRelax || Options.Options.Config.PatchAutopilot);
    }
}
