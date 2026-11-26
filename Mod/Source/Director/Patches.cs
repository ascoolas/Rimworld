using System;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace MemorialDirector
{
    /// <summary>
    /// 用 Harmony 关掉游戏里会“自己发生”的东西。每个补丁单独安装，
    /// 某一个失败（比如游戏更新改了函数名）也不影响其它补丁。
    /// </summary>
    public static class Patches
    {
        static DirectorSettings S => DirectorMod.Settings;

        public static void Apply(Harmony h)
        {
            Prefix(h, typeof(Storyteller), "StorytellerTick", nameof(Storyteller_Prefix));
            Prefix(h, typeof(WeatherDecider), "WeatherDeciderTick", nameof(Weather_Prefix));
            Prefix(h, typeof(MentalBreaker), "MentalBreakerTickInterval", nameof(Mental_Prefix));
            Prefix(h, typeof(InspirationHandler), "InspirationHandlerTickInterval", nameof(Mental_Prefix));
            Prefix(h, typeof(MentalStateHandler), "TryStartMentalState", nameof(TryStartMentalState_Prefix));
            Prefix(h, typeof(Pawn_InteractionsTracker), "InteractionsTrackerTickInterval", nameof(Interactions_Prefix));
            Prefix(h, typeof(Pawn_JobTracker), "TryFindAndStartJob", nameof(TryFindAndStartJob_Prefix));
            Prefix(h, typeof(Pawn_JobTracker), "CheckForJobOverride", nameof(CheckForJobOverride_Prefix));
        }

        static void Prefix(Harmony h, Type type, string method, string prefix)
        {
            try
            {
                var target = AccessTools.Method(type, method);
                if (target == null)
                {
                    Log.Warning($"[导演] 找不到 {type.Name}.{method}，这一项拍摄保护不起作用。");
                    return;
                }
                h.Patch(target, prefix: new HarmonyMethod(typeof(Patches), prefix));
            }
            catch (Exception e)
            {
                Log.Error($"[导演] 安装补丁 {type.Name}.{method} 失败：{e}");
            }
        }

        static bool Storyteller_Prefix() => !S.silentStoryteller;

        static bool Weather_Prefix() => !S.lockWeather;

        static bool Mental_Prefix() => !S.blockMental;

        static bool Interactions_Prefix() => !S.blockInteractions;

        static bool TryStartMentalState_Prefix(ref bool __result)
        {
            if (!S.blockMental) return true;
            __result = false;
            return false;
        }

        // 一个工作结束、游戏要给角色找下一件事做时：演员一律改为“待命”。
        static bool TryFindAndStartJob_Prefix(Pawn ___pawn)
        {
            return !Stage.TryStartHold(___pawn);
        }

        // 游戏会定期检查“有没有更要紧的事”，演员跳过这一检查。
        static bool CheckForJobOverride_Prefix(Pawn ___pawn)
        {
            return !Stage.IsActor(___pawn);
        }
    }
}
