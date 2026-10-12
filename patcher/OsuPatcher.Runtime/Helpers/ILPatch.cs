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

                    var opcodes = new ILReader(b).GetOpCodes();
                    int idx = 0;

                    foreach (var op in opcodes)
                    {
                        if (op == OpCodes.Nop) continue;

                        if (op == sig[idx])
                        {
                            idx++;
                            if (idx == sig.Length) return true;
                        }
                        else
                        {
                            // Rewind to the start — if this opcode is itself the first of the
                            // pattern, kick off a new potential match from here, otherwise reset.
                            idx = (op == sig[0]) ? 1 : 0;
                        }
                    }

                    return false;
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

                    var opcodes = new ILReader(b).GetOpCodes();
                    int idx = 0;

                    foreach (var op in opcodes)
                    {
                        if (op == OpCodes.Nop) continue;

                        if (op == sig[idx])
                        {
                            idx++;
                            if (idx == sig.Length) return true;
                        }
                        else
                        {
                            idx = (op == sig[0]) ? 1 : 0;
                        }
                    }

                    return false;
                });
        }
    }
}
