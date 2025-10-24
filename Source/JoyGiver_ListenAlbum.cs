using Verse;
using RimWorld;
using Verse.AI;

namespace RimRadio
{
    public class JoyGiver_ListenAlbum : JoyGiver
    {
        
        public override Job TryGiveJob(Pawn pawn)
        {
            if (pawn.Map == null) return null;

            Thing album = GenClosest.ClosestThingReachable(
                pawn.Position, pawn.Map,
                ThingRequest.ForGroup(ThingRequestGroup.HaulableEver),
                PathEndMode.Touch,
                TraverseParms.For(pawn),
                50f, 
                t =>
                    t.TryGetComp<CompAlbum>() != null
                    && !t.IsForbidden(pawn)
                    && pawn.CanReserveAndReach(t, PathEndMode.Touch, Danger.Some, 1, -1, null, false)
            );

            if (album == null) return null;

            
            var job = JobMaker.MakeJob(RR_DefOf.RR_ListenAlbum, album);
            return job;
        }

    }
}