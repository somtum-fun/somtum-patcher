using System.Reflection.Emit;

namespace OsuPatcher.Runtime.Constants
{
    public static class Patterns
    {
        #region Options

        // Options.AddElement
        public static readonly OpCode[] Options_AddElement = new[]
        {
            OpCodes.Ldarg_0,
            OpCodes.Ldfld,
            OpCodes.Ldarg_1,
            OpCodes.Callvirt,
            OpCodes.Ldarg_1,
            OpCodes.Callvirt,
            OpCodes.Brfalse_S,
            OpCodes.Ldarg_1,
            OpCodes.Callvirt,
            OpCodes.Callvirt,
            OpCodes.Stloc_0,
            OpCodes.Br_S,
            OpCodes.Ldloc_0,
            OpCodes.Callvirt,
            OpCodes.Stloc_1,
            OpCodes.Ldloc_1,
            OpCodes.Ldarg_0,
            OpCodes.Ldftn,
            OpCodes.Newobj,
            OpCodes.Callvirt,
            OpCodes.Ldarg_0,
            OpCodes.Ldloc_1,
            OpCodes.Call,
            OpCodes.Ldloc_0,
            OpCodes.Callvirt,
            OpCodes.Brtrue_S,
            OpCodes.Leave_S,
            OpCodes.Ldloc_0,
            OpCodes.Brfalse_S,
            OpCodes.Ldloc_0,
            OpCodes.Callvirt,
            OpCodes.Endfinally,
            OpCodes.Ldarg_1,
            OpCodes.Isinst,
            OpCodes.Stloc_2,
            OpCodes.Ldloc_2,
            OpCodes.Brfalse_S,
            OpCodes.Ldloc_2,
            OpCodes.Ldarg_0,
            OpCodes.Ldftn,
            OpCodes.Newobj,
            OpCodes.Newobj,
            OpCodes.Stloc_3,
            OpCodes.Ldarg_0,
            OpCodes.Ldfld,
            OpCodes.Ldloc_3,
            OpCodes.Callvirt,
            OpCodes.Ldarg_0,
            OpCodes.Ldfld,
            OpCodes.Ldloc_3,
            OpCodes.Ldfld,
            OpCodes.Callvirt,
            OpCodes.Ldarg_0,
            OpCodes.Ldfld,
            OpCodes.Ldarg_1,
            OpCodes.Ldfld,
            OpCodes.Callvirt,
            OpCodes.Ret,
        };

        // PatchOptionsMenu.Target
        public static readonly OpCode[] PatchOptionsMenu_Target = new[]
        {
            OpCodes.Ldarg_0,
            OpCodes.Ldfld,
            OpCodes.Ldc_I4_S,
            OpCodes.Call,
            OpCodes.Callvirt,
            OpCodes.Ldarg_0,
            OpCodes.Ldfld,
            OpCodes.Ldc_I4_S,
            OpCodes.Call,
            OpCodes.Callvirt,
        };

        // Category Constructor
        public static readonly OpCode[] Category_Constructor = new[]
        {
            OpCodes.Ldarg_0,
            OpCodes.Ldarga_S,
            OpCodes.Constrained,
            OpCodes.Callvirt,
            OpCodes.Call,
            OpCodes.Ldarg_0,
            OpCodes.Ldarga_S,
            OpCodes.Constrained,
            OpCodes.Callvirt,
            OpCodes.Call
        };

        // Section Constructor
        public static readonly OpCode[] Section_Constructor = new[]
        {
            OpCodes.Ldarg_0,
            OpCodes.Ldarg_1,
            OpCodes.Callvirt,
            OpCodes.Ldc_R4,
            OpCodes.Ldc_R4,
            OpCodes.Ldc_R4,
            OpCodes.Newobj,
            OpCodes.Ldc_R4,
            OpCodes.Ldc_I4_1,
        };

        // Element.SetChildren
        public static readonly OpCode[] Element_SetChildren = new[]
        {
            OpCodes.Ldarg_0,
            OpCodes.Ldarg_1,
            OpCodes.Stfld,
            OpCodes.Ldarg_0,
            OpCodes.Ldfld,
            OpCodes.Brfalse_S,
            OpCodes.Ldarg_0,
            OpCodes.Ldfld,
            OpCodes.Callvirt,
            OpCodes.Stloc_0,
            OpCodes.Br_S,
            OpCodes.Ldloc_0,
            OpCodes.Callvirt,
            OpCodes.Stloc_1,
            OpCodes.Ldloc_1,
            OpCodes.Ldarg_0,
            OpCodes.Stfld,
            OpCodes.Ldarg_0,
            OpCodes.Call,
            OpCodes.Brfalse_S,
            OpCodes.Ldloc_1,
            OpCodes.Ldc_I4_1,
            OpCodes.Callvirt,
            OpCodes.Ldloc_0,
            OpCodes.Callvirt,
            OpCodes.Brtrue_S,
            OpCodes.Leave_S,
            OpCodes.Ldloc_0,
            OpCodes.Brfalse_S,
            OpCodes.Ldloc_0,
            OpCodes.Callvirt,
            OpCodes.Endfinally,
        };

        // CheckBox Constructor
        public static readonly OpCode[] CheckBox_Constructor = new[]
        {
            OpCodes.Ldarg_0,
            OpCodes.Ldarg_3,
            OpCodes.Stfld,
            OpCodes.Ldarg_3,
            OpCodes.Brfalse_S,
            OpCodes.Ldarg_3,
            OpCodes.Ldarg_0,
            OpCodes.Ldftn,
            OpCodes.Newobj
        };

        // TextBox Constructor
        public static readonly OpCode[] TextBox_Constructor = new[]
        {
            OpCodes.Ldarg_0,
            OpCodes.Ldarg_1,
            OpCodes.Ldarg_3,
            OpCodes.Call,
            OpCodes.Ldloc_0,
            OpCodes.Ldarg_0,
            OpCodes.Stfld,
            OpCodes.Ldarg_0,
            OpCodes.Ldloc_0,
            OpCodes.Ldfld,
            OpCodes.Stfld
        };

        #endregion

        // osu.GameBase:BeginExit
        // #=zTp6JhLFlT$nSTXDxMw==:#=zzb6bonY=
        public static readonly OpCode[] GameBase_BeginExit = new[]
        {
            OpCodes.Ldsfld,
            OpCodes.Ldfld,
            OpCodes.Ldsfld,
            OpCodes.Dup,
            OpCodes.Brtrue_S,
            OpCodes.Pop,
            OpCodes.Ldsfld,
            OpCodes.Ldftn,
            OpCodes.Newobj,
            OpCodes.Dup,
            OpCodes.Stsfld,
            OpCodes.Callvirt,
            OpCodes.Callvirt,
            OpCodes.Ldc_I4_0
        };

        // Transition Time
        /// <summary>
        /// Updates the transition time for the game
        /// 100 -> 200
        /// </summary>
        public static readonly OpCode[] PatchTransition_Target = new[]
        {
            OpCodes.Ldsfld,
            OpCodes.Ldc_I4_2,
            OpCodes.Bne_Un_S,
            OpCodes.Ldsfld,
            OpCodes.Ldc_R8,
            OpCodes.Ble_Un,
            OpCodes.Ldsfld,
            OpCodes.Call,
            OpCodes.Brfalse_S,
            OpCodes.Ldsfld,
            OpCodes.Callvirt
        };

        // osu.GameplayElements.HitObjectManager::Hit
        /*
           if (#=zM8XZInS60bPkwTMTkrfebdLZOZcSyFcV90Pe2PH5kYLP == (#=zM8XZInS60bPkwTMTkrfebdLZOZcSyFcV90Pe2PH5kYLP)(-131072) &&
           !#=zS_AS2zptucP0wp1z7HOrPzQb$3ab.#=z98ZION9Ll4et$efEiA== &&
           !#=zS_AS2zptucP0wp1z7HOrPzQb$3ab.#=zcxdiu2iP13Mis9wLlw==)
         */
        public static readonly OpCode[] PatchRelaxMiss_Target = new[]
        {
            OpCodes.Ldarg_1,
            OpCodes.Ldc_I4_8,
            OpCodes.Callvirt,
            OpCodes.Stloc_S,
            OpCodes.Ldloc_0,
            OpCodes.Ldc_I4,
            OpCodes.Bne_Un,
            OpCodes.Ldsfld,
            OpCodes.Brtrue,
            OpCodes.Ldsfld,
            OpCodes.Brtrue
        };

        // LocalisationManager.GetString(OsuString stringType)
        public static readonly OpCode[] LocalisationManager_Target = new[]
        {
            OpCodes.Ldsfld,
            OpCodes.Brtrue_S,
            OpCodes.Ldsfld,
            OpCodes.Call,
            OpCodes.Pop,
            OpCodes.Nop,
            OpCodes.Ldsfld,
            OpCodes.Ldarg_0,
            OpCodes.Callvirt,
            OpCodes.Stloc_0,
            OpCodes.Leave_S,
            OpCodes.Pop,
            OpCodes.Ldsfld,
            OpCodes.Stloc_0,
            OpCodes.Leave_S,
            OpCodes.Ldloc_0,
            OpCodes.Ret,
        };

        // osu.Graphics.Notifications.NotificationManager::ShowMessage
        public static readonly OpCode[] NotificationManager_ShowMessage = new[]
        {
            OpCodes.Ldarg_1,
            OpCodes.Stfld,
            OpCodes.Ldloc_0,
            OpCodes.Ldarg_3,
            OpCodes.Stfld,
            OpCodes.Ldsfld,
            OpCodes.Ldloc_0,
            OpCodes.Ldftn,
            OpCodes.Newobj
        };

        // osu.Graphics.Notifications.NotificationManager::ShowMessageMassive
        public static readonly OpCode[] NotificationManager_ShowMessageMassive = new[]
        {
            OpCodes.Ldarg_1,
            OpCodes.Stfld,
            OpCodes.Ldloc_0,
            OpCodes.Ldarg_2,
            OpCodes.Stfld,
            OpCodes.Ldloc_0,
            OpCodes.Ldfld,
            OpCodes.Brtrue_S,
            OpCodes.Ret,
        };

        // osu.GameModes.Ranking.Ranking::loadLocalUserScore
        /*
            Mods mods = #=zS_AS2zptucP0wp1z7HOrPzQb$3ab.#=zT1XDrLO5K8XN.#=zYI457PYyRc56;
			Mods mods2 = Mods.Relax;
			Mods mods3 = mods;
			if ((mods3 & mods2) <= Mods.None)
         */
        // Pattern reflects the OBFUSCATED (live) IL, not de4dot's cleanup output: de4dot runs a
        // dead-code pass that inlines the mods constant and collapses two local stores, so a
        // pattern derived from the cleaned exe would miss on the real osu!.exe. Always derive
        // patterns against osu!.exe directly, not osu!-cleaned.exe. Shape here: two back-to-back
        // `(mods & relaxBit) > 0 || (mods & autopilotBit) > 0` checks the save path early-returns
        // on.
        public static readonly OpCode[] PatchAutoSaveRelaxScores_Target = new[]
        {
            OpCodes.Ldarg_0,
            OpCodes.Ldfld,
            OpCodes.Ldfld,
            OpCodes.Brfalse,
            OpCodes.Ldarg_0,
            OpCodes.Ldfld,
            OpCodes.Brtrue,
            OpCodes.Ldsfld,
            OpCodes.Ldfld,
            OpCodes.Call,
            OpCodes.Ldc_I4,
            OpCodes.Stloc_2,
            OpCodes.Stloc_1,
            OpCodes.Ldloc_1,
            OpCodes.Ldloc_2,
            OpCodes.And,
            OpCodes.Ldc_I4_0,
            OpCodes.Cgt,
            OpCodes.Brtrue,
            OpCodes.Ldsfld,
            OpCodes.Ldfld,
            OpCodes.Call,
            OpCodes.Ldc_I4,
            OpCodes.Stloc_2,
            OpCodes.Stloc_1,
            OpCodes.Ldloc_1,
            OpCodes.Ldloc_2,
            OpCodes.And,
            OpCodes.Ldc_I4_0,
            OpCodes.Cgt,
            OpCodes.Brtrue
        };

        // osu.GameModes.Play.Rulesets.Ruleset::IncreaseScoreHit
        /*
           if (this.#=z$kEf6vEzCgIu.#=zpQm6Nqd6BI0u() > 20 && !#=zS_AS2zptucP0wp1z7HOrPzQb$3ab.#=z98ZION9Ll4et$efEiA== &&
           !#=zS_AS2zptucP0wp1z7HOrPzQb$3ab.#=zcxdiu2iP13Mis9wLlw==)
         */
        public static readonly OpCode[] PatchRelaxComboBreak_Target = new[]
        {
            // Extended from 9 to 15 opcodes to make this pattern uniquely identifying across
            // reflection enumeration orders. The original 9-op shape (ldarg + ldfld + callvirt
            // + ldc.i4.s + ble.s + the Ldsfld/Brtrue_S quartet) matched the right method in
            // file order but reflection's GetRuntimeMethods didn't always visit types in the
            // same order, so FindMethodBySignature sometimes picked a same-shape method
            // elsewhere, the transpiler mangled the wrong IL, and the game crashed. The tail
            // added here (Ldc_I4 → Call → Ldc_I4_0 → Ldc_I4_1 → Ldc_I4_7 → Call) is specific
            // to the hit-processing call this patch is actually targeting.
            OpCodes.Ldarg_0,
            OpCodes.Ldfld,
            OpCodes.Callvirt,
            OpCodes.Ldc_I4_S,
            OpCodes.Ble_S,
            OpCodes.Ldsfld,
            OpCodes.Brtrue_S,
            OpCodes.Ldsfld,
            OpCodes.Brtrue_S,
            OpCodes.Ldc_I4,
            OpCodes.Call,
            OpCodes.Ldc_I4_0,
            OpCodes.Ldc_I4_1,
            OpCodes.Ldc_I4_7,
            OpCodes.Call
        };

        public static readonly OpCode[] PlayerInitialize_Target = new[]
        {
            OpCodes.Ldarg_0,
            OpCodes.Call,
            OpCodes.Stfld,
            OpCodes.Ldarg_0,
            OpCodes.Call,
            OpCodes.Ldc_I4_0,
            OpCodes.Stsfld,
            OpCodes.Ldsfld,
            OpCodes.Ldarg_0,
            OpCodes.Ldftn,
            OpCodes.Newobj
        };

        public static readonly OpCode[] PlayerUpdate_Target = new[]
        {
            OpCodes.Ldarg_0,
            OpCodes.Call,
            OpCodes.Ldarg_0,
            OpCodes.Call,
            OpCodes.Brfalse_S,
            OpCodes.Ret,
            OpCodes.Ldarg_0,
            OpCodes.Ldfld,
            OpCodes.Brfalse_S,
            OpCodes.Ldarg_0,
            OpCodes.Ldfld,
            OpCodes.Callvirt,
        };

        public static readonly OpCode[] PlayerOnLoadComplete_Target = new[]
        {
            OpCodes.Ldc_I4_0,
            OpCodes.Stsfld,
            OpCodes.Ldarg_1,
            OpCodes.Brtrue_S,
            OpCodes.Call,
            OpCodes.Ldc_I4_1,
            OpCodes.Call,
            OpCodes.Ldc_I4,
            OpCodes.Call,
            OpCodes.Call,
            OpCodes.Ldc_I4,
            OpCodes.Ldnull,
            OpCodes.Call,
            OpCodes.Ldarg_0,
            OpCodes.Ldfld,
            OpCodes.Ldfld,
            OpCodes.Newobj,
            OpCodes.Call,
            OpCodes.Ldc_I4_1,
            OpCodes.Ret,
            OpCodes.Ldarg_0,
            OpCodes.Ldfld,
            OpCodes.Ldfld,
            OpCodes.Call,
            OpCodes.Ldc_I4_1,
            OpCodes.Call,
            OpCodes.Ldarg_0,
            OpCodes.Ldc_I4_1
        };

        public static readonly OpCode[] TextureManager_Load = new[]
        {
            OpCodes.Ldarg_0,
            OpCodes.Brtrue_S,
            OpCodes.Ldnull,
            OpCodes.Ret,
            OpCodes.Ldsfld,
            OpCodes.Brfalse_S,
            OpCodes.Ldarg_1,
            OpCodes.Ldc_I4_4,
            OpCodes.Beq_S,
            OpCodes.Ldarg_1,
            OpCodes.Ldc_I4_S,
            OpCodes.And,
        };

        public static readonly OpCode[] Sprite_Constructor = new[]
        {
            OpCodes.Ldarg_0,
            OpCodes.Ldarg_S,
            OpCodes.Call,
            OpCodes.Ldarg_0,
            OpCodes.Ldarg_1,
            OpCodes.Stfld,
            OpCodes.Ldarg_0,
            OpCodes.Ldarg_3,
            OpCodes.Stfld,
            OpCodes.Ldarg_0,
            OpCodes.Callvirt,
            OpCodes.Ldarg_0,
            OpCodes.Callvirt
        };

        public static readonly OpCode[] Text_Constructor = new[]
        {
            OpCodes.Ldarg_0,
            OpCodes.Ldc_I4_1,
            OpCodes.Stfld,
            OpCodes.Ldarg_0,
            OpCodes.Ldnull,
            OpCodes.Ldc_I4_6,
            OpCodes.Ldc_I4_0,
            OpCodes.Ldc_I4_0,
            OpCodes.Ldarg_3
        };

        public static readonly OpCode[] Text_Setter = new[]
        {
            OpCodes.Ldarg_0,
            OpCodes.Ldfld,
            OpCodes.Ldarg_1,
            OpCodes.Call,
            OpCodes.Brfalse_S,
            OpCodes.Ret,
            OpCodes.Ldarg_0,
            OpCodes.Ldc_I4_1,
            OpCodes.Stfld,
            OpCodes.Ldarg_0,
            OpCodes.Ldarg_1
        };

        public static readonly OpCode[] SpriteText_Constructor = new[]
        {
            OpCodes.Ldarg_0,
            OpCodes.Ldnull,
            OpCodes.Ldarg_S,
            OpCodes.Ldarg_S,
            OpCodes.Ldarg_S,
            OpCodes.Ldarg_S,
            OpCodes.Ldarg_S,
            OpCodes.Ldarg_S,
            OpCodes.Ldarg_S,
            OpCodes.Call
        };

        public static readonly OpCode[] Text_RefreshTexture = new[]
        {
            OpCodes.Ldarg_0,
            OpCodes.Ldfld,
            OpCodes.Stloc_0,
            OpCodes.Ldarg_0,
            OpCodes.Ldc_I4_0,
            OpCodes.Stfld,
            OpCodes.Ldarg_0,
            OpCodes.Callvirt,
            OpCodes.Call,
            OpCodes.Brfalse_S,
            OpCodes.Ldarg_0,
            OpCodes.Ldflda,
            OpCodes.Ldfld,
            OpCodes.Ldc_R4,
            OpCodes.Bne_Un_S,
            OpCodes.Ldarg_0,
            OpCodes.Ldarg_0
        };

        #region PkmsUsername

        // BanchoClient.connect — the inner block that writes {Username, Password} to the
        // login StreamWriter. Anchoring on the surrounding `set_NewLine → WriteLine(username)
        // → WriteLine(password.Get(Hashed))` sequence; both Bindable<string>::op_Implicit and
        // OsuPassword::Get are single callvirt frames that make this a very distinctive 13-
        // opcode window. If stable's login flow changes shape (e.g. the WriteLine order
        // flips, or a wrapper swallows the StreamWriter), re-dump BanchoClient.connect and
        // adjust. Opcode-only — operand validation happens in the transpiler.
        public static readonly OpCode[] PkmsUsername_Target = new[]
        {
            OpCodes.Ldloc_0,       // streamWriter (for set_NewLine)
            OpCodes.Ldc_I4,        // obfuscator string id
            OpCodes.Call,          // A::A(int32) -> string
            OpCodes.Callvirt,      // TextWriter.set_NewLine
            OpCodes.Ldloc_0,       // streamWriter
            OpCodes.Ldsfld,        // ConfigManager.sUsername  (Bindable<string>)
            OpCodes.Call,          // Bindable`1<string>::op_Implicit -> string
            OpCodes.Callvirt,      // TextWriter.WriteLine(string)
            OpCodes.Ldloc_0,       // streamWriter
            OpCodes.Ldsfld,        // ConfigManager.Password (OsuPassword)
            OpCodes.Ldc_I4_0,      // Representation.Hashed = 0
            OpCodes.Callvirt,      // OsuPassword.Get
            OpCodes.Callvirt,      // TextWriter.WriteLine(string)
        };

        #endregion
    }
}
