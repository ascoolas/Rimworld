using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace MemorialDirector
{
    public enum CommandKind { Note, Camera, Follow, Unfollow, Walk, Face, Weather, Hour, End }

    /// <summary>
    /// 所有剧本指令：每个指令都有“检查”（开拍前）和“执行”（到点时）两部分。
    /// 新增指令时在这里加一个 case 即可。
    /// </summary>
    public static class Commands
    {
        static readonly Dictionary<string, CommandKind> tags = new Dictionary<string, CommandKind>
        {
            { "镜头", CommandKind.Camera },
            { "跟随", CommandKind.Follow },
            { "停止跟随", CommandKind.Unfollow },
            { "走", CommandKind.Walk },
            { "面向", CommandKind.Face },
            { "天气", CommandKind.Weather },
            { "时刻", CommandKind.Hour },
            { "结束", CommandKind.End },
            { "注释", CommandKind.Note },
        };

        static readonly Dictionary<CommandKind, string[]> attrs = new Dictionary<CommandKind, string[]>
        {
            { CommandKind.Note, new[] { "内容" } },
            { CommandKind.Camera, new[] { "对准", "缩放", "用时" } },
            { CommandKind.Follow, new[] { "角色", "缩放", "用时" } },
            { CommandKind.Unfollow, new string[0] },
            { CommandKind.Walk, new[] { "角色", "到", "步态", "面向" } },
            { CommandKind.Face, new[] { "角色", "目标" } },
            { CommandKind.Weather, new[] { "类型", "渐变" } },
            { CommandKind.Hour, new[] { "小时" } },
            { CommandKind.End, new string[0] },
        };

        static readonly Dictionary<string, string> weatherAlias = new Dictionary<string, string>
        {
            { "晴", "Clear" },
            { "雾", "Fog" },
            { "雨", "Rain" },
            { "雾雨", "FoggyRain" },
            { "雷雨", "RainyThunderstorm" },
            { "干雷暴", "DryThunderstorm" },
            { "小雪", "SnowGentle" },
            { "大雪", "SnowHard" },
        };

        static readonly Dictionary<string, LocomotionUrgency> gaits = new Dictionary<string, LocomotionUrgency>
        {
            { "漫步", LocomotionUrgency.Amble },
            { "慢走", LocomotionUrgency.Walk },
            { "正常", LocomotionUrgency.Jog },
            { "跑", LocomotionUrgency.Sprint },
        };

        static readonly Dictionary<string, Rot4> directions = new Dictionary<string, Rot4>
        {
            { "北", Rot4.North }, { "上", Rot4.North },
            { "南", Rot4.South }, { "下", Rot4.South },
            { "东", Rot4.East }, { "右", Rot4.East },
            { "西", Rot4.West }, { "左", Rot4.West },
        };

        public static string KnownTags => string.Join("、", tags.Keys);

        public static bool TryGetKind(string tag, out CommandKind kind) => tags.TryGetValue(tag.Trim(), out kind);

        public static string[] AllowedAttrs(CommandKind kind) => attrs[kind];

        // ======================= 检查 =======================

        public static void Check(Cue c, Map map, List<string> errors)
        {
            void Err(string msg) => errors.Add($"{c.Where}：{msg}");

            switch (c.kind)
            {
                case CommandKind.Camera:
                    if (!c.Has("对准") && !c.Has("缩放")) Err("至少要写 对准 或 缩放。");
                    if (c.Has("对准")) CheckSpot(c.Get("对准"), map, Err, allowDirection: false);
                    CheckZoom(c, Err);
                    CheckSeconds(c, "用时", Err);
                    break;

                case CommandKind.Follow:
                    RequirePawn(c, "角色", map, Err);
                    CheckZoom(c, Err);
                    CheckSeconds(c, "用时", Err);
                    break;

                case CommandKind.Walk:
                    RequirePawn(c, "角色", map, Err);
                    if (!c.Has("到")) { Err("缺少 到=\"x,z\"（或 到=\"某个角色\"）。"); break; }
                    if (TryParseCell(c.Get("到"), out var cell))
                    {
                        if (!cell.InBounds(map)) Err($"坐标 {c.Get("到")} 在地图外。地图大小是 {map.Size.x}×{map.Size.z}。");
                        else if (!cell.Standable(map)) Err($"坐标 {c.Get("到")} 站不了人（有墙、树或其它障碍）。");
                    }
                    else CheckSpot(c.Get("到"), map, Err, allowDirection: false);
                    if (c.Has("步态") && !gaits.ContainsKey(c.Get("步态")))
                        Err($"步态只能是：{string.Join("、", gaits.Keys)}。");
                    if (c.Has("面向")) CheckSpot(c.Get("面向"), map, Err, allowDirection: true);
                    break;

                case CommandKind.Face:
                    RequirePawn(c, "角色", map, Err);
                    if (!c.Has("目标")) Err("缺少 目标（坐标、角色名，或 北/南/东/西）。");
                    else CheckSpot(c.Get("目标"), map, Err, allowDirection: true);
                    break;

                case CommandKind.Weather:
                    if (!c.Has("类型")) Err($"缺少 类型。可以写：{string.Join("、", weatherAlias.Keys)}，或天气的英文 defName。");
                    else if (FindWeather(c.Get("类型")) == null)
                        Err($"找不到天气“{c.Get("类型")}”。可以写：{string.Join("、", weatherAlias.Keys)}，或天气的英文 defName。");
                    break;

                case CommandKind.Hour:
                    if (!float.TryParse(c.Get("小时") ?? "", NumberStyles.Float, CultureInfo.InvariantCulture, out float h) || h < 0f || h >= 24f)
                        Err("小时 要写 0 到 23 之间的数字，例如 小时=\"14\"。");
                    break;
            }
        }

        static void CheckZoom(Cue c, System.Action<string> err)
        {
            if (!c.Has("缩放")) return;
            CameraDirector.ZoomFromWord(c.Get("缩放"), out bool ok);
            if (!ok) err("缩放 可以写：特写、近、中、远、全景，或数字（11 最近，60 最远）。");
        }

        static void CheckSeconds(Cue c, string key, System.Action<string> err)
        {
            if (!c.Has(key)) return;
            if (!Script.TryParseSeconds(c.Get(key), out float s) || s < 0f) err($"{key}=\"{c.Get(key)}\" 不是有效的秒数。");
        }

        static void RequirePawn(Cue c, string key, Map map, System.Action<string> err)
        {
            if (!c.Has(key)) { err($"缺少 {key}。"); return; }
            FindPawn(c.Get(key), map, out string e);
            if (e != null) err(e);
        }

        static void CheckSpot(string text, Map map, System.Action<string> err, bool allowDirection)
        {
            if (TryParseCell(text, out var cell))
            {
                if (!cell.InBounds(map)) err($"坐标 {text} 在地图外。地图大小是 {map.Size.x}×{map.Size.z}。");
                return;
            }
            if (allowDirection && directions.ContainsKey(text.Trim())) return;
            FindPawn(text, map, out string e);
            if (e != null) err(e);
        }

        // ======================= 执行 =======================

        public static void Execute(Cue c, Map map, Stage stage)
        {
            switch (c.kind)
            {
                case CommandKind.Camera:
                {
                    Vector3? pos = null;
                    if (c.Has("对准")) pos = SpotPosition(c.Get("对准"), map);
                    float? size = c.Has("缩放") ? CameraDirector.ZoomFromWord(c.Get("缩放"), out _) : (float?)null;
                    stage.camera.MoveTo(pos, size, Seconds(c, "用时"));
                    break;
                }

                case CommandKind.Follow:
                {
                    var p = FindPawn(c.Get("角色"), map, out _);
                    if (p == null) return;
                    stage.camera.Follow(p);
                    if (c.Has("缩放"))
                        stage.camera.MoveTo(null, CameraDirector.ZoomFromWord(c.Get("缩放"), out _), Seconds(c, "用时"));
                    break;
                }

                case CommandKind.Unfollow:
                    stage.camera.StopFollow();
                    break;

                case CommandKind.Walk:
                    DoWalk(c, map, stage);
                    break;

                case CommandKind.Face:
                {
                    var p = FindPawn(c.Get("角色"), map, out _);
                    if (p == null) return;
                    var st = stage.GetState(p);
                    SetFacing(st, c.Get("目标"), map);
                    if (!p.pather.Moving) FaceNow(p, st);
                    break;
                }

                case CommandKind.Weather:
                {
                    var def = FindWeather(c.Get("类型"));
                    var wm = map.weatherManager;
                    if (IsYes(c.Get("渐变")))
                    {
                        wm.TransitionTo(def);
                    }
                    else
                    {
                        wm.curWeather = def;
                        wm.lastWeather = def;
                        wm.curWeatherAge = 100000;
                        wm.ResetSkyTargetLerpCache();
                    }
                    break;
                }

                case CommandKind.Hour:
                {
                    float target = float.Parse(c.Get("小时"), CultureInfo.InvariantCulture);
                    float now = GenLocalDate.HourFloat(map);
                    float diff = target - now;
                    if (diff < 0f) diff += 24f;
                    int ticks = Mathf.RoundToInt(diff * GenDate.TicksPerHour);
                    if (ticks > 0) Find.TickManager.DebugSetTicksGame(Find.TickManager.TicksGame + ticks);
                    break;
                }

                case CommandKind.End:
                    stage.FinishScript();
                    break;
            }
        }

        static void DoWalk(Cue c, Map map, Stage stage)
        {
            var p = FindPawn(c.Get("角色"), map, out _);
            if (p == null) return;
            var st = stage.GetState(p);
            st.ClearFacing();

            IntVec3 dest;
            var toText = c.Get("到");
            if (!TryParseCell(toText, out dest))
            {
                // 走到另一个角色身边，并面向他/她
                var other = FindPawn(toText, map, out _);
                if (other == null) return;
                dest = CellNextTo(other, p, map);
                st.faceTarget = other;
            }
            if (c.Has("面向")) SetFacing(st, c.Get("面向"), map);

            if (!p.CanReach(dest, PathEndMode.OnCell, Danger.Deadly))
            {
                Log.Warning($"[导演] {c.Where}：{p.LabelShort} 走不到 {dest.x},{dest.z}（路被挡住了）。");
                return;
            }

            var job = JobMaker.MakeJob(JobDefOf.Goto, dest);
            job.locomotionUrgency = c.Has("步态") ? gaits[c.Get("步态")] : LocomotionUrgency.Jog;
            stage.GiveDirectedJob(p, job);
        }

        static IntVec3 CellNextTo(Pawn target, Pawn walker, Map map)
        {
            var best = target.Position;
            float bestDist = float.MaxValue;
            foreach (var cell in GenAdj.CellsAdjacent8Way(target))
            {
                if (!cell.InBounds(map) || !cell.Standable(map)) continue;
                if (cell.GetFirstPawn(map) is Pawn other && other != walker) continue;
                float d = cell.DistanceToSquared(walker.Position);
                // 优先站在正侧面（不站斜角），看起来更像在对话
                if (cell.x != target.Position.x && cell.z != target.Position.z) d += 2f;
                if (d < bestDist) { bestDist = d; best = cell; }
            }
            return best;
        }

        static void SetFacing(ActorState st, string text, Map map)
        {
            st.ClearFacing();
            text = text.Trim();
            if (TryParseCell(text, out var cell)) st.faceTarget = cell;
            else if (directions.TryGetValue(text, out var rot)) st.faceDir = rot;
            else
            {
                var p = FindPawn(text, map, out _);
                if (p != null) st.faceTarget = p;
            }
        }

        static void FaceNow(Pawn p, ActorState st)
        {
            if (st.faceTarget.IsValid && st.faceTarget.Cell != p.Position) p.rotationTracker.FaceTarget(st.faceTarget);
            else if (st.faceDir.IsValid) p.Rotation = st.faceDir;
        }

        static Vector3 SpotPosition(string text, Map map)
        {
            if (TryParseCell(text, out var cell)) return cell.ToVector3Shifted();
            var p = FindPawn(text, map, out _);
            return p != null ? p.DrawPos : CameraDirector.CurrentRootPos();
        }

        static float Seconds(Cue c, string key)
        {
            if (!c.Has(key)) return 0f;
            return Script.TryParseSeconds(c.Get(key), out float s) ? s : 0f;
        }

        static bool IsYes(string v) => v != null && (v.Trim() == "是" || v.Trim().ToLowerInvariant() == "true");

        // ======================= 工具 =======================

        public static bool TryParseCell(string text, out IntVec3 cell)
        {
            cell = IntVec3.Invalid;
            if (text == null) return false;
            var parts = text.Replace('，', ',').Split(',');
            if (parts.Length != 2) return false;
            if (int.TryParse(parts[0].Trim(), out int x) && int.TryParse(parts[1].Trim(), out int z))
            {
                cell = new IntVec3(x, 0, z);
                return true;
            }
            return false;
        }

        static WeatherDef FindWeather(string name)
        {
            if (name == null) return null;
            name = name.Trim();
            if (weatherAlias.TryGetValue(name, out var defName)) name = defName;
            return DefDatabase<WeatherDef>.GetNamedSilentFail(name)
                ?? DefDatabase<WeatherDef>.AllDefs.FirstOrDefault(w => w.label == name);
        }

        /// <summary>按名字找地图上的角色：可以写昵称、名、姓或全名。</summary>
        public static Pawn FindPawn(string name, Map map, out string error)
        {
            error = null;
            name = name?.Trim();
            if (string.IsNullOrEmpty(name)) { error = "角色名是空的。"; return null; }

            var matches = map.mapPawns.AllPawnsSpawned.Where(p => NameMatches(p, name)).ToList();
            if (matches.Count == 1) return matches[0];
            if (matches.Count > 1)
            {
                error = $"有 {matches.Count} 个角色都叫“{name}”，请写全名或改成不重名。";
                return null;
            }
            var known = map.mapPawns.AllPawnsSpawned.Where(p => p.RaceProps.Humanlike)
                .Select(p => p.Name?.ToStringShort ?? p.LabelShort).Take(30);
            error = $"地图上找不到角色“{name}”。地图上的人有：{string.Join("、", known)}。";
            return null;
        }

        static bool NameMatches(Pawn p, string name)
        {
            bool Eq(string s) => !string.IsNullOrEmpty(s) && string.Equals(s.Trim(), name, System.StringComparison.OrdinalIgnoreCase);
            if (p.Name is NameTriple t)
                return Eq(t.Nick) || Eq(t.First) || Eq(t.Last) || Eq(t.ToStringFull) || Eq(t.ToStringShort);
            if (p.Name != null && (Eq(p.Name.ToStringShort) || Eq(p.Name.ToStringFull))) return true;
            return Eq(p.LabelShort);
        }
    }
}
