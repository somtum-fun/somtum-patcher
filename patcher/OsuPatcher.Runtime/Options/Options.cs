using System;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using OsuPatcher.Runtime.Constants;
using OsuPatcher.Runtime.Helpers;
using OsuPatcher.Runtime.Utils;

namespace OsuPatcher.Runtime.Options
{
    /// <summary>
    /// Manages the configuration options for the patcher.
    /// </summary>
    internal class Options
    {
        public static readonly Config Config = Config._load();

        /// <summary>
        /// Initializes the options menu with checkboxes and categories.
        /// </summary>
        /// <param name="instance">The instance of the options menu.</param>
        public static void InitializeOptions(object instance)
        {
            // Single checkbox for the shared RX/AP patch. Previously there were two ("Patch Relax"
            // and "Patch Autopilot") but both the miss-burst and the auto-save transpilers only
            // check `!(Config.PatchRelax || Config.PatchAutopilot)` — one guard flag per patched
            // method, shared across both mods — so unticking just one left the other holding the
            // patch on. Players read that as "the toggle doesn't work". The underlying Config
            // fields are kept (for forward compat with older configs on disk) but the UI exposes
            // a single master; PatchAutopilot is kept in sync behind the scenes by RxApToggle.
            CheckBox patchRxAp = new CheckBox("Patch RX/AP",
                "Removes Relax/Autopilot limitations: shows the miss burst, plays the combo-break sound, "
                + "and submits the score. Untick to play RX/AP with osu!'s default behaviour.",
                Config.PatchRelax,
                Config.ToggleRxAp);

            CheckBox transitionTime = new CheckBox("Faster Transition time",
                "Control how fast the screen fades in and out. Turn this on for quicker transitions.",
                Config.TransitionTime,
                Config.ToggleTransitionTime);

            CheckBox performanceCalculator = new CheckBox("Performance Calculator",
                "Let the patcher calculates your plays ingame.",
                Config.PerformanceCalculator,
                Config.TogglePerformanceCalculator);

            Array optionsChildren = Element.CreateArray(
                patchRxAp,
                transitionTime,
                performanceCalculator);

            Section section = new Section("Patches");
            section.SetChildren(optionsChildren);

            Section credit = new Section("somtum patcher v1.2 — " + Main.DefaultServerName);

            Array sectionChildren = Element.CreateArray(section, credit);
            
            Category category = new Category(FontAwesome.Magic);
            category.SetChildren(sectionChildren);

            // add to elements
            Add(instance, category);
        }

        private static void Add(object instance, Element element)
            => BaseAddElement.Invoke(instance, new[] { element.V });

        private static readonly MethodBase BaseAddElement = ILPatch.FindMethodBySignature(Patterns.Options_AddElement);
    }
}
