using Verse;

namespace RimEmbark
{
    public class RimEmbarkSettings : ModSettings
    {
        public EmbarkFilter filter = new EmbarkFilter();

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Deep.Look(ref filter, "filter");
            filter ??= new EmbarkFilter();
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
