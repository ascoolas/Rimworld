using UnityEngine;
using Verse;

namespace MemorialDirector
{
    /// <summary>
    /// 镜头：瞬间切换、平滑推拉摇移、跟随角色。
    /// 动画按游戏速度推进（暂停时镜头也停），保证每次重拍节奏一致。
    /// </summary>
    public class CameraDirector
    {
        Pawn follow;

        bool animating;
        bool animPos;
        Vector3 fromPos, toPos;
        float fromSize, toSize;
        float t, duration;

        public static float ZoomFromWord(string word, out bool ok)
        {
            ok = true;
            switch (word.Trim())
            {
                case "特写": return 11f;
                case "近": return 18f;
                case "中": return 28f;
                case "远": return 45f;
                case "全景": return 60f;
            }
            ok = float.TryParse(word, out var f) && f > 0f;
            return f;
        }

        public void Reset()
        {
            follow = null;
            animating = false;
        }

        public void StopFollow() => follow = null;

        public void Follow(Pawn p) => follow = p;

        /// <summary>移动镜头。pos 为 null 表示位置不变；size 为 null 表示缩放不变；seconds 为 0 表示硬切。</summary>
        public void MoveTo(Vector3? pos, float? size, float seconds)
        {
            var driver = Find.CameraDriver;
            if (pos.HasValue) follow = null; // 指定了新位置，就不再跟随
            var curPos = CurrentRootPos();
            var curSize = driver.RootSize;

            if (seconds <= 0f)
            {
                animating = false;
                driver.SetRootPosAndSize(pos ?? curPos, size ?? curSize);
                return;
            }
            animating = true;
            animPos = pos.HasValue;
            fromPos = curPos;
            toPos = pos ?? curPos;
            fromSize = curSize;
            toSize = size ?? curSize;
            t = 0f;
            duration = seconds;
        }

        public static Vector3 CurrentRootPos()
        {
            var p = Find.Camera.transform.position;
            return new Vector3(p.x, 0f, p.z);
        }

        public void Update()
        {
            if (Find.CurrentMap == null) return;
            if (!animating && follow == null) return;

            var driver = Find.CameraDriver;
            Vector3? followPos = null;
            if (follow != null)
            {
                if (follow.Spawned && follow.Map == Find.CurrentMap)
                {
                    var d = follow.DrawPos;
                    followPos = new Vector3(d.x, 0f, d.z);
                }
            }

            Vector3 pos = followPos ?? CurrentRootPos();
            float size = driver.RootSize;

            if (animating)
            {
                if (!Find.TickManager.Paused)
                    t += Time.deltaTime * Find.TickManager.TickRateMultiplier;
                float k = duration <= 0f ? 1f : Mathf.Clamp01(t / duration);
                k = k * k * (3f - 2f * k); // 缓入缓出
                size = Mathf.Lerp(fromSize, toSize, k);
                if (animPos) pos = Vector3.Lerp(fromPos, toPos, k);
                if (k >= 1f) animating = false;
            }

            driver.SetRootPosAndSize(pos, size);
        }
    }
}
