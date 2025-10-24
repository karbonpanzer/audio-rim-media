using Verse;
using RimWorld;

namespace RimRadio
{
    [DefOf]
    public static class RR_DefOf
    {
        public static JobDef RR_ListenAlbum;
        public static JoyKindDef RR_Listening; 
        public static SoundDef RR_Album_StartListening;
        public static SoundDef RR_Album_StopListening;

        public static ThoughtDef RR_ListenedToAlbum_Pleased;
        public static ThoughtDef RR_ListenedToAlbum_Thrilled;
        public static ThoughtDef RR_ListenedToAlbum_Disliked;

        static RR_DefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(RR_DefOf));
        }
    }
}
