using RimWorld;
using Verse;
using Verse.AI;

namespace RimRadio
{
    public class JoyGiver_ListenAlbum : JoyGiver
    {
        public override Job TryGiveJob(Pawn pawn)
        {
            if (pawn.Map == null || pawn.health?.Downed == true) return null;
            if (pawn.needs?.joy == null) return null;

            var albumDef = DefDatabase<ThingDef>.GetNamedSilentFail("RR_Album");
            if (albumDef == null) return null;

            Thing album = GenClosest.ClosestThingReachable(
                pawn.Position,
                pawn.Map,
                ThingRequest.ForDef(albumDef),
                PathEndMode.Touch,
                TraverseParms.For(pawn),
                maxDistance: 9999f,
                validator: t => !t.IsForbidden(pawn) && pawn.CanReserve(t));

            if (album == null) return null;

            var jobDef = DefDatabase<JobDef>.GetNamedSilentFail("RR_ListenAlbum");
            if (jobDef == null) return null;

            var job = JobMaker.MakeJob(jobDef, album);
            job.count = 1;
            return job;
        }
    }
}
