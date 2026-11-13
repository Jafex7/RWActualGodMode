using HarmonyLib;
using UnityEngine;
using Verse;

namespace ActualGodMode
{
    public class ActualGodModeMod : Mod
    {
        public const string PackageId = "jafex.actualgodmode";

        public static ActualGodModeSettings Settings { get; private set; }

        public ActualGodModeMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<ActualGodModeSettings>();
            new Harmony(PackageId).PatchAll(typeof(ActualGodModeMod).Assembly);
            Log.Message("[Actual God Mode] Loaded.");
        }

        public override string SettingsCategory() => "AGM_SettingsCategory".Translate();

        public override void DoSettingsWindowContents(Rect inRect)
        {
            Settings.DoWindowContents(inRect);
        }
    }
}
