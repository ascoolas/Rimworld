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

        public const float MinSize = 2.5f, MaxSize = 80f;

        /// <summary>放开游戏自带的缩放范围（原版约 11–60），让剧本可以拍特写和鸟瞰。</summary>
        public static void UnlockZoomRange()
        {
            var cfg = Find.CameraDriver?.config;
            if (cfg == null) return;
            if (cfg.sizeRange.min != MinSize || cfg.sizeRange.max != MaxSize)
                cfg.sizeRange = new FloatRange(MinSize, MaxSize);
        }

        public static float ZoomFromWord(string word, out bool ok)
        {
            ok = true;
            switch (word.Trim())
            {
                case "特写": return 4f;
                case "近景": case "近": return 6f;
                case "中景": case "中": return 8f;
                case "全景": return 14f;
                case "大全景": case "远": return 28f;
                case "鸟瞰": return 40f;
            }
            ok = float.TryParse(word, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var f) && f >= MinSize && f <= MaxSize;
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
