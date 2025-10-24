using HarmonyLib;
using Verse;

namespace RimRadio
{
    public class RR_Mod : Mod
    {
        public RR_Mod(ModContentPack content) : base(content)
        {
            new Harmony("kp.rimradio").PatchAll();
        }
    }
}
