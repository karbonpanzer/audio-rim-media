using Verse;

namespace VanillaMusicExpanded
{
    public class SubEffecter_AlbumSymbol : SubEffecter
    {
        public SubEffecter_AlbumSymbol(SubEffecterDef def, Effecter parent) : base(def, parent) { }

        public override void SubEffectTick(TargetInfo A, TargetInfo B)
        {
            // No-op placeholder. Hook up mote spawning here if desired.
        }
    }
}
