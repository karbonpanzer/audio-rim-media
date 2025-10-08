using System;
using System.Reflection;
using RimWorld;
using Verse;

namespace RimRadio
{
    public static class CompQualityHelper
    {
        public static QualityCategory GetQualityFromThing(Thing t)
        {
            if (t == null) return QualityCategory.Normal;
            var comp = t.TryGetComp<CompQuality>();
            if (comp != null) return comp.Quality;
            return QualityCategory.Normal;
        }

        public static void SetQualityOnThing(Thing t, QualityCategory qc)
        {
            if (t == null) return;
            var compQ = t.TryGetComp<CompQuality>();
            if (compQ == null) return;

            var type = compQ.GetType();
            var mi = type.GetMethod("SetQuality", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (mi != null)
            {
                var parms = mi.GetParameters();
                try
                {
                    if (parms.Length == 2)
                    {
                        var ctxType = parms[1].ParameterType;
                        object ctxVal = null;
                        try { ctxVal = Enum.Parse(ctxType, "Local"); } catch { ctxVal = Enum.GetValues(ctxType).GetValue(0); }
                        mi.Invoke(compQ, new object[] { qc, ctxVal });
                        return;
                    }
                    if (parms.Length == 1)
                    {
                        mi.Invoke(compQ, new object[] { qc });
                        return;
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning($"CompQualityHelper.SetQualityOnThing -> SetQuality invoke failed: {ex}");
                }
            }

            var prop = type.GetProperty("Quality", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (prop != null && prop.CanWrite)
            {
                prop.SetValue(compQ, qc);
                return;
            }

            var fi = type.GetField("quality", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                     ?? type.GetField("_quality", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (fi != null) fi.SetValue(compQ, qc);
            else Log.Warning($"CompQualityHelper: couldn't set quality on {t.def.defName}");
        }
    }
}
