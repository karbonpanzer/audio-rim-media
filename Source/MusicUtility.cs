using Verse;
using RimWorld;

namespace VanillaMusicExpanded
{
    public static class MusicUtility
    {
        public static ThoughtDef ThoughtForQuality(QualityCategory qc)
        {
            // Map to the generic listening thoughts we defined in XML
            switch (qc)
            {
                case QualityCategory.Awful:
                case QualityCategory.Poor:
                    return DefDatabase<ThoughtDef>.GetNamedSilentFail("RR_ListenedToAlbum_Disliked");
                case QualityCategory.Normal:
                case QualityCategory.Good:
                    return DefDatabase<ThoughtDef>.GetNamedSilentFail("RR_ListenedToAlbum_Pleased");
                case QualityCategory.Excellent:
                case QualityCategory.Masterwork:
                case QualityCategory.Legendary:
                    return DefDatabase<ThoughtDef>.GetNamedSilentFail("RR_ListenedToAlbum_Thrilled");
                default:
                    return DefDatabase<ThoughtDef>.GetNamedSilentFail("RR_ListenedToAlbum_Pleased");
            }
        }
    }
}
