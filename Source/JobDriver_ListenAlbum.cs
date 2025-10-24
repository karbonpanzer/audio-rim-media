using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace RimRadio
{
    public class JobDriver_ListenAlbum : JobDriver
    {
        
        private const TargetIndex AlbumInd = TargetIndex.A;
        private const TargetIndex SeatInd = TargetIndex.B;

        
        private const float SeatSearchRadius = 24f;

        private Thing AlbumThing => job.GetTarget(AlbumInd).Thing;
        private CompAlbum AlbumComp => AlbumThing?.TryGetComp<CompAlbum>();

        
        private IntVec3 originalCell = IntVec3.Invalid;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            if (AlbumThing == null) return false;

            
            RememberOrigin(AlbumThing);

            
            if (!pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed))
                return false;

            
            Thing seat = TryFindBestSeatNearAlbum(pawn, AlbumThing.Position, SeatSearchRadius);
            if (seat != null)
            {
                job.SetTarget(SeatInd, seat);
                pawn.Reserve(job.targetB, job, 1, -1, null, false);
            }
            else
            {
                job.SetTarget(SeatInd, LocalTargetInfo.Invalid);
            }

            return true;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDestroyedNullOrForbidden(AlbumInd);
            this.FailOn(() => !pawn.CanReserve(TargetA, 1, -1, null, false));
            this.FailOnSomeonePhysicallyInteracting(AlbumInd);

            if (job.targetB.IsValid)
            {
                this.FailOnDestroyedNullOrForbidden(SeatInd);
                this.FailOn(() => job.targetB.Thing is Building b && b.IsForbidden(pawn));
            }

            
            yield return Toils_Goto.GotoThing(AlbumInd, PathEndMode.Touch);

            
            if (job.count < 1) job.count = 1;

            
            var startCarry = Toils_Haul.StartCarryThing(
                AlbumInd,
                putRemainderInQueue: false,
                subtractNumTakenFromJobCount: true,
                failIfStackCountLessThanJobCount: true
            );
            startCarry.FailOnDestroyedNullOrForbidden(AlbumInd);
            yield return startCarry;

            
            if (job.targetB.IsValid)
            {
                yield return Toils_Goto.GotoThing(SeatInd, PathEndMode.OnCell);

                
                var sitWait = Toils_General.WaitWith(SeatInd, 1, true);
                sitWait.handlingFacing = true;
                yield return sitWait; 
            }

            
            var props = AlbumComp?.Props;
            int duration = props?.listeningTicks ?? 1600;
            float joyPerTick = props?.joyAmountPerTick ?? 0.0002f;
            JoyKindDef joyKind = RR_DefOf.RR_Listening ?? JoyKindDefOf.Meditative;
            ThoughtDef finishThought = props?.thoughtOnFinish;

            
            var listen = new Toil
            {
                initAction = () =>
                {
                    pawn.rotationTracker.FaceTarget(job.targetB.IsValid ? job.targetB : job.targetA);
                },
                tickAction = () =>
                {
                    var joy = pawn.needs?.joy;
                    if (joy != null)
                        joy.GainJoy(joyPerTick, joyKind);
                },
                defaultCompleteMode = ToilCompleteMode.Delay,
                defaultDuration = duration,
                socialMode = RandomSocialMode.Quiet
            };

            listen.FailOnDestroyedNullOrForbidden(AlbumInd);
            listen.FailOnSomeonePhysicallyInteracting(AlbumInd);
            if (job.targetB.IsValid)
            {
                listen.FailOnDestroyedNullOrForbidden(SeatInd);
                
                
            }
            listen.WithProgressBar(AlbumInd, () => 1f - (ticksLeftThisToil / (float)duration));
            yield return listen;

            
            yield return new Toil
            {
                initAction = () =>
                {
                    if (finishThought != null)
                        pawn.needs?.mood?.thoughts?.memories?.TryGainMemory(finishThought);
                },
                defaultCompleteMode = ToilCompleteMode.Instant
            };

            
            foreach (var toil in Toils_ReturnAlbumNearOrigin())
                yield return toil;
        }

        
        
        private Thing TryFindBestSeatNearAlbum(Pawn p, IntVec3 center, float radius)
        {
            Map map = p.Map;
            if (map == null) return null;

            Thing best = null;
            float bestScore = float.MinValue;

            var buildings = map.listerBuildings.allBuildingsColonist;
            float radiusSq = radius * radius;

            for (int i = 0; i < buildings.Count; i++)
            {
                var b = buildings[i];
                if (b == null || b.Destroyed) continue;
                if (b.IsForbidden(p)) continue;

                var bd = b.def?.building;
                if (bd == null || !bd.isSittable) continue;

                
                if (b.Position.DistanceToSquared(center) > radiusSq) continue;

                if (!p.CanReserve(b)) continue;
                if (!p.CanReach(b, PathEndMode.OnCell, Danger.Some)) continue;

                float comfort = b.GetStatValue(StatDefOf.Comfort, true); 
                float dist = center.DistanceTo(b.Position);
                float score = (comfort * 100f) - (dist * 0.75f);

                if (score > bestScore)
                {
                    bestScore = score;
                    best = b;
                }
            }

            return best;
        }

        private IEnumerable<Toil> Toils_ReturnAlbumNearOrigin()
        {
            
            if (originalCell.IsValid && pawn.Position != originalCell && pawn.Map?.reachability != null)
            {
                yield return Toils_Goto.GotoCell(originalCell, PathEndMode.Touch);
            }

            
            yield return new Toil
            {
                initAction = () =>
                {
                    if (pawn.carryTracker?.CarriedThing == null) return;

                    Thing carried = pawn.carryTracker.CarriedThing;
                    GenPlace.TryPlaceThing(carried, originalCell.IsValid ? originalCell : pawn.Position, pawn.Map, ThingPlaceMode.Near);
                },
                defaultCompleteMode = ToilCompleteMode.Instant
            };
        }

        private void RememberOrigin(Thing thing)
        {
            originalCell = IntVec3.Invalid;
            if (thing == null) return;

            
            if (thing.Spawned)
                originalCell = thing.Position;

            
            IThingHolder holder = thing.ParentHolder;
            if (holder != null)
            {
                Thing holderThing = (holder as Thing) ?? (holder as ThingOwner)?.Owner as Thing;
                if (holderThing is Building b && b.Spawned)
                    originalCell = b.Position;
            }

            if (!originalCell.IsValid)
                originalCell = thing.Spawned ? thing.Position : pawn.Position;
        }
    }
}
