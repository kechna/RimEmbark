using System.Collections.Generic;
using Verse;

namespace RimEmbark
{
    public enum FilterState : byte
    {
        Any,
        Include,
        Exclude,
    }

    public class EmbarkFilter : IExposable
    {
        // Keyed by "<criterion>/<option>", e.g. "Biome/TemperateForest". Any is never stored.
        private Dictionary<string, FilterState> options = new Dictionary<string, FilterState>();

        // Only enabled ranges are stored.
        private Dictionary<string, FloatRange> ranges = new Dictionary<string, FloatRange>();

        private Dictionary<string, bool> matchAll = new Dictionary<string, bool>();

        public bool showOverlay = true;

        public bool excludeFactionTerritories = true;

        public bool limitToGravshipRange = true;

        public int Version { get; private set; }

        public bool AnyActive => options.Count > 0 || ranges.Count > 0 || (excludeFactionTerritories && FactionTerritoriesCompat.Active)
            || (limitToGravshipRange && EmbarkSearch.GravshipRangeAvailable);

        public static string OptionKey(string criterion, string option)
        {
            return criterion + "/" + option;
        }

        public FilterState GetState(string optionKey)
        {
            return options.TryGetValue(optionKey, out FilterState state) ? state : FilterState.Any;
        }

        public FilterState GetState(List<string> optionKeys)
        {
            foreach (string key in optionKeys)
            {
                FilterState state = GetState(key);
                if (state != FilterState.Any)
                {
                    return state;
                }
            }
            return FilterState.Any;
        }

        public void SetState(List<string> keys, FilterState state)
        {
            foreach (string key in keys)
            {
                SetState(key, state);
            }
        }

        public void SetState(string key, FilterState state)
        {
            if (state == FilterState.Any)
            {
                options.Remove(key);
            }
            else
            {
                options[key] = state;
            }
            Version++;
        }

        public void ClearCriterion(string criterion)
        {
            string prefix = criterion + "/";
            List<string> keys = new List<string>();
            foreach (string key in options.Keys)
            {
                if (key.StartsWith(prefix))
                {
                    keys.Add(key);
                }
            }
            foreach (string key in keys)
            {
                options.Remove(key);
            }
            Version++;
        }

        public bool TryGetRange(string criterion, out FloatRange range)
        {
            return ranges.TryGetValue(criterion, out range);
        }

        public void SetRange(string criterion, FloatRange range)
        {
            ranges[criterion] = range;
            Version++;
        }

        public void DisableRange(string criterion)
        {
            if (ranges.Remove(criterion))
            {
                Version++;
            }
        }

        public bool GetMatchAll(string criterion, bool defaultValue)
        {
            return matchAll.TryGetValue(criterion, out bool value) ? value : defaultValue;
        }

        public void SetMatchAll(string criterion, bool value)
        {
            matchAll[criterion] = value;
            Version++;
        }

        public void Reset()
        {
            options.Clear();
            ranges.Clear();
            matchAll.Clear();
            Version++;
        }

        public void SetExcludeFactionTerritories(bool value)
        {
            excludeFactionTerritories = value;
            Version++;
        }

        public void SetLimitToGravshipRange(bool value)
        {
            limitToGravshipRange = value;
            Version++;
        }

        public void MarkChanged()
        {
            Version++;
        }

        public void ExposeData()
        {
            Scribe_Collections.Look(ref options, "options", LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref ranges, "ranges", LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref matchAll, "matchAll", LookMode.Value, LookMode.Value);
            Scribe_Values.Look(ref showOverlay, "showOverlay", true);
            Scribe_Values.Look(ref excludeFactionTerritories, "excludeFactionTerritories", true);
            Scribe_Values.Look(ref limitToGravshipRange, "limitToGravshipRange", true);
            // Missing nodes (older or empty settings files) load as null.
            options ??= new Dictionary<string, FilterState>();
            ranges ??= new Dictionary<string, FloatRange>();
            matchAll ??= new Dictionary<string, bool>();
        }
    }
}
