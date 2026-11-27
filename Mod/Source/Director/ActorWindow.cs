using System.Collections.Generic;
using System.IO;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace MemorialDirector
{
    /// <summary>演员库：列出 Actors 文件夹里的演员卡，可以放到地图上，也可以把游戏里的角色存成演员卡。</summary>
    public class Dialog_ActorLibrary : Window
    {
        List<ActorCard> cards;
        Vector2 scroll;

        public override Vector2 InitialSize => new Vector2(640f, 560f);

        public Dialog_ActorLibrary()
        {
            doCloseX = true;
            forcePause = false;
            absorbInputAroundWindow = false;
            closeOnClickedOutside = false;
            Refresh();
        }

        void Refresh() => cards = ActorLibrary.LoadAll();

        public override void DoWindowContents(Rect inRect)
        {
            var l = new Listing_Standard();
            l.Begin(inRect);
            Text.Font = GameFont.Medium;
            l.Label("演员库");
            Text.Font = GameFont.Small;
            l.Label("演员卡存放在 Mod 的 Actors 文件夹。同一张卡每次生成的人一模一样。");
            l.GapLine();

            var row = l.GetRect(30f);
            float w = row.width / 3f - 4f;
            if (Widgets.ButtonText(new Rect(row.x, row.y, w, 30f), "刷新")) Refresh();
            if (Widgets.ButtonText(new Rect(row.x + w + 6f, row.y, w, 30f), "打开演员卡文件夹"))
            {
                Directory.CreateDirectory(ActorLibrary.Dir);
                Application.OpenURL("file:///" + ActorLibrary.Dir.Replace('\\', '/'));
            }
            if (Widgets.ButtonText(new Rect(row.x + 2 * (w + 6f), row.y, w, 30f), "选中角色存为演员卡"))
                ExportSelected();
            l.Gap(6f);

            var listRect = l.GetRect(inRect.height - l.CurHeight - 4f);
            var view = new Rect(0f, 0f, listRect.width - 16f, cards.Count * 34f);
            Widgets.BeginScrollView(listRect, ref scroll, view);
            for (int i = 0; i < cards.Count; i++)
            {
                var c = cards[i];
                var r = new Rect(0f, i * 34f, view.width, 32f);
                if (i % 2 == 0) Widgets.DrawLightHighlight(r);
                var problems = c.Check();
                string label = $"{c.DisplayName}（{c.FullName}，{(c.gender == Gender.Female ? "女" : "男")}，{c.age:0} 岁）";
                if (problems.Count > 0) label = "<color=#ff9a6a>⚠</color> " + label;
                Widgets.Label(new Rect(r.x + 6f, r.y + 6f, r.width - 220f, 26f), label);
                if (problems.Count > 0) TooltipHandler.TipRegion(r, string.Join("\n", problems));
                if (Widgets.ButtonText(new Rect(r.xMax - 210f, r.y + 2f, 100f, 28f), "放到地图上")) BeginPlace(c);
                if (Widgets.ButtonText(new Rect(r.xMax - 104f, r.y + 2f, 100f, 28f), "查看问题", active: problems.Count > 0) && problems.Count > 0)
                    Find.WindowStack.Add(new Dialog_MessageBox(string.Join("\n\n", problems)));
            }
            Widgets.EndScrollView();
            l.End();
        }

        static void BeginPlace(ActorCard c)
        {
            var map = Find.CurrentMap;
            if (map == null) return;
            var existing = Commands.FindPawnRaw(c.DisplayName, map, out _);
            var tp = new TargetingParameters { canTargetLocations = true, canTargetPawns = false, canTargetBuildings = false };
            Messages.Message($"点击地图上的一个格子，让“{c.DisplayName}”出现在那里（右键取消）。", MessageTypeDefOf.NeutralEvent, false);
            Find.Targeter.BeginTargeting(tp, t =>
            {
                if (!t.Cell.InBounds(map) || !t.Cell.Standable(map))
                {
                    Messages.Message("这个格子站不了人。", MessageTypeDefOf.RejectInput, false);
                    return;
                }
                if (existing != null && existing.Spawned) ActorFactory.Teleport(existing, t.Cell, Rot4.South);
                else ActorFactory.Spawn(c, map, t.Cell, Rot4.South);
            });
        }

        void ExportSelected()
        {
            var p = Find.Selector.SingleSelectedThing as Pawn;
            if (p == null || !p.RaceProps.Humanlike)
            {
                Messages.Message("先在地图上单独选中一个人，再点这个按钮。", MessageTypeDefOf.RejectInput, false);
                return;
            }
            string name = (p.Name is NameTriple t ? t.Nick : null) ?? p.LabelShort;
            foreach (var ch in Path.GetInvalidFileNameChars()) name = name.Replace(ch, '_');
            string path = Path.Combine(ActorLibrary.Dir, name + ".xml");
            int seed = Rand.Range(1, 999999);
            var old = cards.FirstOrDefault(c => c.Matches(name));
            if (old != null)
            {
                seed = old.seed;
                path = old.path;
            }

            void Write()
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                ActorCard.FromPawn(p, seed).Save(path);
                Messages.Message($"已保存演员卡：{path}", MessageTypeDefOf.PositiveEvent, false);
                Refresh();
            }

            if (File.Exists(path))
                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                    $"演员卡“{Path.GetFileName(path)}”已经存在。要用“{p.LabelShort}”现在的样子覆盖它吗？", Write, destructive: true));
            else Write();
        }
    }
}
