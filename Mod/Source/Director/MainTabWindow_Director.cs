using System.Collections.Generic;
using System.IO;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace MemorialDirector
{
    /// <summary>屏幕底部“导演”按钮打开的面板。</summary>
    public class MainTabWindow_Director : MainTabWindow
    {
        static DirectorSettings S => DirectorMod.Settings;

        List<string> files = new List<string>();
        Vector2 scroll;

        public override Vector2 RequestedTabSize => new Vector2(620f, 520f);

        public override void PreOpen()
        {
            base.PreOpen();
            Refresh();
        }

        void Refresh() => files = Script.ListFiles(DirectorMod.Instance.ScriptsDir);

        static string Relative(string path)
        {
            var root = DirectorMod.Instance.ScriptsDir;
            return path.StartsWith(root) ? path.Substring(root.Length).TrimStart('/', '\\') : path;
        }

        public override void DoWindowContents(Rect inRect)
        {
            var stage = Stage.Instance;
            var l = new Listing_Standard();
            l.Begin(inRect);

            Text.Font = GameFont.Medium;
            l.Label("导演面板");
            Text.Font = GameFont.Small;

            if (stage != null && stage.Running)
            {
                var r = stage.runner;
                l.Label($"正在拍摄：{r.script.name}    {Script.FormatTime(r.elapsed)} / {r.script.Duration}");
                if (l.ButtonText("停止（快捷键 F8）")) stage.StopScript(true);
            }
            else
            {
                l.Label("当前没有在拍摄。选中一个剧本，先点“检查”，没问题再点“开拍”。");
            }
            l.GapLine();

            var row = l.GetRect(30f);
            var w = row.width / 3f - 4f;
            if (Widgets.ButtonText(new Rect(row.x, row.y, w, 30f), "刷新列表")) Refresh();
            if (Widgets.ButtonText(new Rect(row.x + w + 6f, row.y, w, 30f), "打开剧本文件夹"))
            {
                Directory.CreateDirectory(DirectorMod.Instance.ScriptsDir);
                Application.OpenURL("file:///" + DirectorMod.Instance.ScriptsDir.Replace('\\', '/'));
            }
            if (Widgets.ButtonText(new Rect(row.x + 2 * (w + 6f), row.y, w, 30f), "拍摄模式设置"))
                Find.WindowStack.Add(new Dialog_DirectorSettings());
            l.Gap(4f);
            if (l.ButtonText("演员库（登场 / 存为演员卡）"))
            {
                Close();
                Find.WindowStack.Add(new Dialog_ActorLibrary());
            }
            l.Gap(6f);

            l.Label($"剧本（{files.Count} 个）：");
            var listRect = l.GetRect(inRect.height - l.CurHeight - 70f);
            var view = new Rect(0f, 0f, listRect.width - 16f, files.Count * 28f);
            Widgets.BeginScrollView(listRect, ref scroll, view);
            for (int i = 0; i < files.Count; i++)
            {
                var f = files[i];
                var rr = new Rect(0f, i * 28f, view.width, 26f);
                bool selected = f == S.lastScript;
                if (selected) Widgets.DrawHighlightSelected(rr);
                else if (Mouse.IsOver(rr)) Widgets.DrawHighlight(rr);
                Widgets.Label(rr.ContractedBy(4f, 2f), Relative(f));
                if (Widgets.ButtonInvisible(rr))
                {
                    S.lastScript = f;
                    S.Write();
                }
            }
            Widgets.EndScrollView();
            l.Gap(6f);

            bool hasSel = files.Contains(S.lastScript);
            var row2 = l.GetRect(34f);
            var w2 = row2.width / 2f - 3f;
            if (Widgets.ButtonText(new Rect(row2.x, row2.y, w2, 34f), "检查剧本", active: hasSel) && hasSel)
                CheckScript(S.lastScript);
            if (Widgets.ButtonText(new Rect(row2.x + w2 + 6f, row2.y, w2, 34f), "开拍（快捷键 F7）", active: hasSel) && hasSel)
            {
                Close();
                StartScript(S.lastScript);
            }

            l.End();
        }

        static void CheckScript(string path)
        {
            var script = Script.Load(path);
            var errors = script.Validate(Find.CurrentMap);
            if (errors.Count == 0)
            {
                Find.WindowStack.Add(new Dialog_MessageBox(
                    $"“{script.name}”检查通过。\n\n共 {script.cues.Count} 条指令，时长 {script.Duration}。"));
            }
            else
            {
                ShowErrors(script, errors);
            }
        }

        static void ShowErrors(Script script, List<string> errors)
        {
            var shown = errors.Take(25).ToList();
            var text = $"“{script.name}”有 {errors.Count} 个问题：\n\n" + string.Join("\n\n", shown);
            if (errors.Count > shown.Count) text += $"\n\n……还有 {errors.Count - shown.Count} 个问题没有显示。";
            Find.WindowStack.Add(new Dialog_MessageBox(text));
        }

        public static void StartScript(string path)
        {
            var stage = Stage.Instance;
            var map = Find.CurrentMap;
            if (stage == null || map == null) return;
            var script = Script.Load(path);
            if (!stage.StartScript(script, map, out var errors))
            {
                ShowErrors(script, errors);
                return;
            }
            Log.Message($"[导演] 开拍：{script.name}（{script.cues.Count} 条指令，时长 {script.Duration}）");
        }

        public static void StartLastScript()
        {
            var path = DirectorMod.Settings.lastScript;
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                Messages.Message("[导演] 还没有选中剧本：先在底部“导演”面板里点一下要拍的剧本。", MessageTypeDefOf.RejectInput, false);
                return;
            }
            StartScript(path);
        }
    }

    public class Dialog_DirectorSettings : Window
    {
        public override Vector2 InitialSize => new Vector2(640f, 420f);

        public Dialog_DirectorSettings()
        {
            doCloseX = true;
            doCloseButton = true;
            forcePause = false;
        }

        public override void DoWindowContents(Rect inRect)
        {
            var l = new Listing_Standard();
            l.Begin(inRect);
            DirectorMod.Settings.DoSettingsList(l);
            l.End();
        }

        public override void PostClose()
        {
            base.PostClose();
            DirectorMod.Settings.Write();
        }
    }
}
