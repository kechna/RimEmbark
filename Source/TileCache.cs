using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace RimEmbark
{
    public class RangeData
    {
        public RangeCriterion criterion;
        public float[] values;
        public float min;
        public float max;
    }

    public class OptionData : IOptionCollector
    {
        public OptionCriterion criterion;
        public readonly List<string> keys = new List<string>();
        // Every saved filter key of an option; defs that share a label are merged into one option.
        public readonly List<List<string>> filterKeys = new List<List<string>>();
        public readonly List<string> labels = new List<string>();
        public readonly List<string> tooltips = new List<string>();
        public readonly List<int> counts = new List<int>();
        // The options of cached tile i are values[tileStart[i]..tileStart[i + 1]); flat, since caches are kept.
        public int[] tileStart;
        public int[] values;

        // Options sorted by label, for display.
        public int[] order;

        private readonly Dictionary<string, int> indexByKey = new Dictionary<string, int>();
        private readonly Dictionary<string, int> indexByLabel = new Dictionary<string, int>();
        private readonly List<int> current = new List<int>();
        private List<int> buildingStart = new List<int> { 0 };
        private List<int> buildingValues = new List<int>();

        public List<string> FilterKeys(string key)
        {
            return indexByKey.TryGetValue(key, out int index) ? filterKeys[index] : null;
        }

        public void BeginTile()
        {
            current.Clear();
        }

        public void EndTile()
        {
            buildingValues.AddRange(current);
            buildingStart.Add(buildingValues.Count);
        }

        public void Add(Def def)
        {
            Add(def.defName, def.label.NullOrEmpty() ? def.defName : def.LabelCap.ToString(), def.description);
        }

        public void Add(string key, string label, string tooltip = null)
        {
            if (!indexByKey.TryGetValue(key, out int index))
            {
                if (indexByLabel.TryGetValue(label, out index))
                {
                    filterKeys[index].Add(EmbarkFilter.OptionKey(criterion.key, key));
                    if (!tooltip.NullOrEmpty() && (tooltips[index] == null || !tooltips[index].Contains(tooltip)))
                    {
                        tooltips[index] = tooltips[index].NullOrEmpty() ? tooltip : tooltips[index] + "\n\n" + tooltip;
                    }
                }
                else
                {
                    index = keys.Count;
                    indexByLabel[label] = index;
                    keys.Add(key);
                    filterKeys.Add(new List<string> { EmbarkFilter.OptionKey(criterion.key, key) });
                    labels.Add(label);
                    tooltips.Add(tooltip);
                    counts.Add(0);
                }
                indexByKey[key] = index;
            }
            if (current.Contains(index))
            {
                return;
            }
            current.Add(index);
            counts[index]++;
        }

        public void Finish()
        {
            tileStart = buildingStart.ToArray();
            values = buildingValues.ToArray();
            buildingStart = null;
            buildingValues = null;
            List<int> sorted = new List<int>();
            for (int i = 0; i < keys.Count; i++)
            {
                sorted.Add(i);
            }
            sorted.SortBy(i => labels[i]);
            order = sorted.ToArray();
        }
    }

    // Per-tile data for every settleable surface tile, built over several frames on a new world.
    public class TileCache
    {
        private const float FrameBudgetMs = 12f;

        public readonly World world;
        public readonly PlanetLayer layer;
        // Gravships can land next to settlements, on camps and on some sites.
        public readonly bool forGravship;
        // The world objects the cached tiles depend on, when it was built.
        public readonly int worldObjectsHash;
        public int[] tileIds;
        public readonly List<RangeData> ranges = new List<RangeData>();
        public readonly List<OptionData> options = new List<OptionData>();
        // Per cached tile; null without Faction Territories.
        public bool[] inFactionTerritory;
        private int territoryRevision = -1;

        private readonly List<int> buildingTileIds = new List<int>();
        private readonly List<List<float>> buildingRanges = new List<List<float>>();
        private readonly TileInfo info = new TileInfo();
        private readonly Stopwatch stopwatch = new Stopwatch();
        private int[] settlementDistance;
        private IDictionary claimedTiles;
        private readonly List<bool> buildingTerritory = new List<bool>();
        private int nextTile;

        // Same sampling as GenTemperature, but independent of the tile so it's computed once.
        private static float[] twelfthSeasonFactors;
        private static float minSeasonFactor;
        private static float maxSeasonFactor;

        public bool Done { get; private set; }

        public float Progress => layer.TilesCount == 0 ? 1f : (float)nextTile / layer.TilesCount;

        public int Count => tileIds?.Length ?? 0;

        public TileCache(World world, PlanetLayer layer, bool forGravship)
        {
            this.world = world;
            this.layer = layer;
            this.forGravship = forGravship;
            worldObjectsHash = WorldObjectsHash(layer);
            foreach (RangeCriterion criterion in Criteria.Ranges)
            {
                if (criterion.available())
                {
                    ranges.Add(new RangeData { criterion = criterion });
                    buildingRanges.Add(new List<float>());
                }
            }
            foreach (OptionCriterion criterion in Criteria.Options)
            {
                if (criterion.available())
                {
                    options.Add(new OptionData { criterion = criterion });
                }
            }
        }

        // Settleability and settlement distances only change with these objects, so a cache built for the same world
        // and objects can be kept between screens. Caravans and other moving objects can have a map and don't count.
        public static int WorldObjectsHash(PlanetLayer layer)
        {
            int hash = 17;
            foreach (WorldObject worldObject in Find.WorldObjects.AllWorldObjects)
            {
                PlanetTile tile = worldObject.Tile;
                if (!tile.Valid || tile.Layer != layer)
                {
                    continue;
                }
                if (worldObject is MapParent || worldObject is PeaceTalks || !worldObject.def.canHaveMap)
                {
                    hash = Gen.HashCombineInt(hash, worldObject.ID);
                    hash = Gen.HashCombineInt(hash, tile.tileId);
                }
            }
            return hash;
        }

        public bool IsCurrent(World world, PlanetLayer layer)
        {
            return this.world == world && this.layer == layer && worldObjectsHash == WorldObjectsHash(layer);
        }

        // Rereads what changes without world objects changing, for a cache reused on a later screen.
        public void Refresh()
        {
            if (!Done)
            {
                return;
            }
            foreach (RangeData data in ranges)
            {
                if (!data.criterion.changesInGame)
                {
                    continue;
                }
                for (int i = 0; i < tileIds.Length; i++)
                {
                    info.id = layer.PlanetTileForID(tileIds[i]);
                    info.tile = (SurfaceTile)layer[tileIds[i]];
                    data.values[i] = data.criterion.value(info);
                }
                SetBounds(data);
            }
            info.tile = null;
            IDictionary claimed = FactionTerritoriesCompat.ClaimedTiles(out int revision);
            if (claimed != null && (revision != territoryRevision || revision == -1))
            {
                territoryRevision = revision;
                inFactionTerritory = new bool[tileIds.Length];
                for (int i = 0; i < tileIds.Length; i++)
                {
                    inFactionTerritory[i] = FactionTerritoriesCompat.IsClaimed(claimed, tileIds[i]);
                }
            }
        }

        public void Step()
        {
            if (Done)
            {
                return;
            }
            if (settlementDistance == null)
            {
                // A flood fill over the whole planet; give it a frame of its own.
                InitSeasonFactors();
                settlementDistance = ComputeSettlementDistances();
                claimedTiles = FactionTerritoriesCompat.ClaimedTiles(out territoryRevision);
                return;
            }
            stopwatch.Restart();
            int count = layer.TilesCount;
            while (nextTile < count)
            {
                AddTile(nextTile);
                nextTile++;
                if ((nextTile & 15) == 0 && stopwatch.Elapsed.TotalMilliseconds > FrameBudgetMs)
                {
                    return;
                }
            }
            Finish();
        }

        private void AddTile(int tileId)
        {
            PlanetTile id = layer.PlanetTileForID(tileId);
            if (!(layer[tileId] is SurfaceTile tile) || (!forGravship && settlementDistance[tileId] < 2))
            {
                return;
            }
            if (!tile.PrimaryBiome.canBuildBase || !tile.PrimaryBiome.implemented || tile.hilliness == Hilliness.Impassable)
            {
                return;
            }
            if (!TileFinder.IsValidTileForNewSettlement(id, null, forGravship))
            {
                return;
            }

            info.id = id;
            info.tile = tile;
            info.settlementDistance = settlementDistance[tileId];
            FillClimate(info);

            buildingTileIds.Add(tileId);
            if (claimedTiles != null)
            {
                buildingTerritory.Add(FactionTerritoriesCompat.IsClaimed(claimedTiles, tileId));
            }
            for (int i = 0; i < ranges.Count; i++)
            {
                buildingRanges[i].Add(ranges[i].criterion.value(info));
            }
            foreach (OptionData option in options)
            {
                option.BeginTile();
                option.criterion.collect(info, option);
                option.EndTile();
            }
        }

        private void Finish()
        {
            tileIds = buildingTileIds.ToArray();
            for (int i = 0; i < ranges.Count; i++)
            {
                RangeData data = ranges[i];
                data.values = buildingRanges[i].ToArray();
                SetBounds(data);
            }
            foreach (OptionData option in options)
            {
                option.Finish();
            }
            if (claimedTiles != null)
            {
                inFactionTerritory = buildingTerritory.ToArray();
            }
            // Kept caches outlive the build; don't hold a second copy of every value.
            buildingTileIds.Clear();
            buildingTileIds.Capacity = 0;
            buildingRanges.Clear();
            buildingTerritory.Clear();
            buildingTerritory.Capacity = 0;
            settlementDistance = null;
            claimedTiles = null;
            info.tile = null;
            Done = true;
        }

        private static void SetBounds(RangeData data)
        {
            float min = float.MaxValue;
            float max = float.MinValue;
            foreach (float value in data.values)
            {
                min = Mathf.Min(min, value);
                max = Mathf.Max(max, value);
            }
            if (data.values.Length == 0)
            {
                min = 0f;
                max = 1f;
            }
            float step = data.criterion.step;
            data.min = Mathf.Floor(min / step) * step;
            data.max = Mathf.Ceil(max / step) * step;
            if (data.max <= data.min)
            {
                data.max = data.min + step;
            }
        }

        private static void FillClimate(TileInfo info)
        {
            float baseTemp = info.tile.temperature;
            float amplitude = -GenTemperature.SeasonalShiftAmplitudeAt(info.id);
            info.avgTemp = baseTemp;
            info.minTemp = baseTemp + Mathf.Min(minSeasonFactor * amplitude, maxSeasonFactor * amplitude);
            info.maxTemp = baseTemp + Mathf.Max(minSeasonFactor * amplitude, maxSeasonFactor * amplitude);
            int growingTwelfths = 0;
            foreach (float factor in twelfthSeasonFactors)
            {
                float temp = baseTemp + factor * amplitude;
                // Vanilla growing period: plants grow optimally between 6 and 42 °C.
                if (temp >= 6f && temp <= 42f)
                {
                    growingTwelfths++;
                }
            }
            info.growingDays = growingTwelfths * GenDate.DaysPerTwelfth;
        }

        private static void InitSeasonFactors()
        {
            if (twelfthSeasonFactors != null)
            {
                return;
            }
            twelfthSeasonFactors = new float[12];
            for (int twelfth = 0; twelfth < 12; twelfth++)
            {
                float sum = 0f;
                for (int i = 0; i < 120; i++)
                {
                    int absTick = 300000 * twelfth + 30000 + Mathf.RoundToInt(i / 120f * 300000f);
                    sum += SeasonFactor(absTick);
                }
                twelfthSeasonFactors[twelfth] = sum / 120f;
            }
            minSeasonFactor = float.MaxValue;
            maxSeasonFactor = float.MinValue;
            for (int absTick = 0; absTick < 3600000; absTick += 27000)
            {
                float factor = SeasonFactor(Mathf.Max(absTick, 1));
                minSeasonFactor = Mathf.Min(minSeasonFactor, factor);
                maxSeasonFactor = Mathf.Max(maxSeasonFactor, factor);
            }
        }

        // GenTemperature.OffsetFromSeasonCycle without the tile's amplitude.
        private static float SeasonFactor(int absTick)
        {
            float yearPct = absTick / 60000f % 60f / 60f;
            return Mathf.Cos((float)Math.PI * 2f * (yearPct - Season.Winter.GetMiddleTwelfth(0f).GetBeginningYearPct()));
        }

        private int[] ComputeSettlementDistances()
        {
            int count = layer.TilesCount;
            int[] distance = new int[count];
            for (int i = 0; i < count; i++)
            {
                distance[i] = -1;
            }
            Queue<int> queue = new Queue<int>();
            foreach (Settlement settlement in Find.WorldObjects.Settlements)
            {
                PlanetTile tile = settlement.Tile;
                if (tile.Valid && tile.Layer == layer && distance[tile.tileId] != 0)
                {
                    distance[tile.tileId] = 0;
                    queue.Enqueue(tile.tileId);
                }
            }
            List<PlanetTile> neighbors = new List<PlanetTile>();
            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                int next = distance[current] + 1;
                if (next > Criteria.MaxSettlementDistance)
                {
                    continue;
                }
                layer.GetTileNeighbors(current, neighbors);
                foreach (PlanetTile neighbor in neighbors)
                {
                    if (distance[neighbor.tileId] == -1)
                    {
                        distance[neighbor.tileId] = next;
                        queue.Enqueue(neighbor.tileId);
                    }
                }
            }
            for (int i = 0; i < count; i++)
            {
                if (distance[i] == -1)
                {
                    distance[i] = Criteria.MaxSettlementDistance;
                }
            }
            return distance;
        }
    }
}
