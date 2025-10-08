using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace RimRadio
{
    public class JobDriver_ListenAlbum : JobDriver
    {
        private const int ListenTicks = 2500; // ~41.6s at 60 tps

        public Thing Album => job.targetA.Thing;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(Album, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            this.FailOn(() => Album == null);

            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);

            var listen = Toils_General.Wait(ListenTicks);
            listen.WithProgressBar(TargetIndex.A, () =>
                (float)listen.actor.jobs.curDriver.ticksLeftThisToil / ListenTicks);
            listen.socialMode = RandomSocialMode.Off;
            listen.handlingFacing = true;

            // Put the finish action on the TOIL (expects Action with 0 args)
            listen.AddFinishAction(() =>
            {
                if (pawn?.needs?.joy != null)
                {
                    // Flat, tiny recreation bump; we can scale by quality later
                    pawn.needs.joy.GainJoy(0.04f, JoyKindDefOf.Meditative);
                }
            });

            yield return listen;
        }
    }
}
