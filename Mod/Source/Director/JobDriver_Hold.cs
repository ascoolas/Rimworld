using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace MemorialDirector
{
    /// <summary>“待命”：原地站着；如果导演指定了朝向，就一直面向那里（目标移动也会跟着转头）。</summary>
    public class JobDriver_Hold : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

        protected override IEnumerable<Toil> MakeNewToils()
        {
            var hold = ToilMaker.MakeToil("DirectorHold");
            hold.initAction = () =>
            {
                pawn.pather.StopDead();
                Face();
            };
            hold.tickAction = Face;
            hold.defaultCompleteMode = ToilCompleteMode.Never;
            hold.handlingFacing = true; // 朝向由导演控制，游戏不要自己转头
            yield return hold;
        }

        void Face()
        {
            var stage = Stage.Instance;
            if (stage == null) return;
            var st = stage.GetState(pawn);
            if (st.faceTarget.IsValid)
            {
                if (st.faceTarget.HasThing && (!st.faceTarget.Thing.Spawned || st.faceTarget.Thing.Map != pawn.Map)) return;
                if (st.faceTarget.Cell == pawn.Position) return;
                pawn.rotationTracker.FaceTarget(st.faceTarget);
            }
            else if (st.faceDir.IsValid)
            {
                pawn.Rotation = st.faceDir;
            }
        }
    }
}
