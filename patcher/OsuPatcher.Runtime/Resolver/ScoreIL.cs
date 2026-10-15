using System.Collections.Generic;
using System.Reflection.Emit;
using OsuPatcher.Runtime.Helpers;

namespace OsuPatcher.Runtime.Resolver
{
    /// <summary>
    /// Finds osu!'s Score hit-count fields from the IL of its own methods, so nothing depends on
    /// field order or obfuscated names (both differ between builds and streams).
    /// Pure functions over IL bytes: the Runtime resolves the returned metadata tokens with
    /// Module.ResolveField; the offline check in tools runs the same code on the exe files.
    /// </summary>
    internal static class ScoreIL
    {
        /// <summary>
        /// From the base Score.get_Accuracy, (Count50 * 50 + Count100 * 100 + Count300 * 300) / (TotalHits * 300):
        /// the field loaded right before each constant (a conversion may sit between) is that count.
        /// False unless all three were found and differ.
        /// </summary>
        public static bool CountsFromAccuracy(byte[] il, out int count300, out int count100, out int count50)
        {
            count300 = count100 = count50 = 0;
            int lastField = 0;
            foreach (var ins in new ILReader(il).GetInstructions())
            {
                var op = ins.OpCode;
                if (op == OpCodes.Ldfld)
                {
                    lastField = (int)ins.Operand;
                    continue;
                }
                if (op == OpCodes.Conv_I4 || op == OpCodes.Conv_U2 || op == OpCodes.Conv_R4 || op == OpCodes.Conv_R8 || op == OpCodes.Nop)
                    continue;

                if (lastField != 0 && (op == OpCodes.Ldc_I4_S || op == OpCodes.Ldc_I4))
                {
                    switch (ins.Operand)
                    {
                        case 50: count50 = lastField; break;
                        case 100: count100 = lastField; break;
                        case 300: count300 = lastField; break;
                    }
                }
                lastField = 0;
            }
            return count300 != 0 && count100 != 0 && count50 != 0
                && count300 != count100 && count300 != count50 && count100 != count50;
        }

        /// <summary>Every field the body loads with ldfld, in order, without repeats.</summary>
        public static List<int> LoadedFields(byte[] il)
        {
            var list = new List<int>();
            foreach (var ins in new ILReader(il).GetInstructions())
                if (ins.OpCode == OpCodes.Ldfld && !list.Contains((int)ins.Operand))
                    list.Add((int)ins.Operand);
            return list;
        }

        /// <summary>
        /// The base Score.get_TotalHits is Count50 + Count100 + Count300 + CountMiss: if this body loads
        /// exactly the three known counts plus one more field, that one is CountMiss (else 0).
        /// </summary>
        public static int MissFromTotalHits(byte[] il, int count300, int count100, int count50)
        {
            var fields = LoadedFields(il);
            if (fields.Count != 4 || !fields.Contains(count300) || !fields.Contains(count100) || !fields.Contains(count50))
                return 0;
            foreach (var f in fields)
                if (f != count300 && f != count100 && f != count50)
                    return f;
            return 0;
        }
    }
}
