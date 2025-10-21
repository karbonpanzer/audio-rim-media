using Verse;
using RimWorld;
using Verse.AI;

namespace VanillaMusicExpanded
{
    public class JoyGiver_ListenAlbum : JoyGiver
    {
        // JoyGiver_ListenAlbum.cs  (replace TryGiveJob)
        public override Job TryGiveJob(Pawn pawn)
        {
            if (pawn.Map == null) return null;

            Thing album = GenClosest.ClosestThingReachable(
                pawn.Position, pawn.Map,
                ThingRequest.ForGroup(ThingRequestGroup.HaulableEver),
                PathEndMode.Touch,
                TraverseParms.For(pawn),
                50f, // give it a bit more range
                t =>
                    t.TryGetComp<CompAlbum>() != null
                    && !t.IsForbidden(pawn)
                    && pawn.CanReserveAndReach(t, PathEndMode.Touch, Danger.Some, 1, -1, null, false)
            );

            if (album == null) return null;

            // Prefer DefOf over string lookup
            var job = JobMaker.MakeJob(VME_DefOf.VME_ListenAlbum, album);
            return job;
        }

    }
}