using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace RimEmbark
{
    public static class EmbarkSearch
    {
        public static TileCache Cache;

        // In game the world only changes through its objects, so the gravship and caravan screens keep their cache
        // after closing. The starting site page doesn't: going back can generate another world.
        private static readonly Dictionary<EmbarkContext, TileCache> Kept = new Dictionary<EmbarkContext, TileCache>();

        public static readonly List<int> Matches = new List<int>();
        public static readonly List<int> OrderedMatches = new List<int>();

        // Bumped whenever Matches changes, so the overlay knows to rebuild its mesh.
        public static int ResultVersion;

        public static EmbarkContext Context;

        public static bool Active => Context != EmbarkContext.None;

        public static CompPilotConsole GravshipConsole;

        // Surface tiles the gravship can reach, by tile id; null outside the gravship screen.
        private static bool[] gravshipRange;

        public static bool GravshipRangeAvailable => Context == EmbarkContext.Gravship && GravshipConsole?.engine != null && !DebugSettings.ignoreGravshipRange;

        // Tiles selected on this page, which random selection skips until every match was seen.
        private static readonly HashSet<int> Visited = new HashSet<int>();
        private static readonly HashSet<int> Pinned = new HashSet<int>();
        private static readonly Dictionary<int, int> OrderedIndices = new Dictionary<int, int>();

        // Dragging a slider changes the filter every frame; recomputing and rebuilding the overlay mesh that often
        // stutters on large worlds.
        private const float RecomputeInterval = 0.15f;

        private static int computedFilterVersion = -1;
        private static int lastUpdateFrame = -1;
        private static float lastRecomputeTime = -1f;

        public static EmbarkFilter Filter => RimEmbarkMod.Settings.FilterFor(Context);

        // Results can lag the filter by up to RecomputeInterval; showing the previous ones avoids flicker.
        public static bool HasResults => Cache != null && Cache.Done && computedFilterVersion != -1;

        public static bool ShowOverlay => Active && Filter.showOverlay && Filter.AnyActive && HasResults;

        public static int SelectedMatchIndex
        {
            get
            {
                PlanetTile selected = Find.WorldSelector.SelectedTile;
                if (Cache == null || !selected.Valid || selected.Layer != Cache.layer)
                {
                    return -1;
                }
                return OrderedIndices.TryGetValue(selected.tileId, out int index) ? index : -1;
            }
        }

        public static void SetContext(EmbarkContext context)
        {
            if (context == Context)
            {
                return;
            }
            if (Active)
            {
                Find.WindowStack.TryRemove(typeof(Window_EmbarkFilters), doCloseSound: false);
                OnClosed();
            }
            Context = context;
            gravshipRange = null;
            if (Active)
            {
                EnsureCacheForCurrentWorld();
                computedFilterVersion = -1;
                Find.WindowStack.Add(new Window_EmbarkFilters());
            }
        }

        private static void EnsureCacheForCurrentWorld()
        {
            World world = Find.World;
            PlanetLayer surface = Find.WorldGrid.Surface;
            if (Cache != null && Cache.world == world && Cache.layer == surface)
            {
                return;
            }
            Cache = TakeKept(world, surface) ?? new TileCache(world, surface, Context == EmbarkContext.Gravship);
            Visited.Clear();
            Pinned.Clear();
            Matches.Clear();
            OrderedMatches.Clear();
            OrderedIndices.Clear();
            ResultVersion++;
            computedFilterVersion = -1;
        }

        private static TileCache TakeKept(World world, PlanetLayer surface)
        {
            if (!Kept.TryGetValue(Context, out TileCache cache))
            {
                return null;
            }
            Kept.Remove(Context);
            if (!cache.IsCurrent(world, surface))
            {
                return null;
            }
            cache.Refresh();
            return cache;
        }

        public static void ClearKept()
        {
            Kept.Clear();
        }

        private static void OnClosed()
        {
            if (Cache != null && (Context == EmbarkContext.Gravship || Context == EmbarkContext.Caravan))
            {
                Kept[Context] = Cache;
            }
            Context = EmbarkContext.None;
            Cache = null;
            Visited.Clear();
            Pinned.Clear();
            Matches.Clear();
            OrderedMatches.Clear();
            OrderedIndices.Clear();
            ResultVersion++;
            RimEmbarkMod.Instance.WriteSettings();
        }

        public static void Update()
        {
            if (!Active || Cache == null || Time.frameCount == lastUpdateFrame)
            {
                return;
            }
            lastUpdateFrame = Time.frameCount;
            EnsureCacheForCurrentWorld();
            PlanetTile selected = Find.WorldSelector.SelectedTile;
            if (selected.Valid && selected.Layer == Cache.layer)
            {
                Visited.Add(selected.tileId);
            }
            if (!Cache.Done)
            {
                Cache.Step();
                return;
            }
            if (computedFilterVersion != Filter.Version && Time.realtimeSinceStartup - lastRecomputeTime >= RecomputeInterval)
            {
                Recompute();
                lastRecomputeTime = Time.realtimeSinceStartup;
            }
        }

        public static void JumpToRandomMatch()
        {
            if (Matches.Count == 0)
            {
                return;
            }
            int unvisited = CountUnvisited();
            if (unvisited == 0)
            {
                // Every match was seen; start over, but not on the tile that's selected now.
                Visited.Clear();
                PlanetTile current = Find.WorldSelector.SelectedTile;
                if (current.Valid && current.Layer == Cache.layer && Matches.Count > 1)
                {
                    Visited.Add(current.tileId);
                }
                unvisited = CountUnvisited();
            }
            int pick = Rand.Range(0, unvisited);
            int tileId = -1;
            foreach (int id in Matches)
            {
                if (!Visited.Contains(id) && pick-- == 0)
                {
                    tileId = id;
                    break;
                }
            }
            JumpToMatch(tileId);
        }

        public static void JumpToMatch(int tileId)
        {
            if (Cache == null || Matches.BinarySearch(tileId) < 0)
            {
                return;
            }
            Visited.Add(tileId);
            PlanetTile tile = Cache.layer.PlanetTileForID(tileId);
            // Jumping switches to the tile's layer, which clears the selection.
            Find.WorldCameraDriver.JumpTo(tile);
            Find.WorldInterface.SelectedTile = tile;
        }

        public static bool IsPinned(int tileId) => Pinned.Contains(tileId);

        public static void TogglePin(int tileId)
        {
            if (Matches.BinarySearch(tileId) < 0)
            {
                return;
            }
            if (!Pinned.Remove(tileId))
            {
                Pinned.Add(tileId);
            }
            RebuildOrder();
        }

        private static void RebuildOrder()
        {
            OrderedMatches.Clear();
            OrderedIndices.Clear();
            foreach (int id in Matches)
            {
                if (Pinned.Contains(id))
                {
                    OrderedMatches.Add(id);
                }
            }
            foreach (int id in Matches)
            {
                if (!Pinned.Contains(id))
                {
                    OrderedMatches.Add(id);
                }
            }
            for (int i = 0; i < OrderedMatches.Count; i++)
            {
                OrderedIndices[OrderedMatches[i]] = i;
            }
        }

        private static int CountUnvisited()
        {
            int count = 0;
            foreach (int id in Matches)
            {
                if (!Visited.Contains(id))
                {
                    count++;
                }
            }
            return count;
        }

        private static void Recompute()
        {
            computedFilterVersion = Filter.Version;
            Matches.Clear();
            ResultVersion++;
            TileCache cache = Cache;

            List<RangeData> activeRanges = new List<RangeData>();
            List<FloatRange> rangeValues = new List<FloatRange>();
            foreach (RangeData data in cache.ranges)
            {
                if (Filter.TryGetRange(data.criterion.key, out FloatRange range))
                {
                    activeRanges.Add(data);
                    rangeValues.Add(range);
                }
            }

            List<OptionMatcher> matchers = new List<OptionMatcher>();
            foreach (OptionData data in cache.options)
            {
                OptionMatcher matcher = OptionMatcher.For(data, Filter);
                if (matcher != null)
                {
                    matchers.Add(matcher);
                }
            }

            bool[] territory = Filter.excludeFactionTerritories ? cache.inFactionTerritory : null;
            bool[] reachable = null;
            if (Filter.limitToGravshipRange && GravshipRangeAvailable)
            {
                gravshipRange ??= ComputeGravshipRange(cache.layer);
                reachable = gravshipRange;
            }
            for (int i = 0; i < cache.tileIds.Length; i++)
            {
                if (territory != null && territory[i])
                {
                    continue;
                }
                if (reachable != null && !reachable[cache.tileIds[i]])
                {
                    continue;
                }
                if (TileMatches(i, activeRanges, rangeValues, matchers))
                {
                    Matches.Add(cache.tileIds[i]);
                }
            }
            RebuildOrder();
        }

        // The same limits as the destination picker: the engine's range and the fuel for the path, which costs at
        // least 50.
        private static bool[] ComputeGravshipRange(PlanetLayer surface)
        {
            Building_GravEngine engine = GravshipConsole.engine;
            PlanetTile origin = GravshipConsole.parent.Tile;
            bool[] inRange = new bool[surface.TilesCount];
            if (!origin.Valid || engine.TotalFuel < 50f)
            {
                return inRange;
            }
            int maxDistance = Mathf.Min(
                Mathf.FloorToInt(engine.MaxLaunchDistance / surface.Def.rangeDistanceFactor),
                GravshipUtility.MaxDistForFuel(engine.TotalFuel, origin.Layer, surface, 10f, engine.FuelUseageFactor));
            if (maxDistance < 0)
            {
                return inRange;
            }
            PlanetTile start = origin.Layer == surface ? origin : surface.GetClosestTile_NewTemp(origin);
            surface.Filler.FloodFill(start, _ => true, (PlanetTile tile, int distance) =>
            {
                if (distance > maxDistance)
                {
                    return true;
                }
                inRange[tile.tileId] = true;
                return false;
            });
            return inRange;
        }

        private static bool TileMatches(int index, List<RangeData> ranges, List<FloatRange> rangeValues, List<OptionMatcher> matchers)
        {
            for (int r = 0; r < ranges.Count; r++)
            {
                float value = ranges[r].values[index];
                if (value < rangeValues[r].min || value > rangeValues[r].max)
                {
                    return false;
                }
            }
            foreach (OptionMatcher matcher in matchers)
            {
                if (!matcher.Matches(index))
                {
                    return false;
                }
            }
            return true;
        }

        private class OptionMatcher
        {
            private OptionData data;
            private FilterState[] states;
            private int includedCount;
            private bool matchAll;

            public static OptionMatcher For(OptionData data, EmbarkFilter filter)
            {
                FilterState[] states = new FilterState[data.keys.Count];
                int included = 0;
                bool any = false;
                for (int i = 0; i < states.Length; i++)
                {
                    states[i] = filter.GetState(data.filterKeys[i]);
                    if (states[i] == FilterState.Include)
                    {
                        included++;
                    }
                    any |= states[i] != FilterState.Any;
                }
                if (!any)
                {
                    return null;
                }
                return new OptionMatcher
                {
                    data = data,
                    states = states,
                    includedCount = included,
                    matchAll = filter.GetMatchAll(data.criterion.key, data.criterion.defaultMatchAll),
                };
            }

            public bool Matches(int index)
            {
                int[] values = data.values;
                int end = data.tileStart[index + 1];
                int includedHere = 0;
                for (int i = data.tileStart[index]; i < end; i++)
                {
                    FilterState state = states[values[i]];
                    if (state == FilterState.Exclude)
                    {
                        return false;
                    }
                    if (state == FilterState.Include)
                    {
                        includedHere++;
                    }
                }
                if (includedCount == 0)
                {
                    return true;
                }
                return matchAll ? includedHere == includedCount : includedHere > 0;
            }
        }
    }
}
