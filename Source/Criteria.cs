using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace RimEmbark
{
    public class TileInfo
    {
        public PlanetTile id;
        public SurfaceTile tile;
        public float avgTemp;
        public float minTemp;
        public float maxTemp;
        public int growingDays;
        public int settlementDistance;
    }

    public class RangeCriterion
    {
        public string key;
        public string labelKey;
        public string valueLabelKey;
        public ToStringStyle style;
        public float step;
        public Func<TileInfo, float> value;
        public Func<bool> available = () => true;
    }

    public class OptionCriterion
    {
        public string key;
        public string labelKey;
        public bool defaultMatchAll;
        public Action<TileInfo, IOptionCollector> collect;
        public Func<bool> available = () => true;
    }

    public interface IOptionCollector
    {
        void Add(string key, string label, string tooltip = null);

        void Add(Def def);
    }

    public static class Criteria
    {
        public const int MaxSettlementDistance = 100;

        public static readonly List<RangeCriterion> Ranges = new List<RangeCriterion>
        {
            new RangeCriterion
            {
                key = "AvgTemp", labelKey = "RimEmbark_AvgTemp", style = ToStringStyle.Temperature, step = 1f,
                value = t => t.avgTemp,
            },
            new RangeCriterion
            {
                key = "MinTemp", labelKey = "RimEmbark_MinTemp", style = ToStringStyle.Temperature, step = 1f,
                value = t => t.minTemp,
            },
            new RangeCriterion
            {
                key = "MaxTemp", labelKey = "RimEmbark_MaxTemp", style = ToStringStyle.Temperature, step = 1f,
                value = t => t.maxTemp,
            },
            new RangeCriterion
            {
                key = "GrowingDays", labelKey = "RimEmbark_GrowingPeriod", valueLabelKey = "RimEmbark_ValueDays",
                style = ToStringStyle.Integer, step = 5f, value = t => t.growingDays,
            },
            new RangeCriterion
            {
                key = "Rainfall", labelKey = "RimEmbark_Rainfall", valueLabelKey = "RimEmbark_ValueMm",
                style = ToStringStyle.Integer, step = 10f, value = t => t.tile.rainfall,
            },
            new RangeCriterion
            {
                key = "Elevation", labelKey = "RimEmbark_Elevation", valueLabelKey = "RimEmbark_ValueMeters",
                style = ToStringStyle.Integer, step = 10f, value = t => t.tile.elevation,
            },
            new RangeCriterion
            {
                key = "MovementDifficulty", labelKey = "RimEmbark_MovementDifficulty", style = ToStringStyle.FloatOne,
                step = 0.1f, value = MovementDifficulty,
            },
            new RangeCriterion
            {
                key = "AnimalDensity", labelKey = "RimEmbark_AnimalDensity", style = ToStringStyle.FloatOne,
                step = 0.1f, value = t => t.tile.AnimalDensity,
            },
            new RangeCriterion
            {
                key = "Pollution", labelKey = "RimEmbark_Pollution", style = ToStringStyle.PercentZero, step = 0.01f,
                value = t => t.tile.pollution, available = () => ModsConfig.BiotechActive,
            },
            new RangeCriterion
            {
                key = "SettlementDistance", labelKey = "RimEmbark_SettlementDistance",
                valueLabelKey = "RimEmbark_ValueTiles", style = ToStringStyle.Integer, step = 1f,
                value = t => t.settlementDistance,
            },
        };

        public static readonly List<OptionCriterion> Options = new List<OptionCriterion>
        {
            new OptionCriterion
            {
                key = "Biome", labelKey = "RimEmbark_Biome",
                collect = (t, c) =>
                {
                    foreach (BiomeDef biome in t.tile.Biomes)
                    {
                        if (biome != null)
                        {
                            c.Add(biome);
                        }
                    }
                },
            },
            new OptionCriterion
            {
                key = "Terrain", labelKey = "RimEmbark_Terrain",
                collect = (t, c) =>
                {
                    Hilliness hilliness = t.tile.HillinessLabel;
                    if (hilliness != Hilliness.Undefined)
                    {
                        c.Add(hilliness.ToString(), hilliness.GetLabelCap());
                    }
                },
            },
            new OptionCriterion
            {
                key = "Stone", labelKey = "RimEmbark_Stone", defaultMatchAll = true,
                collect = (t, c) =>
                {
                    if (!t.tile.PrimaryBiome.canBuildBase)
                    {
                        return;
                    }
                    foreach (ThingDef rock in Find.World.NaturalRockTypesIn(t.id))
                    {
                        c.Add(rock);
                    }
                },
            },
            new OptionCriterion
            {
                key = "River", labelKey = "RimEmbark_River",
                collect = (t, c) =>
                {
                    // The terrain tab shows only the largest river link, smaller ones are tributaries.
                    List<SurfaceTile.RiverLink> rivers = t.tile.Rivers;
                    if (rivers.NullOrEmpty())
                    {
                        return;
                    }
                    RiverDef largest = rivers[0].river;
                    foreach (SurfaceTile.RiverLink link in rivers)
                    {
                        if (link.river.degradeThreshold > largest.degradeThreshold)
                        {
                            largest = link.river;
                        }
                    }
                    c.Add(largest);
                },
            },
            new OptionCriterion
            {
                key = "Road", labelKey = "RimEmbark_Road",
                collect = (t, c) =>
                {
                    List<SurfaceTile.RoadLink> roads = t.tile.Roads;
                    if (roads == null)
                    {
                        return;
                    }
                    foreach (SurfaceTile.RoadLink link in roads)
                    {
                        c.Add(link.road);
                    }
                },
            },
            new OptionCriterion
            {
                key = "Feature", labelKey = "RimEmbark_Feature", defaultMatchAll = true,
                collect = (t, c) =>
                {
                    foreach (TileMutatorDef mutator in t.tile.Mutators)
                    {
                        AddMutator(t, c, mutator);
                    }
                },
            },
            new OptionCriterion
            {
                key = "Landmark", labelKey = "RimEmbark_Landmark", available = () => ModsConfig.OdysseyActive,
                collect = (t, c) =>
                {
                    LandmarkDef def = t.tile.Landmark?.def;
                    if (def != null)
                    {
                        c.Add(def);
                    }
                },
            },
            new OptionCriterion
            {
                key = "Other", labelKey = "RimEmbark_Other", defaultMatchAll = true,
                collect = (t, c) =>
                {
                    if (t.tile.IsCoastal)
                    {
                        c.Add("Coastal", "RimEmbark_Coastal".Translate());
                    }
                    if (!t.tile.Rivers.NullOrEmpty())
                    {
                        c.Add("AnyRiver", "RimEmbark_AnyRiver".Translate());
                    }
                    if (!t.tile.Roads.NullOrEmpty())
                    {
                        c.Add("AnyRoad", "RimEmbark_AnyRoad".Translate());
                    }
                    if (t.tile.Landmark != null)
                    {
                        c.Add("AnyLandmark", "RimEmbark_AnyLandmark".Translate());
                    }
                    if (t.tile.PrimaryBiome.isExtremeBiome)
                    {
                        c.Add("ExtremeBiome", "RimEmbark_ExtremeBiome".Translate());
                    }
                },
            },
        };

        // WorldPathGrid.CalculatedMovementDifficultyAt with the road multiplier, as the terrain tab shows it, but
        // without the winter offset, which depends on the date.
        private static float MovementDifficulty(TileInfo t)
        {
            float hills;
            switch (t.tile.hilliness)
            {
                case Hilliness.SmallHills: hills = 0.5f; break;
                case Hilliness.LargeHills: hills = 1.5f; break;
                case Hilliness.Mountainous: hills = 3f; break;
                default: hills = 0f; break;
            }
            float difficulty = t.tile.PrimaryBiome.movementDifficulty + hills;
            return difficulty * Find.WorldGrid.GetRoadMovementDifficultyMultiplier(t.id, PlanetTile.Invalid);
        }

        // Some mutators label themselves per tile ("Mineral rich (gold)", animal habitats, groves, stockpiles), as
        // the terrain tab shows them; each variant becomes its own option.
        private static void AddMutator(TileInfo t, IOptionCollector c, TileMutatorDef mutator)
        {
            string label;
            string description;
            try
            {
                label = mutator.Label(t.id);
                description = mutator.Description(t.id);
            }
            catch (Exception e)
            {
                Log.WarningOnce($"[Rim Embark] Could not get the label of {mutator.defName} on tile {t.id}: {e.Message}", mutator.shortHash);
                label = mutator.label;
                description = mutator.description;
            }
            if (label.NullOrEmpty())
            {
                c.Add(mutator);
                return;
            }
            string key = label == mutator.label ? mutator.defName : mutator.defName + ":" + label;
            c.Add(key, label.CapitalizeFirst(), description);
        }
    }
}
