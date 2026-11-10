using Verse;

namespace RimEmbark
{
    public class RimEmbarkSettings : ModSettings
    {
        // The starting site filter keeps its original node name, so filters from older versions still load.
        public EmbarkFilter filter = new EmbarkFilter();
        public EmbarkFilter gravshipFilter = new EmbarkFilter();
        public EmbarkFilter caravanFilter = new EmbarkFilter();

        // Toggled from the world map's visibility icons; hides the whole filter UI.
        public bool showFilters = true;

        public EmbarkFilter FilterFor(EmbarkContext context)
        {
            switch (context)
            {
                case EmbarkContext.Gravship:
                    return gravshipFilter;
                case EmbarkContext.Caravan:
                    return caravanFilter;
                default:
                    return filter;
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref showFilters, "showFilters", true);
            Scribe_Deep.Look(ref filter, "filter");
            Scribe_Deep.Look(ref gravshipFilter, "gravshipFilter");
            Scribe_Deep.Look(ref caravanFilter, "caravanFilter");
            filter ??= new EmbarkFilter();
            gravshipFilter ??= new EmbarkFilter();
            caravanFilter ??= new EmbarkFilter();
        }
    }

    public class RimEmbarkMod : Mod
    {
        public static RimEmbarkMod Instance;

        public static RimEmbarkSettings Settings;

        public RimEmbarkMod(ModContentPack content) : base(content)
        {
            Instance = this;
            Settings = GetSettings<RimEmbarkSettings>();
        }
    }
}
