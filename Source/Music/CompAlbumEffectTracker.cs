using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using UnityEngine;

namespace RimRadio
{
    // ThingComp that tracks applied hediffs from album playback and removes them after expiry.
    // It persists its record list across saves.
    public class CompAlbumEffectTracker : ThingComp
    {
        private List<AppliedHediffRecord> applied = new List<AppliedHediffRecord>();

        public override void CompTick()
        {
            base.CompTick();
            long now = Find.TickManager.TicksGame;
            for (int i = applied.Count - 1; i >= 0; i--)
            {
                var rec = applied[i];
                if (now >= rec.expireTick)
                {
                    TryRemoveRecord(rec);
                    applied.RemoveAt(i);
                }
            }
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            // Save the list of records (pawn references + hediffDefName + severity + expireTick)
            Scribe_Collections.Look(ref applied, "RR_appliedAlbumHediffs", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                // Clean up any stale records (e.g. pawn null)
                applied.RemoveAll(r => r == null || r.pawn == null);
            }
        }

        private void TryRemoveRecord(AppliedHediffRecord rec)
        {
            if (rec == null) return;
            var pawn = rec.pawn;
            if (pawn == null) return;
            HediffDef hdDef = DefDatabase<HediffDef>.GetNamedSilentFail(rec.hediffDefName);
            if (hdDef == null)
            {
                // nothing we can do
                return;
            }

            // Try to find the specific hediff on pawn (match by def and severity approximate)
            Hediff found = null;
            foreach (var h in pawn.health.hediffSet.hediffs)
            {
                if (h.def == hdDef)
                {
                    // If we stored severity, check closeness; but we remove the first matching for simplicity.
                    found = h;
                    break;
                }
            }
            if (found != null)
            {
                pawn.health.RemoveHediff(found);
            }
        }

        // Call this from playback logic to apply and schedule removal.
        // durationTicks should be computed from quality mapping (minutesByQuality * 2500)
        public void ApplyHediffToPawn(Pawn pawn, HediffDef def, int durationTicks, float severity)
        {
            if (pawn == null || def == null) return;

            // Avoid duplicate stacking of identical hediffs. If already present, refresh expiry instead.
            Hediff existing = pawn.health.hediffSet.GetFirstHediffOfDef(def);
            if (existing != null)
            {
                // Update severity to max, refresh expiry if we track it
                existing.Severity = Math.Max(existing.Severity, severity);
                // find record and update expireTick
                var rec = applied.Find(r => r.pawn == pawn && r.hediffDefName == def.defName);
                if (rec != null)
                {
                    rec.expireTick = Find.TickManager.TicksGame + durationTicks;
                }
                else
                {
                    // If hediff exists but we weren't tracking, add a new record to expire it
                    applied.Add(new AppliedHediffRecord { pawn = pawn, hediffDefName = def.defName, severity = severity, expireTick = Find.TickManager.TicksGame + durationTicks });
                }
                return;
            }

            var h = HediffMaker.MakeHediff(def, pawn);
            h.Severity = severity;
            pawn.health.AddHediff(h);
            applied.Add(new AppliedHediffRecord { pawn = pawn, hediffDefName = def.defName, severity = severity, expireTick = Find.TickManager.TicksGame + durationTicks });
        }

        // Lightweight convenience helper
        public void ApplyHediffToPawn(Pawn pawn, HediffDef def, int durationTicks, QualityCategory qc)
        {
            float severity = Mathf.Max(0.05f, (int)qc / 6f);
            ApplyHediffToPawn(pawn, def, durationTicks, severity);
        }

        // Internal class for saving applied hediff records.
        [Serializable]
        public class AppliedHediffRecord : IExposable
        {
            public Pawn pawn;
            public string hediffDefName;
            public float severity;
            public long expireTick;

            public void ExposeData()
            {
                Scribe_References.Look(ref pawn, "pawn");
                Scribe_Values.Look(ref hediffDefName, "hediffDefName", null);
                Scribe_Values.Look(ref severity, "severity", 0f);
                Scribe_Values.Look(ref expireTick, "expireTick", 0L);
            }
        }
    }
}
