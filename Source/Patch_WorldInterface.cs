using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace RimEmbark
{
    public enum EmbarkContext
    {
        None,
        StartingSite,
        Gravship,
        Caravan,
    }

    // Runs in the main menu and in game, so it covers the starting site page, the gravship tile picker and the
    // caravan route planner.
    [HarmonyPatch(typeof(WorldInterface))]
    public static class Patch_WorldInterface
    {
        // The current screen, even while the UI is hidden from the map toggles.
        public static EmbarkContext AvailableContext;

        [HarmonyPostfix]
        [HarmonyPatch(nameof(WorldInterface.WorldInterfaceUpdate))]
        public static void WorldInterfaceUpdate()
        {
            EmbarkSearch.SetContext(CurrentContext());
            EmbarkSearch.Update();
        }

        [HarmonyPostfix]
        [HarmonyPatch(nameof(WorldInterface.WorldInterfaceOnGUI))]
        public static void WorldInterfaceOnGUI()
        {
            if (!EmbarkSearch.Active || !WorldRendererUtility.WorldSelected || Find.WindowStack.IsOpen<Window_EmbarkFilters>())
            {
                return;
            }
            Rect rect = new Rect(UI.screenWidth - 170f, 50f, 160f, 35f);
            if (Widgets.ButtonText(rect, "RimEmbark_OpenFilters".Translate()))
            {
                Find.WindowStack.Add(new Window_EmbarkFilters());
            }
        }

        private static EmbarkContext CurrentContext()
        {
            AvailableContext = DetectContext();
            return RimEmbarkMod.Settings.showFilters ? AvailableContext : EmbarkContext.None;
        }

        private static EmbarkContext DetectContext()
        {
            if (Current.ProgramState != ProgramState.Playing)
            {
                return Find.WindowStack.IsOpen<Page_SelectStartingSite>() ? EmbarkContext.StartingSite : EmbarkContext.None;
            }
            if (!WorldRendererUtility.WorldSelected)
            {
                return EmbarkContext.None;
            }
            if (Find.TilePicker.Active && Find.TilePicker.ForGravship)
            {
                return EmbarkContext.Gravship;
            }
            return Find.WorldRoutePlanner.Active ? EmbarkContext.Caravan : EmbarkContext.None;
        }
    }
}
