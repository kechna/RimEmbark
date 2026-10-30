using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimEmbark
{
    public class Window_EmbarkFilters : Window
    {
        private const float RowHeight = 24f;
        private const float HeaderHeight = 28f;
        private const float SliderHeight = 32f;
        private const float ButtonSize = 24f;
        private const float Gap = 4f;

        private static readonly Color IncludeColor = new Color(0.2f, 0.8f, 0.3f, 0.25f);
        private static readonly Color ExcludeColor = new Color(0.9f, 0.2f, 0.2f, 0.25f);

        // Kept across reopening the window.
        private static readonly HashSet<string> Expanded = new HashSet<string> { "Ranges" };

        private static readonly bool MapPreviewActive = ModsConfig.IsActive("m00nl1ght.MapPreview");
        private readonly HashSet<Window> movedMapPreviewWindows = new HashSet<Window>();

        private Vector2 scrollPosition;
        private float viewHeight;
        private float visibleTop;
        private float visibleBottom;
        private string search = "";

        public override Vector2 InitialSize => new Vector2(400f, Mathf.Min(UI.screenHeight - 220f, 780f));

        private static EmbarkFilter Filter => EmbarkSearch.Filter;

        public Window_EmbarkFilters()
        {
            layer = WindowLayer.Dialog;
            doCloseX = true;
            draggable = true;
            resizeable = true;
            preventCameraMotion = false;
            absorbInputAroundWindow = false;
            closeOnClickedOutside = false;
            closeOnAccept = false;
            closeOnCancel = false;
            focusWhenOpened = false;
            drawShadow = false;
            soundAppear = null;
            soundClose = null;
        }

        protected override void SetInitialSizeAndPosition()
        {
            Vector2 size = InitialSize;
            windowRect = new Rect(UI.screenWidth - size.x - 10f, 50f, size.x, size.y).Rounded();
        }

        public override void WindowUpdate()
        {
            base.WindowUpdate();
            if (MapPreviewActive)
            {
                MoveMapPreviewWindows();
            }
        }

        // Map Preview opens its preview and toolbar in the top right, on top of this window. Each one that overlaps is
        // moved once to the left of it, so dragging it back later is respected.
        private void MoveMapPreviewWindows()
        {
            WindowStack stack = Find.WindowStack;
            for (int i = 0; i < stack.Count; i++)
            {
                Window window = stack[i];
                string type = window.GetType().FullName;
                if (type != "MapPreview.MapPreviewWindow" && type != "MapPreview.MapPreviewToolbar")
                {
                    continue;
                }
                if (!movedMapPreviewWindows.Add(window) || !window.windowRect.Overlaps(windowRect))
                {
                    continue;
                }
                window.windowRect.x = Mathf.Max(0f, windowRect.x - window.windowRect.width - 10f);
            }
        }

        public override void PostClose()
        {
            base.PostClose();
            RimEmbarkMod.Instance.WriteSettings();
        }

        public override void DoWindowContents(Rect inRect)
        {
            TileCache cache = EmbarkSearch.Cache;
            float y = inRect.y;

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inRect.x, y, inRect.width - 30f, 35f), "RimEmbark_Title".Translate());
            y += 35f;
            Text.Font = GameFont.Small;

            Widgets.Label(new Rect(inRect.x, y, inRect.width, RowHeight), StatusText(cache));
            y += RowHeight + Gap;

            float buttonWidth = (inRect.width - 2 * Gap) / 3f;
            Rect randomRect = new Rect(inRect.x, y, buttonWidth, 30f);
            if (Widgets.ButtonText(randomRect, "RimEmbark_RandomMatch".Translate(), active: EmbarkSearch.HasResults && Filter.AnyActive && EmbarkSearch.Matches.Count > 0))
            {
                EmbarkSearch.JumpToRandomMatch();
            }
            TooltipHandler.TipRegion(randomRect, "RimEmbark_RandomMatchTip".Translate());
            if (Widgets.ButtonText(new Rect(randomRect.xMax + Gap, y, buttonWidth, 30f), "RimEmbark_Reset".Translate()))
            {
                Filter.Reset();
            }
            Rect overlayRect = new Rect(randomRect.xMax + buttonWidth + 2 * Gap, y, buttonWidth, 30f);
            bool overlay = Filter.showOverlay;
            Widgets.CheckboxLabeled(overlayRect.ContractedBy(4f, 3f), "RimEmbark_Overlay".Translate(), ref overlay);
            if (overlay != Filter.showOverlay)
            {
                Filter.showOverlay = overlay;
            }
            y += 30f + Gap;

            search = Widgets.TextField(new Rect(inRect.x, y, inRect.width, RowHeight), search);
            if (search.NullOrEmpty())
            {
                GUI.color = Color.gray;
                Widgets.Label(new Rect(inRect.x + 6f, y + 1f, inRect.width, RowHeight), "RimEmbark_Search".Translate());
                GUI.color = Color.white;
            }
            y += RowHeight + Gap * 2;

            Rect outRect = new Rect(inRect.x, y, inRect.width, inRect.yMax - y);
            if (cache == null || !cache.Done)
            {
                return;
            }
            Rect viewRect = new Rect(0f, 0f, outRect.width - 16f, Mathf.Max(viewHeight, outRect.height));
            Widgets.BeginScrollView(outRect, ref scrollPosition, viewRect);
            visibleTop = scrollPosition.y;
            visibleBottom = scrollPosition.y + outRect.height;
            float curY = 0f;
            DrawRanges(cache, viewRect.width, ref curY);
            foreach (OptionData data in cache.options)
            {
                DrawOptions(data, viewRect.width, ref curY);
            }
            viewHeight = curY;
            Widgets.EndScrollView();
        }

        private static string StatusText(TileCache cache)
        {
            if (cache == null)
            {
                return "";
            }
            if (!cache.Done)
            {
                return "RimEmbark_Analyzing".Translate(cache.Progress.ToStringPercent());
            }
            if (!Filter.AnyActive)
            {
                return "RimEmbark_NoFilters".Translate(cache.Count.ToStringCached());
            }
            if (!EmbarkSearch.HasResults)
            {
                return "RimEmbark_Searching".Translate();
            }
            return "RimEmbark_Matches".Translate(EmbarkSearch.Matches.Count.ToStringCached(), cache.Count.ToStringCached());
        }

        private bool DrawHeader(string key, string label, string summary, float width, ref float y, bool? matchAll = null, string criterion = null)
        {
            Rect rect = new Rect(0f, y, width, HeaderHeight);
            Widgets.DrawLightHighlight(rect);
            bool expanded = Expanded.Contains(key) || !search.NullOrEmpty();
            Rect iconRect = new Rect(rect.x + 4f, rect.y + (HeaderHeight - 18f) / 2f, 18f, 18f);
            GUI.DrawTexture(iconRect, expanded ? TexButton.Collapse : TexButton.Reveal);

            float right = rect.xMax;
            if (criterion != null && summary != null)
            {
                Rect clearRect = new Rect(right - ButtonSize, rect.y + 2f, ButtonSize, ButtonSize);
                if (Widgets.ButtonImage(clearRect, TexButton.CloseXSmall))
                {
                    Filter.ClearCriterion(criterion);
                }
                TooltipHandler.TipRegion(clearRect, "RimEmbark_ClearCategory".Translate());
                right = clearRect.x - Gap;
            }
            if (matchAll.HasValue)
            {
                string modeLabel = matchAll.Value ? "RimEmbark_MatchAll".Translate() : "RimEmbark_MatchAny".Translate();
                float modeWidth = Mathf.Max(60f, Text.CalcSize(modeLabel).x + 16f);
                Rect modeRect = new Rect(right - modeWidth, rect.y + 2f, modeWidth, ButtonSize);
                if (Widgets.ButtonText(modeRect, modeLabel))
                {
                    Filter.SetMatchAll(criterion, !matchAll.Value);
                }
                TooltipHandler.TipRegion(modeRect, "RimEmbark_MatchModeTip".Translate());
                right = modeRect.x - Gap;
            }

            Text.Anchor = TextAnchor.MiddleLeft;
            Rect labelRect = new Rect(iconRect.xMax + 6f, rect.y, right - iconRect.xMax - 6f, HeaderHeight);
            Widgets.Label(labelRect, summary == null ? label : label + "  " + summary.Colorize(ColoredText.SubtleGrayColor));
            Text.Anchor = TextAnchor.UpperLeft;
            if (Widgets.ButtonInvisible(labelRect))
            {
                if (!Expanded.Remove(key))
                {
                    Expanded.Add(key);
                }
                SoundDefOf.Click.PlayOneShotOnCamera();
            }
            y += HeaderHeight + Gap;
            return expanded;
        }

        private void DrawRanges(TileCache cache, float width, ref float y)
        {
            int active = 0;
            foreach (RangeData data in cache.ranges)
            {
                if (Filter.TryGetRange(data.criterion.key, out _))
                {
                    active++;
                }
            }
            string summary = active > 0 ? "RimEmbark_ActiveCount".Translate(active).ToString() : null;
            if (!DrawHeader("Ranges", "RimEmbark_Ranges".Translate(), summary, width, ref y))
            {
                return;
            }
            foreach (RangeData data in cache.ranges)
            {
                string label = data.criterion.labelKey.Translate();
                if (!MatchesSearch(label))
                {
                    continue;
                }
                bool enabled = Filter.TryGetRange(data.criterion.key, out FloatRange range);
                bool wasEnabled = enabled;
                Widgets.CheckboxLabeled(new Rect(6f, y, width - 6f, RowHeight), label, ref enabled);
                y += RowHeight;
                if (enabled != wasEnabled)
                {
                    if (enabled)
                    {
                        Filter.SetRange(data.criterion.key, new FloatRange(data.min, data.max));
                    }
                    else
                    {
                        Filter.DisableRange(data.criterion.key);
                    }
                }
                if (!enabled)
                {
                    continue;
                }
                FloatRange edited = range;
                Widgets.FloatRange(new Rect(6f, y, width - 6f, SliderHeight), data.criterion.key.GetHashCode(), ref edited,
                    data.min, data.max, data.criterion.valueLabelKey, data.criterion.style, roundTo: data.criterion.step);
                if (edited != range)
                {
                    Filter.SetRange(data.criterion.key, edited);
                }
                y += SliderHeight + Gap;
            }
            y += Gap;
        }

        private void DrawOptions(OptionData data, float width, ref float y)
        {
            if (data.keys.Count == 0)
            {
                return;
            }
            string criterion = data.criterion.key;
            int included = 0;
            int excluded = 0;
            bool anyVisible = false;
            foreach (int index in data.order)
            {
                FilterState state = Filter.GetState(data.filterKeys[index]);
                if (state == FilterState.Include)
                {
                    included++;
                }
                else if (state == FilterState.Exclude)
                {
                    excluded++;
                }
                anyVisible |= MatchesSearch(data.labels[index]);
            }
            if (!anyVisible)
            {
                return;
            }
            string summary = null;
            if (included > 0 || excluded > 0)
            {
                summary = "RimEmbark_CategorySummary".Translate(included, excluded);
            }
            bool matchAll = Filter.GetMatchAll(criterion, data.criterion.defaultMatchAll);
            if (!DrawHeader(criterion, data.criterion.labelKey.Translate(), summary, width, ref y, matchAll, criterion))
            {
                return;
            }
            foreach (int index in data.order)
            {
                if (MatchesSearch(data.labels[index]))
                {
                    DrawOption(data, index, width, ref y);
                }
            }
            y += Gap;
        }

        private void DrawOption(OptionData data, int index, float width, ref float y)
        {
            float top = y;
            y += RowHeight + 2f;
            if (top + RowHeight < visibleTop || top > visibleBottom)
            {
                return;
            }
            List<string> keys = data.filterKeys[index];
            FilterState state = Filter.GetState(keys);
            Rect rect = new Rect(6f, top, width - 6f, RowHeight);
            if (state == FilterState.Include)
            {
                Widgets.DrawBoxSolid(rect, IncludeColor);
            }
            else if (state == FilterState.Exclude)
            {
                Widgets.DrawBoxSolid(rect, ExcludeColor);
            }
            Widgets.DrawHighlightIfMouseover(rect);

            Rect minusRect = new Rect(rect.xMax - ButtonSize, rect.y, ButtonSize, ButtonSize);
            Rect plusRect = new Rect(minusRect.x - ButtonSize - Gap, rect.y, ButtonSize, ButtonSize);
            Rect labelRect = new Rect(rect.x + 4f, rect.y, plusRect.x - rect.x - 8f, RowHeight);

            Text.Anchor = TextAnchor.MiddleLeft;
            string label = data.labels[index];
            if (state == FilterState.Include)
            {
                label = "RimEmbark_IncludedOption".Translate(label);
            }
            else if (state == FilterState.Exclude)
            {
                label = "RimEmbark_ExcludedOption".Translate(label);
            }
            Widgets.Label(labelRect, label.Truncate(labelRect.width - 50f));
            Text.Anchor = TextAnchor.MiddleRight;
            GUI.color = Color.gray;
            Widgets.Label(labelRect, data.counts[index].ToStringCached());
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;

            string tooltip = data.tooltips[index];
            TooltipHandler.TipRegion(labelRect, tooltip.NullOrEmpty()
                ? "RimEmbark_OptionTip".Translate()
                : tooltip + "\n\n" + "RimEmbark_OptionTip".Translate());

            FilterState newState = state;
            if (Widgets.ButtonInvisible(labelRect))
            {
                newState = state == FilterState.Any ? FilterState.Include : state == FilterState.Include ? FilterState.Exclude : FilterState.Any;
            }
            if (Widgets.ButtonImage(plusRect, TexButton.Plus, state == FilterState.Include ? Color.green : Color.white))
            {
                newState = state == FilterState.Include ? FilterState.Any : FilterState.Include;
            }
            TooltipHandler.TipRegion(plusRect, "RimEmbark_IncludeTip".Translate());
            if (Widgets.ButtonImage(minusRect, TexButton.Minus, state == FilterState.Exclude ? Color.red : Color.white))
            {
                newState = state == FilterState.Exclude ? FilterState.Any : FilterState.Exclude;
            }
            TooltipHandler.TipRegion(minusRect, "RimEmbark_ExcludeTip".Translate());
            if (newState != state)
            {
                Filter.SetState(keys, newState);
            }
        }

        private bool MatchesSearch(string label)
        {
            return search.NullOrEmpty() || label.IndexOf(search, System.StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
