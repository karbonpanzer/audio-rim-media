using HarmonyLib;
using RimWorld;
using Verse;

namespace VanillaMusicExpanded
{
    [StaticConstructorOnStartup]
    public static class AlbumLabelFromArtTitle_Boot
    {
        static AlbumLabelFromArtTitle_Boot()
        {
            new Harmony("VME.AlbumLabelFromArtTitle").PatchAll();
        }
    }

    internal static class RRAlbumUtil
    {
        public static bool IsAlbum(Thing thing)
            => thing?.def?.defName != null && thing.def.defName.StartsWith("RR_Album");
    }

    // Title overwrites label (no count), include Quality in parentheses like books
    [HarmonyPatch(typeof(Thing), nameof(Thing.LabelNoCount), MethodType.Getter)]
    public static class Patch_LabelNoCount
    {
        static void Postfix(Thing __instance, ref string __result)
        {
            if (!RRAlbumUtil.IsAlbum(__instance)) return;

            var compArt = __instance.TryGetComp<CompArt>();
            if (compArt == null) return;

            var title = compArt.Title;
            if (string.IsNullOrEmpty(title)) return;

            // Append quality in parentheses if present
            var compQual = __instance.TryGetComp<CompQuality>();
            if (compQual != null)
            {
                var qLabel = QualityUtility.GetLabel(compQual.Quality);
                if (!string.IsNullOrEmpty(qLabel))
                    title = $"{title} ({qLabel})";
            }

            __result = title;
        }
    }

    // Title overwrites label (capitalized), include Quality in parentheses like books
    [HarmonyPatch(typeof(Thing), nameof(Thing.LabelCap), MethodType.Getter)]
    public static class Patch_LabelCap
    {
        static void Postfix(Thing __instance, ref string __result)
        {
            if (!RRAlbumUtil.IsAlbum(__instance)) return;

            var compArt = __instance.TryGetComp<CompArt>();
            if (compArt == null) return;

            var title = compArt.Title;
            if (string.IsNullOrEmpty(title)) return;

            var compQual = __instance.TryGetComp<CompQuality>();
            if (compQual != null)
            {
                var qLabel = QualityUtility.GetLabel(compQual.Quality);
                if (!string.IsNullOrEmpty(qLabel))
                    title = $"{title} ({qLabel})";
            }

            __result = title.CapitalizeFirst();
        }
    }

    // Inspect: Author on one line, Genre on the next. No title in Inspect.
    [HarmonyPatch(typeof(CompArt), nameof(CompArt.CompInspectStringExtra))]
    public static class Patch_CompArt_Inspect_AuthorThenGenre
    {
        static void Postfix(CompArt __instance, ref string __result)
        {
            if (__instance?.parent == null) return;
            if (!RRAlbumUtil.IsAlbum(__instance.parent)) return;

            var author = __instance.AuthorName;

            var compAlbum = __instance.parent.TryGetComp<CompAlbum>();
            var genre = compAlbum?.Props?.genre;

            if (string.IsNullOrEmpty(author) && string.IsNullOrEmpty(genre))
            {
                __result = null;
                return;
            }

            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            if (!string.IsNullOrEmpty(author)) sb.Append("Author: ").Append(author);
            if (!string.IsNullOrEmpty(genre))
            {
                if (sb.Length > 0) sb.Append('\n');
                sb.Append("Genre: ").Append(genre);
            }

            __result = sb.ToString();
        }
    }
}
