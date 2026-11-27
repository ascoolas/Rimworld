using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI;

namespace MemorialDirector
{
    /// <summary>每个演员的状态：导演给的当前工作、要面向的目标。</summary>
    public class ActorState
    {
        public Job directedJob;
        public LocalTargetInfo faceTarget = LocalTargetInfo.Invalid;
        public Rot4 faceDir = Rot4.Invalid;

        public void ClearFacing()
        {
            faceTarget = LocalTargetInfo.Invalid;
            faceDir = Rot4.Invalid;
        }
    }

    /// <summary>
    /// “舞台”：跟着存档一起存在的游戏组件。负责让演员待命、冻结需求、
    /// 运行剧本、控制镜头、显示坐标、响应快捷键。
    /// </summary>
    public class Stage : GameComponent
    {
        public static Stage Instance => Current.Game?.GetComponent<Stage>();
        static DirectorSettings S => DirectorMod.Settings;

        readonly Dictionary<Pawn, ActorState> actors = new Dictionary<Pawn, ActorState>();
        public ScriptRunner runner;
        public readonly CameraDirector camera = new CameraDirector();

        public Stage(Game game) { }

        public bool Running => runner != null;

        public ActorState GetState(Pawn p)
        {
            if (!actors.TryGetValue(p, out var st))
            {
                st = new ActorState();
                actors[p] = st;
            }
            return st;
        }

        // ---------- 演员待命 ----------

        /// <summary>这个角色现在是否受导演控制（待命）。</summary>
        public static bool IsActor(Pawn p)
        {
            if (S == null || !S.holdActors || p == null) return false;
            if (!p.Spawned || p.Dead || p.Downed || p.InMentalState || p.Drafted) return false;
            if (p.Faction == null || !p.Faction.IsPlayer || !p.RaceProps.Humanlike) return false;
            return true;
        }

        public static bool TryStartHold(Pawn p)
        {
            if (!IsActor(p)) return false;
            StartHold(p);
            return true;
        }

        public static void StartHold(Pawn p)
        {
            var job = JobMaker.MakeJob(DirectorDefOf.MemorialDirector_Hold);
            job.playerForced = true;
            p.jobs.ClearQueuedJobs();
            p.jobs.StartJob(job, JobCondition.InterruptForced, cancelBusyStances: true);
        }

        /// <summary>让演员执行导演给的工作（走位等）。</summary>
        public void GiveDirectedJob(Pawn p, Job job)
        {
            job.playerForced = true;
            GetState(p).directedJob = job;
            p.jobs.ClearQueuedJobs();
            p.jobs.StartJob(job, JobCondition.InterruptForced, cancelBusyStances: true);
        }

        void KeepActorsInLine()
        {
            foreach (var map in Find.Maps)
            {
                var pawns = map.mapPawns.FreeColonistsSpawned;
                for (int i = 0; i < pawns.Count; i++)
                {
                    var p = pawns[i];
                    if (!IsActor(p)) continue;
                    var cur = p.CurJob;
                    if (cur != null)
                    {
                        if (cur.def == DirectorDefOf.MemorialDirector_Hold) continue;
                        if (actors.TryGetValue(p, out var st) && st.directedJob == cur) continue;
                        // 玩家手动右键下的命令：布景时有用，不打断
                        if (cur.playerForced && !Running) continue;
                    }
                    StartHold(p);
                }
            }
        }

        void FreezeNeeds()
        {
            foreach (var map in Find.Maps)
            {
                var pawns = map.mapPawns.FreeColonistsSpawned;
                for (int i = 0; i < pawns.Count; i++)
                {
                    var needs = pawns[i].needs?.AllNeeds;
                    if (needs == null) continue;
                    foreach (var n in needs)
                    {
                        if (n is Need_Mood) continue;
                        try { n.CurLevelPercentage = 1f; } catch { }
                    }
                }
            }
        }

        // ---------- 剧本 ----------

        public bool StartScript(Script script, Map map, out List<string> errors)
        {
            errors = script.Validate(map);
            if (errors.Count > 0) return false;

            StopScript(false);
            actors.Clear();
            runner = new ScriptRunner(script, map, this);
            if (S.autoScreenshotMode) SetScreenshotMode(true);
            Find.TickManager.CurTimeSpeed = TimeSpeed.Normal;
            runner.Process(); // 0 秒的指令立即执行，第一帧镜头就到位
            return true;
        }

        public void StopScript(bool byUser)
        {
            if (runner == null) return;
            var name = runner.script.name;
            runner = null;
            camera.Reset();
            if (byUser) Messages.Message("[导演] 已停止：" + name, MessageTypeDefOf.NeutralEvent, false);
        }

        public void FinishScript()
        {
            var name = runner?.script.name;
            runner = null;
            camera.StopFollow();
            if (S.pauseAtEnd) Find.TickManager.Pause();
            Log.Message("[导演] 剧本播放完毕：" + name);
        }

        public static void SetScreenshotMode(bool on)
        {
            var h = Find.UIRoot?.screenshotMode;
            if (h != null) Traverse.Create(h).Field("active").SetValue(on);
        }

        // ---------- 游戏回调 ----------

        public override void GameComponentTick()
        {
            if (S.holdActors) KeepActorsInLine();
            if (S.freezeNeeds && Find.TickManager.TicksGame % 60 == 0) FreezeNeeds();
            if (runner != null)
            {
                runner.elapsed++;
                runner.Process();
            }
        }

        bool cursorHidden;

        public override void GameComponentUpdate()
        {
            CameraDirector.UnlockZoomRange();
            if (Running && S.lockCamera)
                Traverse.Create(Find.CameraDriver).Field("velocity").SetValue(Vector3.zero);
            camera.Update();

            bool hide = Running && S.hideCursor;
            if (hide) Cursor.visible = false;
            else if (cursorHidden) Cursor.visible = true;
            cursorHidden = hide;
        }

        public override void GameComponentOnGUI()
        {
            HandleHotkeys();
            DrawMouseCell();
        }

        void HandleHotkeys()
        {
            if (DirectorDefOf.MemorialDirector_Stop != null && DirectorDefOf.MemorialDirector_Stop.KeyDownEvent)
            {
                Event.current.Use();
                StopScript(true);
            }
            else if (DirectorDefOf.MemorialDirector_Start != null && DirectorDefOf.MemorialDirector_Start.KeyDownEvent)
            {
                Event.current.Use();
                MainTabWindow_Director.StartLastScript();
            }
        }

        void DrawMouseCell()
        {
            if (!S.showMouseCell || Running) return;
            var map = Find.CurrentMap;
            if (map == null || WorldRendererUtility.WorldRendered) return;
            if (Find.UIRoot?.screenshotMode != null && Find.UIRoot.screenshotMode.Active) return;
            var c = UI.MouseCell();
            if (!c.InBounds(map)) return;
            var text = c.x + "," + c.z;
            var mouse = Event.current.mousePosition;
            Text.Font = GameFont.Small;
            var size = Text.CalcSize(text);
            var rect = new Rect(mouse.x + 18f, mouse.y + 18f, size.x + 8f, size.y + 2f);
            Widgets.DrawBoxSolid(rect, new Color(0f, 0f, 0f, 0.6f));
            Widgets.Label(rect.ContractedBy(4f, 1f), text);
        }
    }
}
