using System.Collections.Generic;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace RimEmbark
{
    public static class EmbarkSearch
    {
        public static TileCache Cache;

        public static readonly List<int> Matches = new List<int>();

        // Bumped whenever Matches changes, so the overlay knows to rebuild its mesh.
        public static int ResultVersion;

        public static bool PageOpen;

        // Tiles selected on this page, by hand or by JumpToRandomMatch, which it skips until every match was seen.
        private static readonly HashSet<int> Visited = new HashSet<int>();

        // Dragging a slider changes the filter every frame; recomputing and rebuilding the overlay mesh that often
        // stutters on large worlds.
        private const float RecomputeInterval = 0.15f;

        private static int computedFilterVersion = -1;
        private static int lastUpdateFrame = -1;
        private static float lastRecomputeTime = -1f;

        public static EmbarkFilter Filter => RimEmbarkMod.Settings.filter;

        // Results can lag the filter by up to RecomputeInterval; showing the previous ones avoids flicker.
        public static bool HasResults => Cache != null && Cache.Done && computedFilterVersion != -1;

        public static bool ShowOverlay => PageOpen && Filter.showOverlay && Filter.AnyActive && HasResults;

        public static void OnPageOpened()
        {
            PageOpen = true;
            EnsureCacheForCurrentWorld();
            computedFilterVersion = -1;
        }

        private static void EnsureCacheForCurrentWorld()
        {
            World world = Find.World;
            PlanetLayer surface = Find.WorldGrid.Surface;
            if (Cache != null && Cache.world == world && Cache.layer == surface)
            {
                return;
            }
            Cache = new TileCache(world, surface);
            Visited.Clear();
            Matches.Clear();
            ResultVersion++;
            computedFilterVersion = -1;
        }

        public static void OnPageClosed()
        {
            PageOpen = false;
            Cache = null;
            Visited.Clear();
            Matches.Clear();
            ResultVersion++;
            RimEmbarkMod.Instance.WriteSettings();
        }

        public static void Update()
        {
            if (!PageOpen || Cache == null || Time.frameCount == lastUpdateFrame)
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
            Visited.Add(tileId);
            PlanetTile tile = Cache.layer.PlanetTileForID(tileId);
            Find.WorldInterface.SelectedTile = tile;
            Find.WorldCameraDriver.JumpTo(tile);
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

            for (int i = 0; i < cache.tileIds.Length; i++)
            {
                if (TileMatches(i, activeRanges, rangeValues, matchers))
                {
                    Matches.Add(cache.tileIds[i]);
                }
            }
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
                int[] values = data.tileValues[index];
                int includedHere = 0;
                foreach (int value in values)
                {
                    FilterState state = states[value];
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
