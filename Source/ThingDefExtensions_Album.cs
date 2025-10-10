using Verse;
using RimWorld;

namespace VanillaMusicExpanded
{
    public static class ThingDefExtensions_Album
    {
        public static CompAlbum AlbumComp(this Thing t) => t?.TryGetComp<CompAlbum>();

        public static QualityCategory? TryGetQuality(this Thing t)
        {
            var cq = t?.TryGetComp<CompQuality>();
            if (cq == null) return null;
            return cq.Quality;
        }

        public static string TryGetGenre(this Thing t)
        {
            return t?.AlbumComp()?.Props?.genre ?? string.Empty;
        }
    }
}
