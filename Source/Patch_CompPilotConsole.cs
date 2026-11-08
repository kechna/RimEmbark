using HarmonyLib;
using RimWorld;

namespace RimEmbark
{
    // The destination picker doesn't expose which ship it's for, and the range filter needs its engine.
    [HarmonyPatch(typeof(CompPilotConsole), nameof(CompPilotConsole.StartChoosingDestination_NewTemp))]
    public static class Patch_CompPilotConsole
    {
        [HarmonyPrefix]
        public static void Prefix(CompPilotConsole __instance)
        {
            EmbarkSearch.GravshipConsole = __instance;
        }
    }
}
