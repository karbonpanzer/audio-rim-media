using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;
using UnityEngine;

namespace RimRadio
{
    public class JobDriver_PlayAlbum : JobDriver
    {
        // Assumed job targets:
        // TargetA = album Thing (ThingWithComps)
        // TargetB = speaker/building (ThingWithComps that has CompAlbumEffectTracker)
        private const int DEFAULT_PLAY_DURATION_TICKS = 2500; // 1 minute default; adjust as needed
        private const float LISTEN_RADIUS = 12f; // in tiles; change to taste

        // quality mapping arrays
        private static readonly int[] minutesByQuality = new int[] { 7, 15, 30, 60, 120, 240, 480 };
        private static readonly float[] negativeChanceByQuality = new float[] { 0.30f, 0.15f, 0.06f, 0.02f, 0.01f, 0.002f, 0f };

        protected override IEnumerable<Toil> MakeNewToils()
        {
            // Reserve album and speaker
            // you could add reservation logic if needed

            var playToil = new Toil();
            playToil.initAction = () =>
            {
                // optional: start playing animation/sound
            };
            playToil.defaultCompleteMode = ToilCompleteMode.Delay;
            playToil.defaultDuration = DEFAULT_PLAY_DURATION_TICKS; // how long the playback action runs for the driver
            playToil.AddFinishAction(() =>
            {
                TryApplyAlbumEffects();
            });

            yield return playToil;
        }

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            // Reserve the album by the actor
            if (pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed) &&
                pawn.Reserve(job.targetB, job, 1, -1, null, errorOnFailed))
                return true;
            return false;
        }

        private void TryApplyAlbumEffects()
        {
            var album = job.GetTarget(TargetIndex.A).Thing;
            var speaker = job.GetTarget(TargetIndex.B).Thing;
            if (album == null || speaker == null) return;

            // Determine hediff to apply from mod extension or fallback
            HediffDef hediffDef = GetHediffForAlbum(album);
            if (hediffDef == null)
            {
                // fallback to a rock hediff so something happens; replace as you prefer
                hediffDef = HediffDef.Named("RR_Rock_Pumped");
            }

            // get quality
            QualityCategory qc = CompQualityHelper.GetQualityFromThing(album);
            int qIndex = Mathf.Clamp((int)qc, 0, 6);
            int minutes = minutesByQuality[qIndex];
            int ticks = minutes * 2500;
            float negChance = negativeChanceByQuality[qIndex];

            // find listeners: simple approach - all player colonists within radius
            var map = pawn.Map;
            if (map == null) return;

            List<Pawn> listeners = new List<Pawn>();
            foreach (var p in map.mapPawns.AllPawnsSpawned)
            {
                if (p.Faction == Faction.OfPlayer && !p.Downed && !p.IsPrisoner)
                {
                    if (p.Position.InHorDistOf(speaker.Position, LISTEN_RADIUS))
                    {
                        listeners.Add(p);
                    }
                }
            }

            // get tracker comp on speaker
            var tracker = speaker.TryGetComp<CompAlbumEffectTracker>();
            if (tracker == null)
            {
                Log.Warning("[Rim-Radio] Speaker has no CompAlbumEffectTracker; attempting to still apply hediffs but they won't auto-expire.");
            }

            // For each listener, roll for negative outcome or apply positive hediff
            foreach (var listener in listeners)
            {
                if (Rand.Value < negChance)
                {
                    // apply bad mastering hediff
                    HediffDef bad = HediffDef.Named("RR_Bad_Mastering");
                    if (tracker != null)
                        tracker.ApplyHediffToPawn(listener, bad, ticks / 2, Mathf.Clamp01( (1f - (qIndex / 6f)) ));
                    else
                    {
                        var h = HediffMaker.MakeHediff(bad, listener);
                        h.Severity = Mathf.Clamp01((1f - (qIndex / 6f)));
                        listener.health.AddHediff(h);
                    }
                }
                else
                {
                    // apply positive hediff based on quality severity
                    if (tracker != null)
                        tracker.ApplyHediffToPawn(listener, hediffDef, ticks, Mathf.Max(0.05f, qIndex / 6f));
                    else
                    {
                        var h = HediffMaker.MakeHediff(hediffDef, listener);
                        h.Severity = Mathf.Max(0.05f, qIndex / 6f);
                        listener.health.AddHediff(h);
                        // no scheduling without tracker -> may be permanent
                    }
                }
            }

            // Optional: consume the album, or reduce charges, or trigger thoughts/events, broadcast notifications etc.
            // If album should be single-use uncomment:
            // album.Destroy(DestroyMode.Vanish);
        }

        private HediffDef GetHediffForAlbum(Thing album)
        {
            if (album == null) return null;
            var ext = album.def.GetModExtension<AlbumMetaExtension>();
            if (ext != null)
            {
                var hd = ext.GetHediffDef();
                if (hd != null) return hd;
            }

            // Fallback: attempt to infer by defName tokens (case-insensitive)
            var name = album.def.defName?.ToLower() ?? "";
            if (name.Contains("rock")) return HediffDef.Named("RR_Rock_Pumped");
            if (name.Contains("metal") || name.Contains("hardcore")) return HediffDef.Named("RR_Metal_Hardened");
            if (name.Contains("jazz")) return HediffDef.Named("RR_Jazz_Recovered");
            if (name.Contains("ambient") || name.Contains("classical")) return HediffDef.Named("RR_Ambient_Focus");
            if (name.Contains("elect") || name.Contains("edm") || name.Contains("electronic")) return HediffDef.Named("RR_Electronic_Hustle");
            if (name.Contains("folk")) return HediffDef.Named("RR_Folk_Bonding");
            if (name.Contains("pop")) return HediffDef.Named("RR_Pop_Buzz");
            if (name.Contains("exp") || name.Contains("experimental")) return HediffDef.Named("RR_Experimental_Chaos");
            if (name.Contains("hip") || name.Contains("hop") || name.Contains("rap")) return HediffDef.Named("RR_HipHop_Hustle");
            if (name.Contains("chant") || name.Contains("relig") || name.Contains("reverent")) return HediffDef.Named("RR_Chant_Reverent");

            // default
            return HediffDef.Named("RR_Rock_Pumped");
        }
    }
}
