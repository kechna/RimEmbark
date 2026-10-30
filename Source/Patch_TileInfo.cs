using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimEmbark
{
    // On the starting-site page, the biome, terrain, stone, river, road, landmark and features in the terrain tab can
    // be clicked to include or exclude them, and filtered ones are highlighted there and in the inspect pane.
    [HarmonyPatch]
    public static class Patch_TileInfo
    {
        private const float TitleHeight = 35f;

        private class Token
        {
            public string criterion;
            public string key;
            public string label;
            public string tooltip;
        }

        private static Tile drawingTile;
        private static PlanetTile drawingId;
        private static string terrainLabel;
        private static string roadLabel;
        private static string riverLabel;
        private static string stoneLabel;
        private static string landmarkLabel;
        private static string featuresLabel;

        [HarmonyPrefix]
        [HarmonyPatch(typeof(WITab_Terrain), "DrawScrollContents")]
        public static void DrawScrollContentsPrefix(Rect infoRect)
        {
            if (!EmbarkSearch.PageOpen)
            {
                return;
            }
            // FillTab draws the biome as the title, just above infoRect.
            BiomeDef biome = Find.WorldGrid[Find.WorldSelector.SelectedTile].PrimaryBiome;
            Text.Font = GameFont.Medium;
            Rect rect = new Rect(infoRect.x, infoRect.y - TitleHeight, Text.CalcSize(biome.LabelCap).x, Text.LineHeight);
            Text.Font = GameFont.Small;
            DrawToken(rect, new Token
            {
                criterion = "Biome",
                key = biome.defName,
                tooltip = Title(biome.LabelCap) + "\n" + biome.description,
            }, drawLabel: false);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(WITab_Terrain), "ListGeometricDetails")]
        public static void ListGeometricDetailsPrefix(Tile ws, PlanetTile tile)
        {
            if (!EmbarkSearch.PageOpen)
            {
                return;
            }
            drawingTile = ws;
            drawingId = tile;
            terrainLabel = "Terrain".Translate();
            roadLabel = "Road".Translate();
            riverLabel = "River".Translate();
            stoneLabel = "StoneTypesHere".Translate();
            landmarkLabel = "Landmark".Translate();
            featuresLabel = "TileMutators".Translate();
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(WITab_Terrain), "ListGeometricDetails")]
        public static void ListGeometricDetailsPostfix()
        {
            drawingTile = null;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Listing_Standard), nameof(Listing_Standard.LabelDouble))]
        public static bool LabelDoublePrefix(Listing_Standard __instance, string leftLabel)
        {
            if (drawingTile == null)
            {
                return true;
            }
            List<Token> tokens = RowTokens(leftLabel, drawingTile, drawingId, out bool useAnd);
            if (tokens.Count == 0)
            {
                return true;
            }
            DrawRow(__instance, leftLabel, tokens, useAnd);
            return false;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(WorldInspectPane), "TileInspectString", MethodType.Getter)]
        public static void TileInspectStringPostfix(ref string __result)
        {
            if (!EmbarkSearch.PageOpen || __result == null)
            {
                return;
            }
            PlanetTile id = Find.WorldSelector.SelectedTile;
            Tile tile = Find.WorldGrid[id];
            string hilliness = tile.hilliness.GetLabelCap();
            RoadDef road = (tile as SurfaceTile)?.Roads?.Select(r => r.road).MaxBy(r => r.priority);
            string roadLine = road?.LabelCap.ToString();
            string featuresPrefix = "TileMutators".Translate() + ": ";
            string[] lines = __result.Split('\n');
            // Line 0 is the coordinates.
            for (int i = 1; i < lines.Length; i++)
            {
                string line = lines[i].TrimEnd('\r');
                string end = lines[i].Substring(line.Length);
                if (line == hilliness)
                {
                    lines[i] = Colorize(line, "Terrain", tile.HillinessLabel.ToString()) + end;
                }
                else if (line == roadLine)
                {
                    lines[i] = Colorize(line, "Road", road.defName) + end;
                }
                else if (line.StartsWith(featuresPrefix))
                {
                    List<string> labels = new List<string>();
                    foreach (Token token in FeatureTokens(tile, id))
                    {
                        labels.Add(Colorize(labels.Count == 0 ? token.label.CapitalizeFirst() : token.label, token.criterion, token.key));
                    }
                    lines[i] = featuresPrefix + string.Join(", ", labels) + end;
                }
            }
            __result = string.Join("\n", lines);
        }

        private static string Colorize(string text, string criterion, string key)
        {
            FilterState state = EmbarkSearch.Filter.GetState(FilterKeys(criterion, key));
            return state == FilterState.Include ? text.Colorize(Window_EmbarkFilters.IncludeTextColor)
                : state == FilterState.Exclude ? text.Colorize(Window_EmbarkFilters.ExcludeTextColor)
                : text;
        }

        private static string Title(string label)
        {
            return label.CapitalizeFirst().Colorize(ColoredText.TipSectionTitleColor);
        }

        private static Token DefToken(string criterion, Def def, string label)
        {
            return new Token
            {
                criterion = criterion,
                key = def.defName,
                label = label,
                tooltip = def.description.NullOrEmpty() ? Title(label) : Title(label) + "\n" + def.description,
            };
        }

        // The rows as WITab_Terrain.ListGeometricDetails builds them, split into the options of their criterion.
        private static List<Token> RowTokens(string leftLabel, Tile tile, PlanetTile id, out bool useAnd)
        {
            useAnd = false;
            List<Token> tokens = new List<Token>();
            SurfaceTile surface = tile as SurfaceTile;
            if (leftLabel == terrainLabel)
            {
                string label = tile.HillinessLabel.GetLabelCap();
                tokens.Add(new Token { criterion = "Terrain", key = tile.HillinessLabel.ToString(), label = label, tooltip = Title(label) });
            }
            else if (leftLabel == roadLabel && surface?.Roads != null)
            {
                useAnd = true;
                foreach (RoadDef road in surface.Roads.Select(r => r.road).Distinct())
                {
                    if (!tokens.Any(t => t.label == road.label))
                    {
                        tokens.Add(DefToken("Road", road, road.label));
                    }
                }
            }
            else if (leftLabel == riverLabel && surface?.Rivers != null)
            {
                RiverDef river = surface.Rivers.MaxBy(r => r.river.degradeThreshold).river;
                tokens.Add(DefToken("River", river, river.LabelCap));
            }
            else if (leftLabel == stoneLabel)
            {
                useAnd = true;
                foreach (ThingDef rock in Find.World.NaturalRockTypesIn(id))
                {
                    tokens.Add(DefToken("Stone", rock, rock.label));
                }
            }
            else if (leftLabel == landmarkLabel && tile.Landmark != null)
            {
                tokens.Add(DefToken("Landmark", tile.Landmark.def, tile.Landmark.name));
            }
            else if (leftLabel == featuresLabel)
            {
                tokens.AddRange(FeatureTokens(tile, id));
            }
            if (tokens.Count > 0)
            {
                tokens[0].label = tokens[0].label.CapitalizeFirst();
            }
            return tokens;
        }

        private static IEnumerable<Token> FeatureTokens(Tile tile, PlanetTile id)
        {
            foreach (TileMutatorDef mutator in tile.Mutators.OrderBy(m => -m.displayPriority))
            {
                Criteria.MutatorOption(id, mutator, out string key, out string label, out string description);
                yield return new Token { criterion = "Feature", key = key, label = label, tooltip = Title(label) + "\n" + description };
            }
        }

        private static List<string> FilterKeys(string criterion, string key)
        {
            List<OptionData> options = EmbarkSearch.Cache?.options;
            List<string> keys = options?.Find(o => o.criterion.key == criterion)?.FilterKeys(key);
            return keys ?? new List<string> { EmbarkFilter.OptionKey(criterion, key) };
        }

        // Lays the row out like LabelDouble, with each option as its own clickable label.
        private static void DrawRow(Listing_Standard listing, string leftLabel, List<Token> tokens, bool useAnd)
        {
            float half = listing.ColumnWidth / 2f;
            float width = listing.ColumnWidth - half;
            float lineHeight = Text.LineHeight;
            string and = AndSeparator();

            string[] separators = new string[tokens.Count];
            Rect[] rects = new Rect[tokens.Count];
            float x = 0f;
            float y = 0f;
            for (int i = 0; i < tokens.Count; i++)
            {
                separators[i] = i == tokens.Count - 1 ? "" : useAnd && i == tokens.Count - 2 ? and : ", ";
                float labelWidth = Mathf.Min(Text.CalcSize(tokens[i].label).x, width);
                float needed = labelWidth + Text.CalcSize(separators[i].TrimEnd()).x;
                if (x > 0f && x + needed > width)
                {
                    x = 0f;
                    y += lineHeight;
                }
                rects[i] = new Rect(x, y, labelWidth, lineHeight);
                x += labelWidth + Text.CalcSize(separators[i]).x;
            }

            Rect rect = listing.GetRect(Mathf.Max(Text.CalcHeight(leftLabel, half), y + lineHeight));
            Widgets.Label(rect.LeftHalf(), leftLabel);
            for (int i = 0; i < tokens.Count; i++)
            {
                Rect labelRect = new Rect(rects[i].x + rect.x + half, rects[i].y + rect.y, rects[i].width, lineHeight);
                DrawToken(labelRect, tokens[i], drawLabel: true);
                if (separators[i].Length > 0)
                {
                    Widgets.Label(new Rect(labelRect.xMax, labelRect.y, Text.CalcSize(separators[i]).x + 2f, lineHeight), separators[i]);
                }
            }
            listing.Gap(listing.verticalSpacing);
        }

        // The " and " of ToCommaList(useAnd: true) in the current language.
        private static string AndSeparator()
        {
            string pattern = "ToCommaListAnd".Translate("\u0001", "\u0002").Resolve();
            int first = pattern.IndexOf('\u0001');
            int second = pattern.IndexOf('\u0002');
            return first >= 0 && second > first ? pattern.Substring(first + 1, second - first - 1) : ", ";
        }

        private static void DrawToken(Rect rect, Token token, bool drawLabel)
        {
            List<string> keys = FilterKeys(token.criterion, token.key);
            FilterState state = EmbarkSearch.Filter.GetState(keys);
            if (state == FilterState.Include)
            {
                Widgets.DrawBoxSolid(rect, Window_EmbarkFilters.IncludeColor);
            }
            else if (state == FilterState.Exclude)
            {
                Widgets.DrawBoxSolid(rect, Window_EmbarkFilters.ExcludeColor);
            }
            Widgets.DrawHighlightIfMouseover(rect);
            if (drawLabel)
            {
                Widgets.Label(rect, token.label);
            }
            TooltipHandler.TipRegion(rect, token.tooltip + "\n\n" + "RimEmbark_TileInfoClickTip".Translate());

            Event e = Event.current;
            if (e.type == EventType.MouseDown && (e.button == 0 || e.button == 1) && Mouse.IsOver(rect))
            {
                FilterState target = e.button == 0 ? FilterState.Include : FilterState.Exclude;
                EmbarkSearch.Filter.SetState(keys, state == target ? FilterState.Any : target);
                SoundDefOf.Click.PlayOneShotOnCamera();
                e.Use();
            }
        }
    }
}
