using Verse;
using Verse.Grammar;

namespace VanillaMusicExpanded
{
    public static class AlbumRulePackUtility
    {
        public static string MakeAlbumName()
        {
            var req = new GrammarRequest();
            // The existing rulepacks use this defName pair
            req.Includes.Add(DefDatabase<RulePackDef>.GetNamedSilentFail("ArtDescription_BookArtName"));
            return GrammarResolver.Resolve("r_art_name", req, null, false);
        }

        public static string MakeAlbumDescription()
        {
            var req = new GrammarRequest();
            req.Includes.Add(DefDatabase<RulePackDef>.GetNamedSilentFail("ArtDescription_BookArtDesc"));
            return GrammarResolver.Resolve("r_art_desc", req, null, false);
        }
    }
}
