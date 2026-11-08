using HarmonyLib;
using RimWorld;

namespace RimEmbark
{
    // Going back from the page can also destroy the world, after which the world interface no longer updates.
    [HarmonyPatch(typeof(Page_SelectStartingSite), nameof(Page_SelectStartingSite.PostClose))]
    public static class Patch_Page_SelectStartingSite
    {
        [HarmonyPostfix]
        public static void PostClose()
        {
            EmbarkSearch.SetContext(EmbarkContext.None);
        }
    }
}
