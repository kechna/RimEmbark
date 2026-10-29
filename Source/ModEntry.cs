using HarmonyLib;
using Verse;

namespace RimEmbark
{
    [StaticConstructorOnStartup]
    public static class ModEntry
    {
        static ModEntry()
        {
            new Harmony("Kechna.RimEmbark").PatchAll();
        }
    }
}
