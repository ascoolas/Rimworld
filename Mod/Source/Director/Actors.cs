using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace MemorialDirector
{
    /// <summary>
    /// 演员卡：一个角色的长期“定妆照”，存成 Mod/Actors/名字.xml。
    /// 同一张卡每次生成的人长得完全一样（外貌、服装、伤残），可在任何存档里反复使用。
    /// </summary>
    public class ActorCard
    {
        public string path;
        public string first, last, nick;
        public Gender gender = Gender.Male;
        public float age = 25f;
        public int seed = 1;

        public string bodyType, headType, hair, beard;
        public string childhood, adulthood;
        public Color? hairColor, skinColor;

        public class Item { public string def, stuff; public Color? color; }
        public List<Item> apparel = new List<Item>();
        public Item weapon;

        public class Health { public string def, part; }
        public List<Health> health = new List<Health>();

        public List<string> errors = new List<string>();

        public string DisplayName => !string.IsNullOrEmpty(nick) ? nick : first;

        public bool Matches(string name)
        {
            name = name?.Trim();
            if (string.IsNullOrEmpty(name)) return false;
            bool Eq(string s) => !string.IsNullOrEmpty(s) && string.Equals(s.Trim(), name, StringComparison.OrdinalIgnoreCase);
            return Eq(nick) || Eq(first) || Eq(last) || Eq(FullName);
        }

        public string FullName => string.IsNullOrEmpty(last) ? first : first + "·" + last;

        // ---------------- 读取 ----------------

        public static ActorCard Load(string path)
        {
            var c = new ActorCard { path = path };
            XElement root;
            try { root = XDocument.Load(path).Root; }
            catch (Exception e)
            {
                c.errors.Add($"{Path.GetFileName(path)}：XML 格式错误（{e.Message}）");
                c.first = Path.GetFileNameWithoutExtension(path);
                return c;
            }
            string A(XElement el, string k) => el?.Attribute(k)?.Value?.Trim().NullIfEmpty();

            c.first = A(root, "名") ?? Path.GetFileNameWithoutExtension(path);
            c.last = A(root, "姓");
            c.nick = A(root, "昵称") ?? c.first;
            var g = A(root, "性别");
            c.gender = g == "女" || g == "Female" ? Gender.Female : Gender.Male;
            c.childhood = A(root, "童年");
            c.adulthood = A(root, "成年");
            if (A(root, "年龄") is string ageText && float.TryParse(ageText, NumberStyles.Float, CultureInfo.InvariantCulture, out var age)) c.age = age;
            if (A(root, "种子") is string seedText && int.TryParse(seedText, out var seed)) c.seed = seed;
            else c.seed = c.first.GetHashCode();

            var look = root.Element("外貌");
            c.bodyType = A(look, "体型");
            c.headType = A(look, "头型");
            c.hair = A(look, "发型");
            c.beard = A(look, "胡子");
            c.hairColor = ParseColor(A(look, "发色"));
            c.skinColor = ParseColor(A(look, "肤色"));

            foreach (var el in root.Elements("服装"))
                c.apparel.Add(new Item { def = A(el, "物品"), stuff = A(el, "材料"), color = ParseColor(A(el, "颜色")) });
            var w = root.Element("武器");
            if (w != null && A(w, "物品") != null)
                c.weapon = new Item { def = A(w, "物品"), stuff = A(w, "材料"), color = ParseColor(A(w, "颜色")) };
            foreach (var el in root.Elements("伤病"))
                c.health.Add(new Health { def = A(el, "类型"), part = A(el, "部位") });
            return c;
        }

        static Color? ParseColor(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            var p = text.Replace('，', ',').Split(',');
            if (p.Length < 3) return null;
            float F(string s) => float.Parse(s.Trim(), CultureInfo.InvariantCulture);
            try { return new Color(F(p[0]), F(p[1]), F(p[2]), p.Length > 3 ? F(p[3]) : 1f); }
            catch { return null; }
        }

        static string ColorText(Color c) =>
            string.Format(CultureInfo.InvariantCulture, "{0:0.###},{1:0.###},{2:0.###}", c.r, c.g, c.b);

        /// <summary>检查卡片里写的东西在当前游戏（含已装 DLC / Mod）里是否存在。</summary>
        public List<string> Check()
        {
            var errs = new List<string>(errors);
            string who = $"演员卡“{DisplayName}”";
            if (bodyType != null && DefDatabase<BodyTypeDef>.GetNamedSilentFail(bodyType) == null) errs.Add($"{who}：找不到体型 {bodyType}");
            if (childhood != null && DefDatabase<BackstoryDef>.GetNamedSilentFail(childhood) == null) errs.Add($"{who}：找不到童年背景 {childhood}");
            if (adulthood != null && DefDatabase<BackstoryDef>.GetNamedSilentFail(adulthood) == null) errs.Add($"{who}：找不到成年背景 {adulthood}");
            if (headType != null && DefDatabase<HeadTypeDef>.GetNamedSilentFail(headType) == null) errs.Add($"{who}：找不到头型 {headType}");
            if (hair != null && DefDatabase<HairDef>.GetNamedSilentFail(hair) == null) errs.Add($"{who}：找不到发型 {hair}");
            if (beard != null && DefDatabase<BeardDef>.GetNamedSilentFail(beard) == null) errs.Add($"{who}：找不到胡子 {beard}");
            foreach (var a in apparel.Concat(weapon != null ? new[] { weapon } : new Item[0]))
            {
                if (ActorFactory.PickDef(a.def) == null) errs.Add($"{who}：找不到物品 {a.def}");
                if (a.stuff != null && DefDatabase<ThingDef>.GetNamedSilentFail(a.stuff) == null) errs.Add($"{who}：找不到材料 {a.stuff}");
            }
            foreach (var h in health)
                if (DefDatabase<HediffDef>.GetNamedSilentFail(h.def ?? "") == null) errs.Add($"{who}：找不到伤病类型 {h.def}");
            return errs;
        }

        // ---------------- 导出 ----------------

        /// <summary>把游戏里一个角色的外貌、服装、伤残写成演员卡。</summary>
        public static XDocument FromPawn(Pawn p, int seed)
        {
            var root = new XElement("演员");
            if (p.Name is NameTriple t)
            {
                root.SetAttributeValue("名", t.First);
                if (!string.IsNullOrEmpty(t.Last)) root.SetAttributeValue("姓", t.Last);
                root.SetAttributeValue("昵称", t.Nick);
            }
            else root.SetAttributeValue("名", p.LabelShort);
            root.SetAttributeValue("性别", p.gender == Gender.Female ? "女" : "男");
            root.SetAttributeValue("年龄", p.ageTracker.AgeBiologicalYears);
            root.SetAttributeValue("种子", seed);
            if (p.story?.Childhood != null) root.SetAttributeValue("童年", p.story.Childhood.defName);
            if (p.story?.Adulthood != null) root.SetAttributeValue("成年", p.story.Adulthood.defName);

            var look = new XElement("外貌");
            if (p.story != null)
            {
                if (p.story.bodyType != null) look.SetAttributeValue("体型", p.story.bodyType.defName);
                if (p.story.headType != null) look.SetAttributeValue("头型", p.story.headType.defName);
                if (p.story.hairDef != null) look.SetAttributeValue("发型", p.story.hairDef.defName);
                look.SetAttributeValue("发色", ColorText(p.story.HairColor));
                look.SetAttributeValue("肤色", ColorText(p.story.SkinColor));
            }
            if (p.style?.beardDef != null) look.SetAttributeValue("胡子", p.style.beardDef.defName);
            root.Add(look);

            foreach (var ap in p.apparel?.WornApparel ?? new List<Apparel>())
            {
                var el = new XElement("服装");
                el.SetAttributeValue("物品", ap.def.defName);
                if (ap.Stuff != null) el.SetAttributeValue("材料", ap.Stuff.defName);
                if (ap.TryGetComp<CompColorable>() is CompColorable cc && cc.Active) el.SetAttributeValue("颜色", ColorText(ap.DrawColor));
                root.Add(el);
            }
            if (p.equipment?.Primary is ThingWithComps eq)
            {
                var el = new XElement("武器");
                el.SetAttributeValue("物品", eq.def.defName);
                if (eq.Stuff != null) el.SetAttributeValue("材料", eq.Stuff.defName);
                root.Add(el);
            }
            foreach (var h in p.health.hediffSet.hediffs)
            {
                if (h.Part == null) continue;
                if (h is Hediff_MissingPart && h.Part.parent != null && p.health.hediffSet.PartIsMissing(h.Part.parent)) continue;
                if (!(h is Hediff_MissingPart) && !(h is Hediff_AddedPart)) continue;
                var el = new XElement("伤病");
                el.SetAttributeValue("类型", h.def.defName);
                el.SetAttributeValue("部位", ActorFactory.PartName(h.Part));
                root.Add(el);
            }
            return new XDocument(new XDeclaration("1.0", "utf-8", null), root);
        }
    }

    public static class ActorLibrary
    {
        public static string Dir => Path.Combine(DirectorMod.Instance.Content.RootDir, "Actors");

        public static List<ActorCard> LoadAll()
        {
            if (!Directory.Exists(Dir)) return new List<ActorCard>();
            return Directory.GetFiles(Dir, "*.xml", SearchOption.AllDirectories)
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .Select(ActorCard.Load).ToList();
        }

        public static ActorCard Find(string name) => LoadAll().FirstOrDefault(c => c.Matches(name));
    }

    public static class ActorFactory
    {
        static readonly Dictionary<string, string> partAlias = new Dictionary<string, string>
        {
            { "左手", "left hand" }, { "右手", "right hand" },
            { "左臂", "left arm" }, { "右臂", "right arm" },
            { "左肩", "left shoulder" }, { "右肩", "right shoulder" },
            { "左腿", "left leg" }, { "右腿", "right leg" },
            { "左脚", "left foot" }, { "右脚", "right foot" },
            { "左眼", "left eye" }, { "右眼", "right eye" },
            { "左耳", "left ear" }, { "右耳", "right ear" },
        };

        /// <summary>物品可以写成 A|B|C：用第一个在当前游戏里存在的（没装某个 DLC 时自动用备选）。</summary>
        public static ThingDef PickDef(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            foreach (var name in text.Split('|'))
            {
                var d = DefDatabase<ThingDef>.GetNamedSilentFail(name.Trim());
                if (d != null) return d;
            }
            return null;
        }

        public static string PartName(BodyPartRecord part) =>
            !string.IsNullOrEmpty(part.untranslatedCustomLabel) ? part.untranslatedCustomLabel : part.def.defName;

        public static BodyPartRecord FindPart(Pawn p, string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            name = name.Trim();
            if (partAlias.TryGetValue(name, out var en)) name = en;
            var parts = p.RaceProps.body.AllParts;
            return parts.FirstOrDefault(x => string.Equals(x.untranslatedCustomLabel, name, StringComparison.OrdinalIgnoreCase))
                ?? parts.FirstOrDefault(x => string.Equals(x.def.defName, name, StringComparison.OrdinalIgnoreCase))
                ?? parts.FirstOrDefault(x => x.Label == name);
        }

        /// <summary>按演员卡生成一个角色（还没有放到地图上）。</summary>
        public static Pawn Create(ActorCard c)
        {
            DevelopmentalStage stage = c.age < 3f ? DevelopmentalStage.Baby
                : c.age < 13f ? DevelopmentalStage.Child : DevelopmentalStage.Adult;
            var req = new PawnGenerationRequest(
                PawnKindDefOf.Colonist, Faction.OfPlayer, PawnGenerationContext.NonPlayer,
                forceGenerateNewPawn: true,
                canGeneratePawnRelations: false,
                colonistRelationChanceFactor: 0f,
                allowPregnant: false,
                allowFood: false,
                allowAddictions: false,
                fixedBiologicalAge: c.age,
                fixedChronologicalAge: c.age,
                fixedGender: c.gender,
                forbidAnyTitle: true,
                forcedXenotype: ModsConfig.BiotechActive ? XenotypeDefOf.Baseliner : null,
                developmentalStages: stage,
                dontGiveWeapon: true,
                forceNoGear: true);

            Pawn p;
            Rand.PushState(c.seed); // 固定随机种子：卡里没写的部分（脸、发型等）每次也一样
            try { p = PawnGenerator.GeneratePawn(req); }
            finally { Rand.PopState(); }

            p.Name = new NameTriple(c.first, c.nick ?? c.first, c.last ?? "");

            // 背景故事（童年 / 成年）：可以写本 Mod 的 MD_ 背景，也可以写游戏自带背景的 defName
            if (c.childhood != null && DefDatabase<BackstoryDef>.GetNamedSilentFail(c.childhood) is BackstoryDef ch) p.story.Childhood = ch;
            if (c.adulthood != null && DefDatabase<BackstoryDef>.GetNamedSilentFail(c.adulthood) is BackstoryDef ad) p.story.Adulthood = ad;

            // 去掉随机特性和随机伤病（特性会影响走路速度，伤病会影响外观）
            foreach (var t in p.story.traits.allTraits.ToList()) p.story.traits.RemoveTrait(t);
            foreach (var h in p.health.hediffSet.hediffs.ToList()) p.health.RemoveHediff(h);

            if (c.bodyType != null && DefDatabase<BodyTypeDef>.GetNamedSilentFail(c.bodyType) is BodyTypeDef bt) p.story.bodyType = bt;
            if (c.headType != null && DefDatabase<HeadTypeDef>.GetNamedSilentFail(c.headType) is HeadTypeDef ht) p.story.headType = ht;
            if (c.hair != null && DefDatabase<HairDef>.GetNamedSilentFail(c.hair) is HairDef hd) p.story.hairDef = hd;
            if (c.hairColor.HasValue) p.story.HairColor = c.hairColor.Value;
            if (c.skinColor.HasValue) p.story.skinColorOverride = c.skinColor.Value;
            if (p.style != null)
            {
                if (c.beard != null && DefDatabase<BeardDef>.GetNamedSilentFail(c.beard) is BeardDef bd) p.style.beardDef = bd;
                else if (c.beard != null || c.gender == Gender.Female) p.style.beardDef = BeardDefOf.NoBeard;
            }

            p.apparel?.DestroyAll();
            p.equipment?.DestroyAllEquipment();
            p.inventory?.DestroyAll();
            foreach (var a in c.apparel)
            {
                var thing = MakeItem(a, c);
                if (thing is Apparel ap) p.apparel.Wear(ap, false, true);
            }
            if (c.weapon != null && MakeItem(c.weapon, c) is ThingWithComps w) p.equipment.AddEquipment(w);

            foreach (var h in c.health)
            {
                var def = DefDatabase<HediffDef>.GetNamedSilentFail(h.def ?? "");
                if (def == null) { Log.Warning($"[导演] 演员卡“{c.DisplayName}”：找不到伤病类型 {h.def}"); continue; }
                var part = FindPart(p, h.part);
                if (h.part != null && part == null) { Log.Warning($"[导演] 演员卡“{c.DisplayName}”：找不到身体部位 {h.part}"); continue; }
                if (def == HediffDefOf.MissingBodyPart)
                {
                    var missing = (Hediff_MissingPart)HediffMaker.MakeHediff(def, p, part);
                    missing.IsFresh = false; // 旧伤，不流血
                    p.health.AddHediff(missing, part);
                }
                else
                {
                    if (part != null && typeof(Hediff_AddedPart).IsAssignableFrom(def.hediffClass) && p.health.hediffSet.PartIsMissing(part))
                        p.health.RestorePart(part, null, true);
                    p.health.AddHediff(def, part);
                }
            }
            p.Notify_DisabledWorkTypesChanged();
            return p;
        }

        static Thing MakeItem(ActorCard.Item a, ActorCard c)
        {
            var def = PickDef(a.def);
            if (def == null) { Log.Warning($"[导演] 演员卡“{c.DisplayName}”：找不到物品 {a.def}"); return null; }
            ThingDef stuff = null;
            if (def.MadeFromStuff)
                stuff = (a.stuff != null ? DefDatabase<ThingDef>.GetNamedSilentFail(a.stuff) : null) ?? GenStuff.DefaultStuffFor(def);
            var thing = ThingMaker.MakeThing(def, stuff);
            if (a.color.HasValue) thing.SetColor(a.color.Value, false);
            thing.TryGetComp<CompQuality>()?.SetQuality(QualityCategory.Normal, ArtGenerationContext.Colony);
            return thing;
        }

        public static Pawn Spawn(ActorCard c, Map map, IntVec3 cell, Rot4 rot)
        {
            var p = Create(c);
            GenSpawn.Spawn(p, cell, map, rot);
            p.Drawer.renderer.SetAllGraphicsDirty();
            PortraitsCache.SetDirty(p);
            return p;
        }

        /// <summary>把角色移到某个格子（瞬移），用于“登场”时角色已经在地图上的情况。</summary>
        public static void Teleport(Pawn p, IntVec3 cell, Rot4 rot)
        {
            p.jobs?.StopAll();
            p.pather?.StopDead();
            p.Position = cell;
            p.Notify_Teleported();
            if (rot.IsValid) p.Rotation = rot;
        }

        public static void Remove(Pawn p)
        {
            if (p.Spawned) p.DeSpawn();
            if (!Find.WorldPawns.Contains(p)) Find.WorldPawns.PassToWorld(p, RimWorld.Planet.PawnDiscardDecideMode.Discard);
        }
    }

    internal static class StringExt
    {
        public static string NullIfEmpty(this string s) => string.IsNullOrWhiteSpace(s) ? null : s;
    }
}
