using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using OsuPatcher.Runtime.Constants;
using OsuPatcher.Runtime.Helpers;

namespace OsuPatcher.Runtime.Patches
{
    /// <summary>
    /// Plants <c>PKMS_USERNAME</c> in the process environment the moment the login attempt is
    /// about to be transmitted to bancho, so pkms can POST <c>/anticheat/key</c> without
    /// waiting for osu! to flush <c>osu!.&lt;WindowsUser&gt;.cfg</c> on exit. Without this a
    /// freshly-installed stable has no cfg on first launch, pkms spins on
    /// <c>read_osu_username</c> and the player has to close + relaunch to verify. With this,
    /// first-launch verification just works.
    ///
    /// Approach: locate <c>BanchoClient.connect</c> via its distinctive
    /// <c>set_NewLine → WriteLine(username) → WriteLine(password.Get(Hashed))</c> IL
    /// window, then transpile <c>dup; call SetUsernameEnv(string)</c> between
    /// <c>Bindable&lt;string&gt;::op_Implicit</c> and <c>WriteLine</c>. We duplicate the exact
    /// string that's about to be written to the login POST — no reflection on obfuscated type
    /// names, no risk of grabbing the wrong account mid-switch.
    /// </summary>
    [HarmonyPatch]
    internal class PkmsUsernamePatch
    {
        [HarmonyTargetMethod]
        private static MethodBase Target() => ILPatch.FindMethodBySignature(Patterns.PkmsUsername_Target);

        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var codes = new List<CodeInstruction>(instructions);
            var setter = typeof(PkmsUsernamePatch).GetMethod(
                nameof(SetUsernameEnv), BindingFlags.NonPublic | BindingFlags.Static);

            // Walk for: ldloc.0; ldsfld (any); call (op_Implicit : string); callvirt (TextWriter.WriteLine(string))
            //
            // We deliberately do NOT constrain the ldsfld field type to `Bindable<string>`:
            // Eazfuscator renames the Bindable<T> wrapper class to a hash (seen in the real osu!.exe
            // as `#=zd38xb3hWobHs98jfQA==<string>`), so a Name.StartsWith("Bindable") check never
            // matches on the shipped obfuscated build. What DOES survive the obfuscator:
            //   - `op_Implicit` keeps its name (CLR operator contract)
            //   - mscorlib's `TextWriter.WriteLine(string)` is referenced by its real name
            //   - the user-defined `op_Implicit` returning `string` on a static field is unique to
            //     the username line within connect() (the other op_Implicit calls here return bool).
            int inserted = 0;
            for (int i = 0; i + 3 < codes.Count && inserted == 0; i++)
            {
                if (codes[i].opcode != OpCodes.Ldloc_0) continue;
                if (codes[i + 1].opcode != OpCodes.Ldsfld) continue;
                if (codes[i + 2].opcode != OpCodes.Call) continue;
                if (codes[i + 3].opcode != OpCodes.Callvirt) continue;

                if (!(codes[i + 1].operand is FieldInfo)) continue;

                if (!(codes[i + 2].operand is MethodInfo opImpl)) continue;
                if (opImpl.Name != "op_Implicit") continue;
                if (opImpl.ReturnType != typeof(string)) continue;

                if (!(codes[i + 3].operand is MethodInfo wl)) continue;
                if (wl.Name != "WriteLine") continue;
                if (wl.DeclaringType != typeof(TextWriter) && wl.DeclaringType != typeof(StreamWriter)) continue;
                var pars = wl.GetParameters();
                if (pars.Length != 1 || pars[0].ParameterType != typeof(string)) continue;

                // Stack at codes[i+3] (before WriteLine): [streamWriter, string]
                // Insert `dup` then `call SetUsernameEnv(string)` so the setter sees the exact
                // string going into the login POST without disturbing the WriteLine call.
                codes.Insert(i + 3, new CodeInstruction(OpCodes.Dup));
                codes.Insert(i + 4, new CodeInstruction(OpCodes.Call, setter));
                inserted++;
            }

            if (inserted == 0)
                throw new System.InvalidOperationException(
                    "[PkmsUsernamePatch] no op_Implicit→WriteLine site matched; BanchoClient.connect login-write shape changed, pattern needs a rebase");

            return codes;
        }

        // Called inline from the login StreamWriter. Must be reentrant and must never throw —
        // a throw here would propagate up through BanchoClient.connect and kill the login.
        private static void SetUsernameEnv(string username)
        {
            try
            {
                if (string.IsNullOrEmpty(username))
                    return;
                Environment.SetEnvironmentVariable("PKMS_USERNAME", username);
                Utils.Logger.logFile($"[PkmsUsernamePatch] PKMS_USERNAME planted (len={username.Length})");
            }
            catch
            {
                // Swallow: env var write is best-effort; worst case pkms falls back to the cfg path
                // on second launch (the pre-patch behavior).
            }
        }
    }
}
