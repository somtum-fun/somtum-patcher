using System;
using System.Linq;
using HarmonyLib;
using OsuPatcher.Runtime.Utils;
using OsuPatcher.Runtime.Wrappers;

namespace OsuPatcher.Runtime
{
    public class Main
    {
        private static readonly Harmony Harmony = new Harmony("osu_patcher.ano");
        private const string DefaultServer = "somtum.fun";

        internal static string Server { get; private set; } = DefaultServer;
        
        public static int Initialize(string st)
        {
            try
            {
                Server = ResolveServer(st);

                // Per-type patching: one broken patch (shifted IL from a new obfuscation pass, a
                // removed method, a changed signature) throws SEHException inside Harmony's
                // UpdateWrapper and would, under PatchAll, abort the whole batch — the player loses
                // every patch. Iterating [HarmonyPatch] types individually with a try/catch means
                // the working patches still install and the broken ones get logged for the next
                // rebase pass. Partial RX/AP beats a crashed game.
                int installed = 0, skipped = 0;
                foreach (var type in typeof(Main).Assembly.GetTypes())
                {
                    if (type.GetCustomAttributes(typeof(HarmonyPatch), inherit: true).Length == 0)
                        continue;
                    try
                    {
                        new PatchClassProcessor(Harmony, type).Patch();
                        installed++;
                    }
                    catch (Exception patchEx)
                    {
                        skipped++;
                        // File-only: these are developer-level diagnostics (obfuscation shifted the
                        // IL, our Harmony pattern needs a rebase). Players don't need the stack
                        // trace in a toast — they get a friendly summary below instead.
                        Logger.logFile($"[patcher] skipped {type.Name}: {patchEx.GetType().Name}: {patchEx.Message}");
                    }
                }
                Logger.logFile($"[patcher] installed {installed} patch(es), skipped {skipped}");

                NotificationManager.ShowMessageMassive(
                    skipped == 0
                        ? "somtum patcher is now initialized!"
                        : "Some patcher features aren't compatible with this osu! build. "
                          + "Try: launch normal osu! first, then relaunch with the Launcher; "
                          + "or switch osu! to the cuttingedge build. "
                          + "Still not working? Ask us on Discord.",
                    skipped == 0 ? 5000 : 12000,
                    NotificationManager.NotificationType.Warning
                );
            }
            catch (Exception e)
            {
                // File-only: full exception trace is noise for players.
                Logger.logFile(e.ToString());
            }

            return 0;
        }

        private static string ResolveServer(string injectedArgument)
        {
            var args = Environment.GetCommandLineArgs()
                .Concat((injectedArgument ?? string.Empty).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                .ToArray();

            for (int i = 0; i < args.Length; i++)
            {
                if (!string.Equals(args[i], "-devserver", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (i + 1 < args.Length && !string.IsNullOrWhiteSpace(args[i + 1]))
                    return args[i + 1].Trim();
            }

            return DefaultServer;
        }
    }
}
