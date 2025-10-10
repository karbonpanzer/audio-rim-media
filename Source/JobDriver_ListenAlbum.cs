using Verse;
using Verse.AI;
using RimWorld;

namespace VanillaMusicExpanded
{
    public class JobDriver_ListenAlbum : JobDriver
    {
        private Thing Album => this.job.targetA.Thing;
        private CompAlbum AlbumComp => Album?.TryGetComp<CompAlbum>();

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return this.pawn.Reserve(this.job.targetA, this.job, 1, -1, null, errorOnFailed);
        }

        protected override System.Collections.Generic.IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDestroyedNullOrForbidden(TargetIndex.A);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);

            int duration = AlbumComp?.Props?.listeningTicks ?? 1600;
            float joyPerTick = AlbumComp?.Props?.joyAmountPerTick ?? 0.0001f;

            var listen = new Toil();
            listen.initAction = () =>
            {
                this.pawn.rotationTracker.FaceTarget(this.job.targetA);
            };
            listen.tickAction = () =>
            {
                var needJoy = this.pawn.needs?.joy;
                if (needJoy != null)
                {
                    // Add a little joy each tick using our JoyKind (kept as VBE_Reading for compatibility)
                    needJoy.GainJoy(joyPerTick, VME_DefOf.VBE_Reading);
                }
            };
            listen.defaultCompleteMode = ToilCompleteMode.Delay;
            listen.defaultDuration = duration;
            listen.WithProgressBar(TargetIndex.A, () =>
                1f - (this.ticksLeftThisToil / (float)duration)
            );
            listen.socialMode = RandomSocialMode.Quiet;
            yield return listen;

            yield return new Toil
            {
                initAction = () =>
                {
                    var thought = AlbumComp?.Props?.thoughtOnFinish;
                    if (thought != null)
                    {
                        this.pawn.needs?.mood?.thoughts?.memories?.TryGainMemory(thought);
                    }
                },
                defaultCompleteMode = ToilCompleteMode.Instant
            };
        }
    }
}
