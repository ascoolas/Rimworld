using System.IO;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace MemorialDirector
{
    /// <summary>导演 Mod 的设置（选项 → Mod 设置 → 修道院纪事 · 导演）。</summary>
    public class DirectorSettings : ModSettings
    {
        public bool silentStoryteller = true;   // 不触发任何随机事件
        public bool lockWeather = true;         // 天气只由剧本改变
        public bool blockMental = true;         // 不出现精神崩溃、灵感
        public bool blockInteractions = true;   // 不出现随机社交气泡
        public bool freezeNeeds = true;         // 需求始终满值
        public bool holdActors = true;          // 殖民者没有指令时原地待命
        public bool showMouseCell = true;       // 鼠标旁显示格子坐标
        public bool autoScreenshotMode = false; // 开拍时自动隐藏界面
        public bool pauseAtEnd = true;          // 剧本结束时暂停
        public string lastScript = "";

        public override void ExposeData()
        {
            Scribe_Values.Look(ref silentStoryteller, "silentStoryteller", true);
            Scribe_Values.Look(ref lockWeather, "lockWeather", true);
            Scribe_Values.Look(ref blockMental, "blockMental", true);
            Scribe_Values.Look(ref blockInteractions, "blockInteractions", true);
            Scribe_Values.Look(ref freezeNeeds, "freezeNeeds", true);
            Scribe_Values.Look(ref holdActors, "holdActors", true);
            Scribe_Values.Look(ref showMouseCell, "showMouseCell", true);
            Scribe_Values.Look(ref autoScreenshotMode, "autoScreenshotMode", false);
            Scribe_Values.Look(ref pauseAtEnd, "pauseAtEnd", true);
            Scribe_Values.Look(ref lastScript, "lastScript", "");
        }

        public void DoSettingsList(Listing_Standard l)
        {
            l.Label("拍摄模式（推荐全部开启）");
            l.CheckboxLabeled("静默叙事者：不触发任何随机事件（袭击、访客、疾病、天象……）", ref silentStoryteller);
            l.CheckboxLabeled("锁定天气：天气只由剧本的 <天气> 指令改变", ref lockWeather);
            l.CheckboxLabeled("禁止精神崩溃和灵感", ref blockMental);
            l.CheckboxLabeled("禁止随机社交（聊天气泡、打架）", ref blockInteractions);
            l.CheckboxLabeled("冻结需求：饥饿、睡眠、娱乐等始终满值", ref freezeNeeds);
            l.CheckboxLabeled("演员待命：殖民者没有接到指令时原地站着不动", ref holdActors,
                "开启后，殖民者不会自己去干活、吃饭、闲逛。你手动右键下达的命令仍然有效，方便布景。");
            l.GapLine();
            l.Label("拍摄辅助");
            l.CheckboxLabeled("在鼠标旁显示格子坐标（写剧本用；开拍后自动隐藏）", ref showMouseCell);
            l.CheckboxLabeled("开拍时自动进入截图模式（隐藏所有界面）", ref autoScreenshotMode,
                "截图模式的开关键在 选项→按键设置 里叫“切换截图模式”。");
            l.CheckboxLabeled("剧本结束时自动暂停游戏", ref pauseAtEnd);
        }
    }

    public class DirectorMod : Mod
    {
        public static DirectorMod Instance;
        public static DirectorSettings Settings;

        public DirectorMod(ModContentPack content) : base(content)
        {
            Instance = this;
            Settings = GetSettings<DirectorSettings>();
            Patches.Apply(new Harmony("MemorialDoConvento.Director"));
            Log.Message("[导演] 修道院纪事 · 导演 Mod 已加载。剧本文件夹：" + ScriptsDir);
        }

        /// <summary>剧本文件夹：Mod 目录下的 Scripts（可以有子文件夹）。</summary>
        public string ScriptsDir => Path.Combine(Content.RootDir, "Scripts");

        public override string SettingsCategory() => "修道院纪事 · 导演";

        public override void DoSettingsWindowContents(Rect inRect)
        {
            var l = new Listing_Standard();
            l.Begin(inRect);
            Settings.DoSettingsList(l);
            l.End();
        }
    }

    [DefOf]
    public static class DirectorDefOf
    {
        public static JobDef MemorialDirector_Hold;
        public static KeyBindingDef MemorialDirector_Start;
        public static KeyBindingDef MemorialDirector_Stop;

        static DirectorDefOf() => DefOfHelper.EnsureInitializedInCtor(typeof(DirectorDefOf));
    }
}
