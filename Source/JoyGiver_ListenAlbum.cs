using Verse;
using RimWorld;
using Verse.AI;

namespace VanillaMusicExpanded
{
    public class JoyGiver_ListenAlbum : JoyGiver
    {
        public override Job TryGiveJob(Pawn pawn)
        {
            if (pawn.skills == null) return null;

            Thing album = GenClosest.ClosestThingReachable(
                pawn.Position,
                pawn.Map,
                ThingRequest.ForGroup(ThingRequestGroup.HaulableEver),
                PathEndMode.ClosestTouch,
                TraverseParms.For(pawn),
                30f,
                t => t.TryGetComp<CompAlbum>() != null
            );

            if (album == null) return null;

            var job = JobMaker.MakeJob(DefDatabase<JobDef>.GetNamedSilentFail("VME_ListenAlbum"), album);
            if (job == null) return null;

            job.ignoreJoyTimeAssignment = false;
            return job;
        }
    }
}