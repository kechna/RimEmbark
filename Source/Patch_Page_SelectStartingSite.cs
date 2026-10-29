using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimEmbark
{
    [HarmonyPatch(typeof(Page_SelectStartingSite))]
    public static class Patch_Page_SelectStartingSite
    {
        [HarmonyPostfix]
        [HarmonyPatch(nameof(Page_SelectStartingSite.PostOpen))]
        public static void PostOpen()
        {
            EmbarkSearch.OnPageOpened();
            Find.WindowStack.Add(new Window_EmbarkFilters());
        }

        [HarmonyPostfix]
        [HarmonyPatch(nameof(Page_SelectStartingSite.PostClose))]
        public static void PostClose()
        {
            Find.WindowStack.TryRemove(typeof(Window_EmbarkFilters), doCloseSound: false);
            EmbarkSearch.OnPageClosed();
        }

        [HarmonyPostfix]
        [HarmonyPatch(nameof(Page_SelectStartingSite.ExtraOnGUI))]
        public static void ExtraOnGUI()
        {
            EmbarkSearch.Update();
            if (Find.WindowStack.IsOpen<Window_EmbarkFilters>())
            {
                return;
            }
            Rect rect = new Rect(UI.screenWidth - 170f, 50f, 160f, 35f);
            if (Widgets.ButtonText(rect, "RimEmbark_OpenFilters".Translate()))
            {
                Find.WindowStack.Add(new Window_EmbarkFilters());
            }
        }
    }
}
