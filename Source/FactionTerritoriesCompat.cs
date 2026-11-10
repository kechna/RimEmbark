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

        // Surface tile id -> claiming faction ids, or null when unavailable. The revision changes when it's rebuilt.
        public static IDictionary ClaimedTiles(out int revision)
        {
            revision = -1;
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
                // Its game components keep it current in game. Before that it's never marked dirty, so it could still
                // hold a previous world.
                if (Current.ProgramState != ProgramState.Playing)
                {
                    AccessTools.Method(cache, "MarkDirty")?.Invoke(null, null);
                }
                IDictionary claimed = AccessTools.Method(cache, "TryGetFactionMapUnsafeForMainThreadOnly")?.Invoke(null, null) as IDictionary;
                if (AccessTools.Method(cache, "TryGetRevisionUnsafeForMainThreadOnly")?.Invoke(null, null) is int value)
                {
                    revision = value;
                }
                return claimed;
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
