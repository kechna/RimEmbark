using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimEmbark
{
    // Adds the filter UI toggle to the world map's bottom right visibility toggles.
    [HarmonyPatch(typeof(PlaySettings), nameof(PlaySettings.DoPlaySettingsGlobalControls))]
    public static class Patch_PlaySettings
    {
        // Loaded on first draw: Harmony touches this class before textures are loaded.
        private static Texture2D icon;

        [HarmonyPostfix]
        public static void Postfix(WidgetRow row, bool worldView)
        {
            if (!worldView || Patch_WorldInterface.AvailableContext == EmbarkContext.None)
            {
                return;
            }
            icon ??= ContentFinder<Texture2D>.Get("UI/RimEmbark/ShowFilters");
            row.ToggleableIcon(ref RimEmbarkMod.Settings.showFilters, icon, "RimEmbark_ShowOnMapTip".Translate(), SoundDefOf.Mouseover_ButtonToggle);
        }
    }
}
