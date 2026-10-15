using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

namespace OsuPatcher.Runtime.Helpers
{
    internal sealed class ILPatch
    {
        private static readonly Module OsuModule = AppDomain.CurrentDomain.GetAssemblies()
            .SingleOrDefault(a => a.GetName().Name == "osu!")
            .GetModules()
            .SingleOrDefault();
        
        /// <summary>
        /// Cari method pake IL Opcodes (kalo match)
        /// </summary>
        internal static MethodInfo FindMethodBySignature(OpCode[] signature)
        {
            if (signature == null || signature.Length == 0 || OsuModule == null)
                return null;

            // Fold nops out of the pattern — the 20260924+ obfuscator sprinkles stray Nops into
            // method bodies (notably after Stloc.N and before Br.S), and some older patterns were
            // written with explicit Nops to match a specific build's debug IL. Nops are stack-
            // neutral and semantically meaningless, so matching ignores them on both sides.
            var sig = signature.Where(s => s != OpCodes.Nop).ToArray();
            if (sig.Length == 0) return null;

            return OsuModule.GetTypes()
                .SelectMany(t => t.GetRuntimeMethods())
                .FirstOrDefault(m =>
                {
                    var b = m.GetMethodBody()?.GetILAsByteArray();
                    if (b == null) return false;

                    return ContainsSequence(b, sig);
                });
        }

        /// <summary>
        /// Cari constructor pake IL Opcodes (kalo match)
        /// </summary>
        public static ConstructorInfo FindConstructorBySignature(OpCode[] signature)
        {
            if (signature == null || signature.Length == 0 || OsuModule == null)
                return null;

            var sig = signature.Where(s => s != OpCodes.Nop).ToArray();
            if (sig.Length == 0) return null;

            return OsuModule.GetTypes()
                .SelectMany(t => t.GetConstructors(
                    BindingFlags.Instance |
                    BindingFlags.Static |
                    BindingFlags.Public |
                    BindingFlags.NonPublic))
                .FirstOrDefault(ctor =>
                {
                    var b = ctor.GetMethodBody()?.GetILAsByteArray();
                    if (b == null) return false;

                    return ContainsSequence(b, sig);
                });
        }

        /// <summary>
        /// True if the body's opcodes (nops left out) contain <paramref name="sig"/> as a run. A plain
        /// search from every start: the old one-pass matcher only restarted at the current opcode, so
        /// a pattern beginning with a repeated pair (ldarg.0; call; ldarg.0; call …) could miss a
        /// match that started inside a failed one.
        /// </summary>
        internal static bool ContainsSequence(byte[] il, OpCode[] sig)
        {
            var ops = new List<OpCode>(il.Length / 2);
            foreach (var op in new ILReader(il).GetOpCodes())
                if (op != OpCodes.Nop)
                    ops.Add(op);

            for (int start = 0; start + sig.Length <= ops.Count; start++)
            {
                int i = 0;
                while (i < sig.Length && ops[start + i] == sig[i]) i++;
                if (i == sig.Length) return true;
            }
            return false;
        }
    }
}
