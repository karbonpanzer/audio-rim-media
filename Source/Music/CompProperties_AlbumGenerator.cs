using System.Collections.Generic;
using Verse;

namespace RimRadio
{
    public class CompProperties_AlbumGenerator : CompProperties
    {
        // Backwards-compatible prefixes (kept so existing XML need not change)
        public string nameRulepackPrefix = "MusicName_";
        public string descRulepackPrefix = "MusicDesc_";

        // Book-style RulePackDefs: if these are present, the comp will attempt to use Tale/Grammar
        // generation via reflection at runtime. These are optional and may be left null.
        public RulePackDef nameMaker;            // optional: RulePackDef to generate ArtName (ArtName purpose)
        public RulePackDef descriptionMaker;     // optional: RulePackDef to generate ArtDescription (ArtDescription purpose)

        // optional whitelist if you want product-specific allowed genres
        public List<string> allowedGenres;

        // optional overrides for root token names if you need nonstandard names
        public string rulesStringName;
        public string rulesStringDescription;

        public CompProperties_AlbumGenerator()
        {
            this.compClass = typeof(CompAlbumGenerator);
        }
    }
}
