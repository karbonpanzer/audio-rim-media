using Verse;
using RimWorld;

namespace VanillaMusicExpanded
{
    [DefOf]
    public static class VME_DefOf
    {
        public static JobDef VME_ListenAlbum;
        public static JoyKindDef RR_Listening; // kept for compatibility in XML for now
        public static SoundDef RR_Album_StartListening;
        public static SoundDef RR_Album_StopListening;

        public static ThoughtDef RR_ListenedToAlbum_Pleased;
        public static ThoughtDef RR_ListenedToAlbum_Thrilled;
        public static ThoughtDef RR_ListenedToAlbum_Disliked;

        static VME_DefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(VME_DefOf));
        }
    }
}
