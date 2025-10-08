using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Verse;
using RimWorld;

namespace RimRadio
{
    public class RimRadioDefExtension : DefModExtension
    {
        public string genre;
    }

    public class CompAlbumGenerator : ThingComp
    {
        private CompProperties_AlbumGenerator Props => (CompProperties_AlbumGenerator)this.props;

        private string forcedGenre = null;
        private Pawn authorPawn = null;
        private string authorNameFallback = null;
        private string generatedTitle = null;
        private string generatedDescription = null;

        // NOTE: we do not reference TaleBookReference at compile time.
        // If available at runtime we will use reflection to access it.
        private object reflectedTaleReference = null;

        public string GeneratedTitle => generatedTitle;
        public string GeneratedDescription => generatedDescription;
        public string ForcedGenre => forcedGenre;
        public Pawn AuthorPawn => authorPawn;
        public string AuthorNameFallback => authorNameFallback;

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref forcedGenre, "forcedGenre");
            Scribe_References.Look(ref authorPawn, "authorPawn");
            Scribe_Values.Look(ref authorNameFallback, "authorNameFallback");
            Scribe_Values.Look(ref generatedTitle, "generatedTitle");
            Scribe_Values.Look(ref generatedDescription, "generatedDescription");

            // If we managed to instantiate a reflected tale reference, try to persist it using Deep (best-effort).
            // If it can't be persisted, that's fine; fallback path will still work on load.
            if (reflectedTaleReference != null)
                Scribe_Deep.Look(ref reflectedTaleReference, "reflectedTaleReference");
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);

            if (!respawningAfterLoad)
            {
                if (string.IsNullOrEmpty(forcedGenre))
                {
                    var ext = parent.def.GetModExtension<RimRadioDefExtension>();
                    if (ext != null && !string.IsNullOrEmpty(ext.genre))
                        forcedGenre = ext.genre;
                }

                if (string.IsNullOrEmpty(generatedTitle) || string.IsNullOrEmpty(generatedDescription))
                {
                    // Attempt to find and cache a reflected TaleBookReference instance if possible
                    TryInitializeReflectedTaleReference();
                    GenerateMetadata();
                }
            }
            else
            {
                // On load, try to re-init reflection references if not present
                if (reflectedTaleReference == null) TryInitializeReflectedTaleReference();
            }
        }

        public void SetAuthor(Pawn p)
        {
            if (p == null) return;
            authorPawn = p;
            try
            {
                authorNameFallback = p.LabelShort?.Trim();
                if (string.IsNullOrEmpty(authorNameFallback) && p.Name != null) authorNameFallback = p.Name.ToStringFull;
            }
            catch
            {
                authorNameFallback = p.LabelShort;
            }
            GenerateMetadata();
        }

        public void SetGenre(string genre, bool persist = true)
        {
            if (string.IsNullOrWhiteSpace(genre)) return;
            if (persist) forcedGenre = genre;
            else forcedGenre = null;
            GenerateMetadata();
        }

        private string GetEffectiveGenre()
        {
            if (!string.IsNullOrEmpty(forcedGenre)) return forcedGenre;
            var ext = parent?.def?.GetModExtension<RimRadioDefExtension>();
            if (ext != null && !string.IsNullOrEmpty(ext.genre)) return ext.genre;
            return "Misc";
        }

        private string QualityAdjectiveFor(QualityCategory qc)
        {
            switch (qc)
            {
                case QualityCategory.Awful: return "atrocious";
                case QualityCategory.Poor: return "poor";
                case QualityCategory.Normal: return "decent";
                case QualityCategory.Good: return "good";
                case QualityCategory.Excellent: return "excellent";
                case QualityCategory.Masterwork: return "masterfully recorded";
                case QualityCategory.Legendary: return "legendary";
                default: return "decent";
            }
        }

        // Attempt to find TaleBookReference.Taleless (or equivalent) and cache it.
        // Uses reflection by searching all loaded assemblies for type named "TaleBookReference"
        // and then looks for a public static property or field named "Taleless".
        private void TryInitializeReflectedTaleReference()
        {
            try
            {
                if (reflectedTaleReference != null) return;

                Type taleType = null;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (asm == null) continue;
                    // Try typical full name
                    taleType = asm.GetType("RimWorld.TaleBookReference") ?? asm.GetType("TaleBookReference");
                    if (taleType != null) break;
                    // sometimes the type may be in a different namespace or assembly; try types by name
                    foreach (var t in asm.GetTypesSafe())
                    {
                        if (t.Name == "TaleBookReference")
                        {
                            taleType = t;
                            break;
                        }
                    }
                    if (taleType != null) break;
                }

                if (taleType == null) return;

                // Try static property Taleless or static field Taleless
                var prop = taleType.GetProperty("Taleless", BindingFlags.Public | BindingFlags.Static);
                if (prop != null)
                {
                    reflectedTaleReference = prop.GetValue(null, null);
                    return;
                }

                var field = taleType.GetField("Taleless", BindingFlags.Public | BindingFlags.Static);
                if (field != null)
                {
                    reflectedTaleReference = field.GetValue(null);
                    return;
                }
            }
            catch
            {
                // swallow - reflection is best effort
                reflectedTaleReference = null;
            }
        }

        // Try to use Tale-based generation (via reflection) to produce the text.
        // purposeName must be "ArtName" or "ArtDescription".
        private string TryGenerateViaReflectedTale(string purposeName, RulePackDef maker)
        {
            if (reflectedTaleReference == null || maker == null) return null;

            try
            {
                // Find the TextGenerationPurpose enum type
                Type purposeEnumType = null;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (asm == null) continue;
                    var t = asm.GetType("RimWorld.TextGenerationPurpose") ?? asm.GetType("TextGenerationPurpose");
                    if (t != null)
                    {
                        purposeEnumType = t;
                        break;
                    }
                }

                if (purposeEnumType == null) return null;

                // Parse the enum value
                var purposeValue = Enum.Parse(purposeEnumType, purposeName);

                // Find GenerateText method (taking three parameters) on the tale reference
                MethodInfo mi = reflectedTaleReference.GetType()
                    .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .FirstOrDefault(m =>
                    {
                        var parameters = m.GetParameters();
                        return m.Name == "GenerateText"
                            && parameters.Length == 3;
                    });

                if (mi == null) return null;

                // Invoke: GenerateText(purposeEnumValue, maker, this)
                object result = mi.Invoke(reflectedTaleReference, new object[] { purposeValue, maker, this });
                if (result is string s) return s;
                return result?.ToString();
            }
            catch (Exception e)
            {
                Log.Warning("[RimRadio] Reflection tale generation failed: " + e.Message);
                return null;
            }
        }

        private void GenerateMetadata()
        {
            string genre = GetEffectiveGenre();
            string nameRulepack = (Props?.nameRulepackPrefix ?? "MusicName_") + genre;
            string descRulepack = (Props?.descRulepackPrefix ?? "MusicDesc_") + genre;

            string artistDisplay = authorPawn != null ? authorPawn.LabelShort : (authorNameFallback ?? "Unknown");

            // Try reflected book-style name generation first (if the maker is provided)
            if (Props?.nameMaker != null)
            {
                string rawTitle = TryGenerateViaReflectedTale("ArtName", Props.nameMaker);
                if (!string.IsNullOrEmpty(rawTitle))
                {
                    // We don't know compile-time TextGenerationPurpose, so use capitalisation helper only
                    generatedTitle = GenText.CapitalizeAsTitle(rawTitle);
                }
            }

            // Fallback to prefix-based name resolution
            if (string.IsNullOrEmpty(generatedTitle))
            {
                string title = ResolveRulepackEntry(nameRulepack, "r_album_name", new Dictionary<string, string> { { "Artist", artistDisplay } }) ??
                               ResolveRulepackEntry(nameRulepack, "r_album_name_default", new Dictionary<string, string> { { "Artist", artistDisplay } });

                if (string.IsNullOrEmpty(title))
                {
                    title = $"{artistDisplay} - {genre} Album";
                }
                generatedTitle = title;
            }

            // Try reflected book-style description generation
            if (Props?.descriptionMaker != null)
            {
                string desc = TryGenerateViaReflectedTale("ArtDescription", Props.descriptionMaker);
                if (!string.IsNullOrEmpty(desc))
                {
                    generatedDescription = desc;
                    return;
                }
            }

            // Fallback: existing assembly behavior (genre + middles + quality)
            string first = ResolveRulepackEntry(descRulepack, "r_genre_sentence", new Dictionary<string, string> { { "Artist", artistDisplay } }) ?? $"This is a {genre.ToLower()} record.";

            int M = MiddleCountForQuality();
            List<string> middles = new List<string>();
            int maxMiddleVariants = 20;
            var usedIndices = new HashSet<int>();
            var rand = new System.Random();

            int attempts = 0;
            while (middles.Count < M && attempts < maxMiddleVariants * 3)
            {
                attempts++;
                int idx = rand.Next(1, maxMiddleVariants + 1);
                if (usedIndices.Contains(idx)) continue;
                string key = $"r_middle_sentence_{idx}";
                string s = ResolveRulepackEntry(descRulepack, key, new Dictionary<string, string> { { "Artist", artistDisplay } });
                if (!string.IsNullOrEmpty(s))
                {
                    middles.Add(s);
                    usedIndices.Add(idx);
                }
            }

            attempts = 0;
            while (middles.Count < M && attempts < 10)
            {
                attempts++;
                string key = "r_middle_sentence";
                string s = ResolveRulepackEntry(descRulepack, key, new Dictionary<string, string> { { "Artist", artistDisplay } });
                if (!string.IsNullOrEmpty(s))
                {
                    middles.Add(s);
                }
            }

            CompQuality qualityComp = parent.TryGetComp<CompQuality>();
            string qualityWord = qualityComp != null ? qualityComp.Quality.ToString() : "Normal";
            string last = ResolveRulepackEntry(descRulepack, "r_sound_quality_sentence", new Dictionary<string, string>
            {
                { "Artist", artistDisplay },
                { "Quality", qualityWord },
                { "QualityAdjective", QualityAdjectiveFor(qualityComp?.Quality ?? QualityCategory.Normal) }
            }) ?? $"Recorded with standard equipment, the sound is average.";

            List<string> pieces = new List<string>();
            if (!string.IsNullOrEmpty(first)) pieces.Add(Punctuate(first));
            foreach (var mid in middles) if (!string.IsNullOrEmpty(mid)) pieces.Add(Punctuate(mid));
            if (!string.IsNullOrEmpty(last)) pieces.Add(Punctuate(last));

            generatedDescription = string.Join(" ", pieces).Trim();
        }

        private string Punctuate(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return s;
            char last = s[s.Length - 1];
            if (last == '.' || last == '!' || last == '?') return s.Trim();
            return s.Trim() + ".";
        }

        private int MiddleCountForQuality()
        {
            CompQuality qComp = parent.TryGetComp<CompQuality>();
            QualityCategory qc = QualityCategory.Normal;
            if (qComp != null) qc = qComp.Quality;

            int idx = 2;
            switch (qc)
            {
                case QualityCategory.Awful: idx = 1; break;
                case QualityCategory.Poor: idx = 1; break;
                case QualityCategory.Normal: idx = 2; break;
                case QualityCategory.Good: idx = 3; break;
                case QualityCategory.Excellent: idx = 4; break;
                case QualityCategory.Masterwork: idx = 5; break;
                case QualityCategory.Legendary: idx = 5; break;
            }

            if (idx > 5) idx = 5;
            if (idx < 1) idx = 1;
            return idx;
        }

        // --------------------------------------------------------------------
        // IMPORTANT: paste your original ResolveRulepackEntry implementation here.
        // --------------------------------------------------------------------
        private string ResolveRulepackEntry(string rulepackDefName, string rootKeyword, Dictionary<string, string> injections = null)
        {
            // --- BEGIN PASTE YOUR ORIGINAL ResolveRulepackEntry BODY HERE ---
            // The original implementation should include the GrammarResolver attempts
            // and the reflection fallback that reads RulePackDef.rulePack.rulesStrings entries.
            // Copy the exact method body from your previous CompAlbumGenerator implementation.
            // --- END PASTE ---
            //
            // For compilation until you paste your original method, return null to avoid failures.
            return null;
        }
    }

    // Small helper extension to safely iterate types in an assembly without throwing on dynamic assemblies
    internal static class AssemblyExt
    {
        public static Type[] GetTypesSafe(this System.Reflection.Assembly asm)
        {
            try
            {
                return asm.GetTypes();
            }
            catch
            {
                return new Type[0];
            }
        }
    }
}
