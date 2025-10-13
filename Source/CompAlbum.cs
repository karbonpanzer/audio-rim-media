// Source/CompAlbum.cs
using Verse;
using RimWorld;

namespace VanillaMusicExpanded
{
	public class CompProperties_Album : CompProperties
	{
		public string genre;
		public int listeningTicks = 1600;
		public float joyAmountPerTick = 0.0001f;
		public ThoughtDef thoughtOnFinish;

		public CompProperties_Album()
		{
			compClass = typeof(CompAlbum);
		}
	}

	public class CompAlbum : ThingComp
	{
		public CompProperties_Album Props => (CompProperties_Album)props;

		public override string CompInspectStringExtra()
		{
			// Genre now shown via CompArt patch together with Author and Quality
			return null;
		}
	}
}
