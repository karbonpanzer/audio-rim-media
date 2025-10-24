
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.Grammar;

namespace RimRadio
{
	[StaticConstructorOnStartup]
	public static class AlbumDescriptionByQuality_Boot
	{
		static AlbumDescriptionByQuality_Boot()
		{
			new Harmony("RR.AlbumDescriptionByQuality").PatchAll();
		}
	}

	internal static class RRAlbumDescUtil
	{
		public static bool IsAlbum(Thing t)
			=> t?.def?.defName != null && t.def.defName.StartsWith("RR_Album");

		public static string PackForQuality(Thing t)
		{
			var q = t.TryGetComp<CompQuality>()?.Quality ?? QualityCategory.Normal;
			switch (q)
			{
				case QualityCategory.Awful:
				case QualityCategory.Poor:
					return "RR_Album_Desc_Short";

				case QualityCategory.Normal:
				case QualityCategory.Good:
					return "RR_Album_Desc_Medium";

				case QualityCategory.Excellent:
				case QualityCategory.Masterwork:
				case QualityCategory.Legendary:
					return "RR_Album_Desc_Long";

				default:
					return "RR_Album_Desc_Medium";
			}
		}

		public static string QualityFlavorPack(Thing t)
		{
			var q = t.TryGetComp<CompQuality>()?.Quality ?? QualityCategory.Normal;
			return q switch
			{
				QualityCategory.Awful      => "RR_Album_Quality_Awful",
				QualityCategory.Poor       => "RR_Album_Quality_Poor",
				QualityCategory.Normal     => "RR_Album_Quality_Normal",
				QualityCategory.Good       => "RR_Album_Quality_Good",
				QualityCategory.Excellent  => "RR_Album_Quality_Excellent",
				QualityCategory.Masterwork => "RR_Album_Quality_Masterwork",
				QualityCategory.Legendary  => "RR_Album_Quality_Legendary",
				_                          => "RR_Album_Quality_Normal"
			};
		}

		public static string GenrePack(Thing t)
		{
			
			string def = t?.def?.defName;
			if (string.IsNullOrEmpty(def) || !def.StartsWith("RR_Album_"))
				return "RR_AlbumGenre_Generic";

			string key = def.Substring("RR_Album_".Length); 
			if (string.IsNullOrEmpty(key))
				return "RR_AlbumGenre_Generic";

			return "RR_AlbumGenre_" + key;
		}
	}

	
	[HarmonyPatch(typeof(CompArt), nameof(CompArt.GetDescriptionPart))]
	public static class Patch_CompArt_GetDescriptionPart_Albums_ByQuality
	{
		static bool Prefix(CompArt __instance, ref string __result)
		{
			var thing = __instance?.parent;
			if (thing == null || !RRAlbumDescUtil.IsAlbum(thing))
				return true; 

			var tierPackName = RRAlbumDescUtil.PackForQuality(thing);
			var qualPackName = RRAlbumDescUtil.QualityFlavorPack(thing);
			var genrePackName = RRAlbumDescUtil.GenrePack(thing);

			var tierPack = DefDatabase<RulePackDef>.GetNamedSilentFail(tierPackName);
			var qualPack = DefDatabase<RulePackDef>.GetNamedSilentFail(qualPackName);
			var genrePack = DefDatabase<RulePackDef>.GetNamedSilentFail(genrePackName);
			
			if (genrePack == null)
				genrePack = DefDatabase<RulePackDef>.GetNamedSilentFail("RR_AlbumGenre_Generic");

			if (tierPack == null || qualPack == null || genrePack == null)
				return true; 

			GrammarRequest req = default;
			req.Includes.Add(tierPack);
			req.Includes.Add(qualPack);
			req.Includes.Add(genrePack);

			__result = GrammarResolver.Resolve("r_art_description", req, forceLog: false);
			return false; 
		}
	}
}
