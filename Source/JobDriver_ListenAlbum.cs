using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace VanillaMusicExpanded
{
    public class JobDriver_ListenAlbum : JobDriver
    {
        // A = album, B = seat (optional)
        private const TargetIndex AlbumInd = TargetIndex.A;
        private const TargetIndex SeatInd = TargetIndex.B;

        // Seat search radius (tiles) around the ALBUM, not the pawn.
        private const float SeatSearchRadius = 24f;

        private Thing AlbumThing => job.GetTarget(AlbumInd).Thing;
        private CompAlbum AlbumComp => AlbumThing?.TryGetComp<CompAlbum>();

        // Remember where the album came from so we can drop it back near that spot
        private IntVec3 originalCell = IntVec3.Invalid;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            if (AlbumThing == null) return false;

            // Snapshot origin before moving anything
            RememberOrigin(AlbumThing);

            // Reserve the album
            if (!pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed))
                return false;

            // Find the best seat NEAR THE ALBUM within limited radius; otherwise stand.
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

            // Go to the album
            yield return Toils_Goto.GotoThing(AlbumInd, PathEndMode.Touch);

            // Ensure a valid count for haul toils
            if (job.count < 1) job.count = 1;

            // Pick it up
            var startCarry = Toils_Haul.StartCarryThing(
                AlbumInd,
                putRemainderInQueue: false,
                subtractNumTakenFromJobCount: true,
                failIfStackCountLessThanJobCount: true
            );
            startCarry.FailOnDestroyedNullOrForbidden(AlbumInd);
            yield return startCarry;

            // If we have a seat (within radius of the album), go there and adopt seated posture
            if (job.targetB.IsValid)
            {
                yield return Toils_Goto.GotoThing(SeatInd, PathEndMode.OnCell);

                // WaitWith on a sittable building cell makes the pawn sit while waiting
                var sitWait = Toils_General.WaitWith(SeatInd, 1, true);
                sitWait.handlingFacing = true;
                yield return sitWait; // one tick to adopt seated posture
            }

            // Tunables
            var props = AlbumComp?.Props;
            int duration = props?.listeningTicks ?? 1600;
            float joyPerTick = props?.joyAmountPerTick ?? 0.0002f;
            JoyKindDef joyKind = VME_DefOf.RR_Listening ?? JoyKindDefOf.Meditative;
            ThoughtDef finishThought = props?.thoughtOnFinish;

            // Listen while holding the album
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
                // To require staying seated, uncomment:
                // listen.FailOn(() => pawn.Position != job.targetB.Cell);
            }
            listen.WithProgressBar(AlbumInd, () => 1f - (ticksLeftThisToil / (float)duration));
            yield return listen;

            // Mood buff pops immediately after listening
            yield return new Toil
            {
                initAction = () =>
                {
                    if (finishThought != null)
                        pawn.needs?.mood?.thoughts?.memories?.TryGainMemory(finishThought);
                },
                defaultCompleteMode = ToilCompleteMode.Instant
            };

            // Return the album near the original cell
            foreach (var toil in Toils_ReturnAlbumNearOrigin())
                yield return toil;
        }

        // Finds the best reservable & reachable sittable within 'radius' of 'center' (album position).
        // Scores by Comfort (dominant) with a mild distance penalty to break ties.
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

                // Must be within radius of the ALBUM location
                if (b.Position.DistanceToSquared(center) > radiusSq) continue;

                if (!p.CanReserve(b)) continue;
                if (!p.CanReach(b, PathEndMode.OnCell, Danger.Some)) continue;

                float comfort = b.GetStatValue(StatDefOf.Comfort, true); // ~0..1+
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
            // Go back near the original cell if valid
            if (originalCell.IsValid && pawn.Position != originalCell && pawn.Map?.reachability != null)
            {
                yield return Toils_Goto.GotoCell(originalCell, PathEndMode.Touch);
            }

            // Drop at or near the original cell
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

            // If it was on the map, use its position
            if (thing.Spawned)
                originalCell = thing.Position;

            // If it was in a holder (like a shelf), prefer that building's position
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
