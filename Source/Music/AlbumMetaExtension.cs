using Verse;

namespace RimRadio
{
    // A small ModExtension you can add to album ThingDef to point at the HediffDef to apply.
    // Example in ThingDef:
    // <modExtensions><li Class="RimRadio.AlbumMetaExtension"><hediffDefName>RR_Rock_Pumped</hediffDefName></li></modExtensions>
    public class AlbumMetaExtension : DefModExtension
    {
        public string hediffDefName;

        public HediffDef GetHediffDef()
        {
            if (string.IsNullOrEmpty(hediffDefName)) return null;
            return DefDatabase<HediffDef>.GetNamedSilentFail(hediffDefName);
        }
    }
}
