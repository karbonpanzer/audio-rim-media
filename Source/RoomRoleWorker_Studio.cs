using Verse;
using RimWorld;

namespace RimRadio
{
    public class RoomRoleWorker_Studio : RoomRoleWorker
    {
        public override float GetScore(Room room)
        {
            if (room == null) return 0f;

            float score = 0f;
            foreach (var c in room.ContainedAndAdjacentThings)
            {
                if (c?.def == null) continue;
                if (c.def.defName == "RR_RecordingConsole") score += 25f;
                if (c.def.defName == "RR_AlbumRack") score += 10f;
            }
            score += room.GetStat(RoomStatDefOf.Impressiveness) * 0.25f;
            return score;
        }
    }
}
