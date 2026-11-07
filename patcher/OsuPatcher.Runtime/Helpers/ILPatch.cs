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

            // Collect ALL matches so an ambiguous pattern (two methods containing the sequence after
            // a new obfuscation pass) is logged instead of silently picking the first — a wrong
            // target lets the transpiler run on an unrelated method, which usually succeeds
            // vacuously and gives us a false "installed" count.
            //
            // GetRuntimeMethods() on a subclass also lists the methods it inherits, so one body shows
            // up once per subclass (same token, different ReflectedType). Harmony refuses those
            // inherited views ("Patch the declared method instead"), which is what kept
            // PatchAutoSaveRelaxScores (Ranking has 3 subclasses) and PatchRelaxMiss
            // (HitObjectManager has 11) off on both stable and cuttingedge. Dedupe by token and
            // resolve each match to the view of the type that declares it. The scan itself is
            // unchanged: matching still runs over the same methods in the same order.
            var seen = new HashSet<int>();
            var matches = OsuModule.GetTypes()
                .SelectMany(t => t.GetRuntimeMethods())
                .Where(m =>
                {
                    var b = m.GetMethodBody()?.GetILAsByteArray();
                    if (b == null) return false;
                    return ContainsSequence(b, sig);
                })
                .Where(m => m.Module == OsuModule && seen.Add(m.MetadataToken))
                .Select(m => m.ReflectedType == m.DeclaringType ? m : (MethodInfo)OsuModule.ResolveMethod(m.MetadataToken))
                .Take(5)
                .ToList();
            if (matches.Count > 1)
                OsuPatcher.Runtime.Utils.Logger.logFile(
                    $"[ILPatch] ambiguous signature matched {matches.Count}+ methods: {string.Join(", ", matches.Select(m => (m.DeclaringType?.Name ?? "?") + "." + m.Name))} — picking first, pattern should be tightened");
            return matches.FirstOrDefault();
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
