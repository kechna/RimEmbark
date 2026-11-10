using HarmonyLib;
using Verse.Profile;

namespace RimEmbark
{
    // Runs when the game or world is unloaded, so kept caches don't hold on to an old world.
    [HarmonyPatch(typeof(MemoryUtility), nameof(MemoryUtility.ClearAllMapsAndWorld))]
    public static class Patch_MemoryUtility
    {
        [HarmonyPostfix]
        public static void ClearAllMapsAndWorld()
        {
            EmbarkSearch.ClearKept();
        }
    }
}
