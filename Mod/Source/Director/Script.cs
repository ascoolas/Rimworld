using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using Verse;

namespace MemorialDirector
{
    /// <summary>剧本里的一条指令（XML 里的一行）。</summary>
    public class Cue
    {
        public string tag;           // 原始标签名，如 “走”
        public CommandKind kind;
        public int tick;             // 第几个游戏刻执行（1 秒 = 60 刻）
        public int line;             // 在剧本文件里的行号，报错用
        public Dictionary<string, string> attrs = new Dictionary<string, string>();

        public string Get(string key) => attrs.TryGetValue(key, out var v) ? v.Trim() : null;
        public bool Has(string key) => !string.IsNullOrWhiteSpace(Get(key));
        public string Where => $"第 {line} 行 <{tag}>";
    }

    public class Script
    {
        public string name;
        public string path;
        public List<Cue> cues = new List<Cue>();
        public int endTick;
        public List<string> parseErrors = new List<string>();

        public const int TicksPerSecond = 60;
        public const string TimeAttr = "秒";

        /// <summary>读取剧本文件。语法错误记录在 parseErrors 里。</summary>
        public static Script Load(string path)
        {
            var s = new Script { path = path, name = Path.GetFileNameWithoutExtension(path) };
            XDocument doc;
            try
            {
                doc = XDocument.Load(path, LoadOptions.SetLineInfo);
            }
            catch (XmlException e)
            {
                s.parseErrors.Add($"第 {e.LineNumber} 行：XML 格式错误（{e.Message}）。常见原因：引号没配对、标签没有用 /> 结尾、用了中文引号“”。");
                return s;
            }
            catch (Exception e)
            {
                s.parseErrors.Add("无法读取文件：" + e.Message);
                return s;
            }

            var root = doc.Root;
            var title = root.Attribute("名称")?.Value;
            if (!string.IsNullOrWhiteSpace(title)) s.name = title.Trim();

            int lastTick = 0;
            bool hasEnd = false;
            foreach (var el in root.Elements())
            {
                var cue = new Cue
                {
                    tag = el.Name.LocalName,
                    line = ((IXmlLineInfo)el).LineNumber,
                };
                foreach (var a in el.Attributes()) cue.attrs[a.Name.LocalName] = a.Value;

                if (!Commands.TryGetKind(cue.tag, out cue.kind))
                {
                    s.parseErrors.Add($"{cue.Where}：不认识这个指令。可用的指令有：{Commands.KnownTags}。");
                    continue;
                }
                if (cue.kind == CommandKind.Note) continue;

                if (cue.Has(TimeAttr))
                {
                    if (!TryParseSeconds(cue.Get(TimeAttr), out float sec) || sec < 0f)
                    {
                        s.parseErrors.Add($"{cue.Where}：时间 秒=\"{cue.Get(TimeAttr)}\" 写得不对。可以写 12、2.5 或 1:05（1 分 5 秒）。");
                        continue;
                    }
                    cue.tick = (int)Math.Round(sec * TicksPerSecond);
                }
                else
                {
                    cue.tick = lastTick; // 没写时间：和上一条同时
                }
                lastTick = cue.tick;

                foreach (var key in cue.attrs.Keys)
                {
                    if (key != TimeAttr && !Commands.AllowedAttrs(cue.kind).Contains(key))
                        s.parseErrors.Add($"{cue.Where}：不认识属性“{key}”。这个指令可用的属性：{string.Join("、", Commands.AllowedAttrs(cue.kind))}。");
                }

                if (cue.kind == CommandKind.End) hasEnd = true;
                s.cues.Add(cue);
            }

            // 按时间排序（同一时间保持书写顺序）
            s.cues = s.cues.OrderBy(c => c.tick).ToList();
            var endCue = s.cues.FirstOrDefault(c => c.kind == CommandKind.End);
            if (hasEnd)
                s.endTick = endCue.tick;
            else
                s.endTick = (s.cues.Count > 0 ? s.cues[s.cues.Count - 1].tick : 0) + 10 * TicksPerSecond;
            return s;
        }

        public static bool TryParseSeconds(string text, out float seconds)
        {
            seconds = 0f;
            text = text.Trim().Replace('：', ':');
            var parts = text.Split(':');
            if (parts.Length == 1)
                return float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out seconds);
            if (parts.Length == 2
                && int.TryParse(parts[0], out int min)
                && float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float sec))
            {
                seconds = min * 60f + sec;
                return true;
            }
            return false;
        }

        /// <summary>在开拍前检查整份剧本：语法 + 地图上的角色、坐标、天气是否存在。</summary>
        public List<string> Validate(Map map)
        {
            var errors = new List<string>(parseErrors);
            if (map == null)
            {
                errors.Add("当前没有打开的地图。");
                return errors;
            }
            if (cues.Count == 0 && parseErrors.Count == 0)
                errors.Add("剧本里没有任何指令。");
            foreach (var cue in cues)
            {
                try { Commands.Check(cue, map, errors); }
                catch (Exception e) { errors.Add($"{cue.Where}：检查时出错 {e.Message}"); }
            }
            return errors;
        }

        public string Duration => FormatTime(endTick);

        public static string FormatTime(int ticks)
        {
            float sec = ticks / (float)TicksPerSecond;
            int m = (int)(sec / 60f);
            return $"{m}:{sec - m * 60f:00.0}";
        }

        public static List<string> ListFiles(string dir)
        {
            if (!Directory.Exists(dir)) return new List<string>();
            return Directory.GetFiles(dir, "*.xml", SearchOption.AllDirectories)
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
        }
    }

    /// <summary>运行中的剧本：按时间逐条执行指令。</summary>
    public class ScriptRunner
    {
        public readonly Script script;
        public readonly Map map;
        readonly Stage stage;
        public int elapsed;
        int next;

        public ScriptRunner(Script script, Map map, Stage stage)
        {
            this.script = script;
            this.map = map;
            this.stage = stage;
        }

        public void Process()
        {
            while (next < script.cues.Count && script.cues[next].tick <= elapsed)
            {
                var cue = script.cues[next++];
                try
                {
                    Commands.Execute(cue, map, stage);
                }
                catch (Exception e)
                {
                    Log.Error($"[导演] {script.name} {cue.Where} 执行失败：{e}");
                }
                if (stage.runner != this) return; // 被 <结束> 停掉了
            }
            if (elapsed >= script.endTick && stage.runner == this) stage.FinishScript();
        }
    }
}
