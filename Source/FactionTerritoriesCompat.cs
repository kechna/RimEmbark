using System;
using System.Collections;
using HarmonyLib;
using Verse;

namespace RimEmbark
{
    // Faction Territories (jaeger972.factionterritories), read through reflection so there's no reference to it.
    public static class FactionTerritoriesCompat
    {
        public static readonly bool Active = ModsConfig.IsActive("jaeger972.factionterritories");

        // Surface tile id -> claiming faction ids, or null when unavailable.
        public static IDictionary ClaimedTiles()
        {
            if (!Active)
            {
                return null;
            }
            try
            {
                Type cache = AccessTools.TypeByName("FactionTerritories.TerritoryOwnershipCache");
                if (cache == null)
                {
                    return null;
                }
                // It's only marked dirty by game components, which don't run before the game starts, so it could
                // still hold a previous world.
                AccessTools.Method(cache, "MarkDirty")?.Invoke(null, null);
                return AccessTools.Method(cache, "TryGetFactionMapUnsafeForMainThreadOnly")?.Invoke(null, null) as IDictionary;
            }
            catch (Exception e)
            {
                Log.WarningOnce($"[Rim Embark] Could not read Faction Territories: {e}", 0x52454654);
                return null;
            }
        }

        public static bool IsClaimed(IDictionary claimed, int tileId)
        {
            // The values are HashSet<int>, which isn't a non-generic ICollection.
            return claimed[tileId] is IEnumerable factions && factions.GetEnumerator().MoveNext();
        }
    }
}
