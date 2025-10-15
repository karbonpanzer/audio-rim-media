using HarmonyLib;
using Verse;

namespace VanillaMusicExpanded
{
    public class VME_Mod : Mod
    {
        public VME_Mod(ModContentPack content) : base(content)
        {
            new Harmony("kp.rimradio").PatchAll();
        }
    }
}
