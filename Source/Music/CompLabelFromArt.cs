using RimWorld;
using Verse;

namespace RimRadio
{
    public class CompProperties_LabelFromArt : CompProperties
    {
        public bool titleOverrides = true;     // use art title as the main label
        public bool appendQuality = true;      // add (Excellent), (Good), etc. to label
        public bool showNormalQuality = true;  // include (Normal); set false if you want to hide it

        public CompProperties_LabelFromArt()
        {
            compClass = typeof(CompLabelFromArt);
        }
    }

    public class CompLabelFromArt : ThingComp
    {
        public CompProperties_LabelFromArt Props => (CompProperties_LabelFromArt)props;

        public override void PostPostMake()
        {
            base.PostPostMake();

            // Make sure art initializes so we get a title immediately (dev-spawn safe)
            var compArt = parent.TryGetComp<CompArt>();
            if (compArt != null && !compArt.Active)
            {
                compArt.InitializeArt(null);
            }
        }

        public override string TransformLabel(string label)
        {
            string result = label;

            // 1) Use art title instead of base label (book-like behavior)
            var compArt = parent.TryGetComp<CompArt>();
            if (Props.titleOverrides && compArt != null && compArt.Active && !compArt.Title.NullOrEmpty())
            {
                result = compArt.Title;
            }

            // 2) Append quality so it shows on ground labels too
            if (Props.appendQuality)
            {
                var compQual = parent.TryGetComp<CompQuality>();
                if (compQual != null)
                {
                    var qc = compQual.Quality;
                    if (Props.showNormalQuality || qc != QualityCategory.Normal)
                    {
                        var q = QualityUtility.GetLabel(qc); // "poor", "excellent", etc.
                        if (!q.NullOrEmpty())
                            result = $"{result} ({q.CapitalizeFirst()})";
                    }
                }
            }

            return result;
        }
    }
}
