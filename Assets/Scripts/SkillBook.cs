using System.Collections.Generic;
using UnityEngine;
using MapleDash.Wz;

// ————————————————————————————————————————————————————————————
// 技能书：把 Skill.wz 的整棵职业技能树搬到运行时。
//
// 数据来自 tools/export_jobs.py 导出的 UI/JobTree.json：
//   jobs[]             —— 63 个职业（冒险家 42 + 骑士团 21），每个职业一条技能线
//   jobs[].skills      —— 技能窗列表（旧 6 条线沿用已验证 tab，新职业列该 img 全部技能）
//   jobs[].allSkills   —— 该职业全部技能（含被动/隐藏）
//   skills[<id>]       —— 中文名、说明、上限等级、类型、前置、逐级数值、图标
// 逐级数值是 WZ 里 level/N 的原始字段（mpCon / damage / attackCount / cooltime …），
// 没有任何本地估算。
//
// 技能点：源端 level_up_sp_gain=3，且 levelUpGainSp() 对新手直接返回（新手无 SP）；
// 一转起 SP 是存档累计字段（Profile.sp），转职另按 JobSystem 授予。
// ————————————————————————————————————————————————————————————

public sealed class SkillData
{
    public int id, maxLevel, masterLevel, job;
    public string name, desc, type, icon;
    public bool magic;
    public bool noName;       // 源端 String.wz 缺中文名（五六转 V 技组件），UI 回退显示 id
    public bool autoLearn;   // 不是手动加点的技能（如精灵的祝福，按账号等级自动习得）
    public bool hexa;        // 六转技能：等级由 HEXA 矩阵提升，不吃 SP（282 口径）
    public List<int[]> req = new List<int[]>();      // [前置技能id, 需要等级]
    // 五转共通技能里「伤害按职业分流」的那几条（艾尔达斯的降临/蜘蛛之镜/烈阳印记）：
    // 282 把 damage/attackCount 放在 common/attackInfo/<四转职业ID>/ 下，common 顶层没有 damage。
    // 形状 { 职业: { 等级: { damage, attackCount, … } } }，取行见 SkillBook.JobAttackRow。
    public Dictionary<string, object> attackInfo;
    public Dictionary<string, object> levels = new Dictionary<string, object>();

    /// <summary>
    /// V/HEXA 矩阵 tooltip 专用的局部副本。核心名/说明的权威是 VCore.img /
    /// HexaCore.img（VMatrix.json / HexaMatrix.json），不是 SkillBook 里那条可能被
    /// 别的界面复用的技能定义。旧实现对 SkillBook.Get() 返回的共享单例直接写
    /// data.name = 核心名，一次悬停就把名字钉死在 byId 里，之后技能窗、快捷栏、
    /// 状态栏全部跟着串位 —— 这里只做副本，绝不回写共享状态。
    /// </summary>
    public SkillData ForTooltip(string title, string detail) => new SkillData
    {
        id = id, maxLevel = maxLevel, masterLevel = masterLevel, job = job,
        name = string.IsNullOrEmpty(title) ? name : title,
        desc = string.IsNullOrEmpty(detail) ? desc : detail,
        type = type, icon = icon, magic = magic, noName = false,
        autoLearn = autoLearn, hexa = hexa,
        req = req, attackInfo = attackInfo, levels = levels,
    };

    public Dictionary<string, object> Level(int lv)
    {
        if (lv < 1) lv = 1;
        if (lv > maxLevel) lv = maxLevel;
        return levels.TryGetValue(lv.ToString(), out var row)
            ? row as Dictionary<string, object> : null;
    }

    public bool IsAttack => type == "attack";
    public bool IsBuff => type == "buff" && SkillBook.SpecialKind(id)==null;
    public bool IsSummon => type == "summon";
    public bool IsPassive => type == "passive" && !SkillSystem.InstantSelfSkill(id) && SkillBook.SpecialKind(id)==null;
}

public static class SkillBook
{
    // StatEffect.isCrash/loadFromData: all three share SEAL_SKILL + debuffMob.
    public static string SpecialKind(int id) => id==1111007 || id==1211009 || id==1311007 ? "Dispel" :
        NativeTeleportMovement.IsSkill(id) ? "Teleport" :
        id==4121003 || id==4221003 ? "Attack" :
        id==4111006 || id==14101004 || id==11101005 ? "FlashJump" : null;
    public static bool Ready { get; private set; }

    static readonly Dictionary<int, SkillData> byId = new Dictionary<int, SkillData>();
    static readonly List<Dictionary<string, object>> lines = new List<Dictionary<string, object>>();
    static readonly Dictionary<int, Dictionary<string, object>> lineByJob = new Dictionary<int, Dictionary<string, object>>();
    static Dictionary<string, object> jobNames = new Dictionary<string, object>();
    // 核心技能的中文名/说明：项目自己的 String/Skill.img 只有四转那 960 条，五转 V 核心
    // 连名字都没有（400011006 惡魔覺醒 之类），界面上只能显示 id。282 的 String/Skill.img
    // 有全量条目，导出成 VMatrixSkills.json 的 names 段，Get() 直读 IMG 时按 id 兜底。
    static readonly Dictionary<int, string> matrixNames = new Dictionary<int, string>();
    static readonly Dictionary<int, string> matrixDescs = new Dictionary<int, string>();

    public static void Ensure()
    {
        if (Ready) return;
        Ready = true;
        var asset = Resources.Load<TextAsset>("UI/JobTree");
        if (asset == null) { Debug.LogError("[SkillBook] 缺少 UI/JobTree.json"); return; }
        var root = MiniJson.Parse(asset.text) as Dictionary<string, object>;
        if (root == null) { Debug.LogError("[SkillBook] JobTree.json 解析失败"); return; }

        // skills 是 JSON 对象（id → 数据），MiniJson 解析成 Dictionary
        foreach (var pair in (Dictionary<string, object>)root["skills"])
        {
            var d = pair.Value as Dictionary<string, object>;
            var s = new SkillData
            {
                // 技能号 4 亿级，必须用 Art.Id 走 long；Art.Num 走 float 会把
                // 400011088 舍成 400011072，byId 互相覆盖（见 Art.Long 注释）。
                id = Art.Id(d, "id"),
                maxLevel = Mathf.Max(1, (int)Art.Num(d, "maxLevel", 1)),
                masterLevel = (int)Art.Num(d, "masterLevel"),
                job = (int)Art.Num(d, "job"),
                name = d.TryGetValue("name", out var n) ? n as string : "",
                desc = d.TryGetValue("desc", out var de) ? de as string : "",
                type = d.TryGetValue("type", out var t) ? t as string : "attack",
                icon = d.TryGetValue("icon", out var ic) ? ic as string : "",
            };
            s.levels = d.TryGetValue("levels", out var lv) ? lv as Dictionary<string, object> : s.levels;
            s.autoLearn = d.TryGetValue("auto", out var au) && au != null &&
                          au.ToString().ToLowerInvariant() == "true";
            s.hexa = d.TryGetValue("hexa", out var hx) && hx != null &&
                     hx.ToString().ToLowerInvariant() == "true";
            // 源端 String.wz 缺名字的技能（五六转 V 技组件）：保留 id 文本，UI 不显示空白行
            s.noName = string.IsNullOrEmpty(s.name);
            if (s.noName) s.name = s.id.ToString();
            // 前置技能：[技能id, 需要等级]，客户端加点的硬性条件（服务端不校验）
            if (d.TryGetValue("req", out var rq) && rq is List<object> reqList)
                foreach (var item in reqList)
                    if (item is List<object> reqPair && reqPair.Count >= 2)
                        s.req.Add(new[] { int.Parse(reqPair[0].ToString()), int.Parse(reqPair[1].ToString()) });
            // Extended physical skills also carry mad. The compat dispatch, not
            // the presence of that field, selects their physical/magic handler.
            // V/HEXA 反过来：526 条没有一条带 mad，法师的 V 技能靠 282 的
            // info/magicDamage=1 标记（导出层写成 magic:true），只看 mad 会
            // 让火毒/冰雷/主教拿物理攻击力结算。
            if (d.TryGetValue("magic", out var magicFlag) && magicFlag is bool magicTrue && magicTrue)
                s.magic = true;
            foreach (var row in s.levels.Values)
                if (row is Dictionary<string, object> rd && rd.ContainsKey("mad")) { s.magic = true; break; }
            s.magic = MagicDamageOf(s.id, s.magic);
            byId[s.id] = s;
        }

        // JobTree 的 jobs[] 是「一职业一线」；合成旧结构（line.tabs[0] = 该职业全部技能），
        // 让技能窗/推荐技能等既有消费方不用改数据形状。
        foreach (var item in (List<object>)root["jobs"])
        {
            if (!(item is Dictionary<string, object> d)) continue;
            int id = Art.Id(d, "id");
            var skillIds = new List<object>();
            if (d.TryGetValue("skills", out var sk) && sk is List<object> skList) skillIds = skList;

            var tab = new Dictionary<string, object>
            {
                { "job", id },
                { "name", d.TryGetValue("name", out var jn) ? jn as string : "" },
                { "book", d.TryGetValue("book", out var bk) ? bk as string : "" },
                { "skills", skillIds },
            };
            var line = new Dictionary<string, object>
            {
                { "id", id },
                { "name", d.TryGetValue("name", out var ln) ? ln as string : "" },
                { "book", d.TryGetValue("book", out var lb) ? lb as string : "" },
                { "tabs", new List<object> { tab } },
            };
            lines.Add(line);
            lineByJob[id] = line;
        }

        if (root.TryGetValue("jobNames", out var jn2)) jobNames = jn2 as Dictionary<string, object>;

        LoadMatrixSkills();
    }

    /// <summary>
    /// 五转（V 核心）/六转核心连接的技能：JobTree 只导出四转与 xxxVI 两条线，
    /// 400001xxx / 40002xxxx 这些核心技能一条都没有，于是「核心能强化、却绑不上快捷栏也放不出来」。
    /// 这里读 tools/ms282/export_vhexa_skills.py 导出的 UI/VMatrixSkills.json
    /// （282 定义层 common 公式按 1..maxLevel 展开，与 JobTree 同一口径），只补缺不覆盖。
    /// 等级仍由 NativeMatrixSkills 投影到 skills 表 → Profile，SkillProgress 不用另开分支。
    /// </summary>
    static void LoadMatrixSkills()
    {
        var asset = Resources.Load<TextAsset>("UI/VMatrixSkills");
        if (asset == null) { Debug.LogWarning("[SkillBook] 缺少 UI/VMatrixSkills.json，五转核心技能不可施放"); return; }
        var root = MiniJson.Parse(asset.text) as Dictionary<string, object>;
        if (root == null || !root.TryGetValue("skills", out var raw) || !(raw is Dictionary<string, object> table))
        { Debug.LogWarning("[SkillBook] VMatrixSkills.json 解析失败"); return; }

        foreach (var pair in table)
        {
            if (!(pair.Value is Dictionary<string, object> d)) continue;
            int id = Art.Id(d, "id");
            // JobTree 已收录的同 id（1141000 这类 xxxVI）以 JobTree 为准，不让两套导出分叉。
            if (id == 0) continue;
            // V/HEXA definitions are authoritative in this export.  Do not let
            // an older JobTree/cache entry keep a stale type/name/icon for the
            // same connection skill (that makes a cross icon behave like Steel
            // Body, or turns an active V skill into a passive one).
            if (byId.TryGetValue(id, out var existing) && id < 400000000)
            {
                // 但 282 的 info/magicDamage 标记只写在这份 VMatrixSkills 里（JobTree 那条
                // 来自另一条导出链，没有 magic 字段）。不补的话，2141000 火焰之袭VI、2341000
                // 天使之箭VI 这些法师 HEXA 技能又会被判成物理，拿物理攻击力结算。
                if (d.TryGetValue("magic", out var magicOn) && magicOn is bool magicOnTrue && magicOnTrue)
                    existing.magic = true;
                continue;
            }
            var s = new SkillData
            {
                id = id,
                maxLevel = Mathf.Max(1, (int)Art.Num(d, "maxLevel", 1)),
                masterLevel = (int)Art.Num(d, "masterLevel"),
                job = (int)Art.Num(d, "job"),
                name = d.TryGetValue("name", out var n) ? n as string : "",
                desc = d.TryGetValue("desc", out var de) ? de as string : "",
                type = d.TryGetValue("type", out var t) ? t as string : "attack",
                icon = d.TryGetValue("icon", out var ic) ? ic as string : "",
                hexa = true,   // 不吃 SP：等级只跟核心等级走
            };
            s.levels = d.TryGetValue("levels", out var lv) ? lv as Dictionary<string, object> : s.levels;
            // 按职业分流的伤害表（只有艾尔达斯的降临/蜘蛛之镜/烈阳印记这三条带）
            if (d.TryGetValue("attackInfo", out var ai) && ai is Dictionary<string, object> aiTable)
                s.attackInfo = aiTable;
            s.noName = string.IsNullOrEmpty(s.name);
            if (s.noName) s.name = id.ToString();
            if (d.TryGetValue("req", out var rq) && rq is List<object> reqList)
                foreach (var item in reqList)
                    if (item is List<object> reqPair && reqPair.Count >= 2)
                        s.req.Add(new[] { int.Parse(reqPair[0].ToString()), int.Parse(reqPair[1].ToString()) });
            // 同 LoadMatrixSkills：V/HEXA 的 magic 来自 282 的 info/magicDamage，
            // 落盘成 magic:true 字段（这批技能没有 mad）。
            if (d.TryGetValue("magic", out var matrixMagic) && matrixMagic is bool matrixMagicTrue && matrixMagicTrue)
                s.magic = true;
            foreach (var row in s.levels.Values)
                if (row is Dictionary<string, object> rd && rd.ContainsKey("mad")) { s.magic = true; break; }
            s.magic = MagicDamageOf(s.id, s.magic);
            byId[s.id] = s;
        }

        // names 段覆盖「所有核心引用的技能」（含未迁移职业组、含強化核心的包装技能），
        // 只补名字/说明，不参与数学计算。与本表同源，少一个资源文件少一处不同步。
        if (!root.TryGetValue("names", out var namesRaw) || !(namesRaw is Dictionary<string, object> nameTable)) return;
        foreach (var pair in nameTable)
        {
            if (!int.TryParse(pair.Key, out int nameId) || !(pair.Value is Dictionary<string, object> row)) continue;
            if (row.TryGetValue("name", out var value) && value as string is string text && text.Length > 0)
                matrixNames[nameId] = text;
            if (row.TryGetValue("desc", out var detail) && detail as string is string body && body.Length > 0)
                matrixDescs[nameId] = body;
            // 本表已有的条目名字非空，这里只在 IMG 直读路径里兜底，不改已有记录。
        }
    }

    public static SkillData Get(int id)
    {
        Ensure();
        if (byId.TryGetValue(id,out var d)) return d;
        if(NativeSoulWeapon.IsSkill(id))return byId[id]=NativeSoulWeapon.Definition(id);
        // JobTree 只收录已迁移的职业线；其余核心技能、追加强化技能的包装技能都要直读 IMG。
        var node=WzAssets.FileRoot("Skill/"+(id/10000).ToString("D3")+".img")?.Get("skill/"+id.ToString("D7"));
        var levels=node?.Get("level");
        var common=node?.Get("common");
        // 282 起一大批技能（五转 V / HEXA / 強化核心的包装技能 400004xxx）只写 common 公式，
        // **没有 level 行**。旧实现「levels==null 就 return null」把这类技能判成不存在，
        // 表现就是「核心能强化，却绑不上快捷栏、也放不出技能」。这里按 common 展开成与
        // level 同形的行，公式语义与 Common()/EvalCommon 完全一致（d/u/logN/四则）。
        int commonMax=common?.Int("maxLevel") ?? 0;
        if (levels==null && commonMax<=0) return null;
        var text=WzAssets.FileRoot("String/Skill.img")?.Get(id.ToString("D7"));
        string skillName=text?.Text("name") ?? "";
        string skillDesc=text?.Text("desc") ?? "";
        // 项目自己的 String/Skill.img 没有五转条目（只有四转那 960 条），名字从 282 的导出来。
        if (skillName.Length==0) matrixNames.TryGetValue(id, out skillName);
        if (string.IsNullOrEmpty(skillDesc)) matrixDescs.TryGetValue(id, out skillDesc);
        d=new SkillData { id=id,job=id/10000,name=skillName ?? "",desc=skillDesc ?? "",
            masterLevel=node.Int("masterLevel"),icon=node.Get("icon")!=null ? "wz://Skill/"+(id/10000).ToString("D3")+".img/skill/"+id.ToString("D7")+"/icon" : "",type="passive" };
        if (levels==null)
        {
            for (int level=1; level<=commonMax; level++) d.levels[level.ToString()]=ExpandCommon(common,level);
            d.maxLevel=commonMax;
        }
        else foreach (var level in levels.Children)
        {
            if (!int.TryParse(level.Name,out int number)) continue;
            var row=new Dictionary<string,object>();
            foreach (var field in level.Children)
            {
                var value=field.TryResolve();
                if (value==null) continue;
                if (value.Kind=="Shape2D#Vector2D") row[field.Name]=new List<object>{value.X,value.Y};
                else if (value.Value!=null && value.Kind!="Canvas") row[field.Name]=value.Value;
            }
            d.levels[number.ToString()]=row;d.maxLevel=Mathf.Max(d.maxLevel,number);
            if (row.ContainsKey("mad")) d.magic=true;
        }
        if (d.maxLevel==0) return null;
        // 282 用 info/magicDamage=1 标「走魔法管线」：V/HEXA 的 common 一条 mad 都没有。
        if (node.Int("info/magicDamage")!=0) d.magic=true;
        d.magic=MagicDamageOf(id,d.magic);
        ApplyFieldAliases(id,d);
        var first=d.Level(1) ?? new Dictionary<string,object>();
        if (node.Get("summon")!=null) d.type="summon";
        else if (first.ContainsKey("damage") || first.ContainsKey("fixdamage") || first.ContainsKey("mad") || first.ContainsKey("attackCount")) d.type="attack";
        else if (first.ContainsKey("time") || first.ContainsKey("prop")) d.type="buff";
        // 282「主动上 debuff」技能（極冰風暴 400011121）common 里 damage/attackCount/
        // time/prop 一个都没有，上面三条都不命中 ⇒ 留在 passive，按下去只回
        // 「被动技能无需施放」。登记在 NativeSkillCurses.json 里的都给主动 Buff：
        // Cast 协程在 Buff 分支 yield break，不会凭空多打一发；诅咒与冰塊
        // 由 NativeSkillCurses 驱动。别给它补 u2→damage 别名，那会把母技能
        // 变成一发凭空的 1390% AOE。
        if (SkillSystem.IsCurseParent(id)) d.type="buff";
        // 普力特的祝福 400001024：common 只有 s(持续 30s)/x(最多 6 层)/cooltime/mpCon
        // 与六个效果字段，没有 time/prop ⇒ 上面三条都不命中，按下去只回「被动技能无需施放」。
        // 它是**主动叠层 buff**（h：「使用時持續#s秒並賦予可疊加最多#x次的增益」），
        // 持续时间由 NativeSkillStacks 的 durationSec 记，所以这里直接给 buff。
        if (SkillSystem.IsStackSkill(id)) d.type="buff";
        d.noName=string.IsNullOrEmpty(d.name);if(d.noName)d.name=id.ToString();
        byId[id]=d;
        return d;
    }

    // 282 有一批技能把「伤害 / 段数 / 目标数」全塞在单字母或 dummyStr 里，真实含义由
    // String/Skill.img 的 h 模板指定。导出链（tools/ms282/export_advanced_skills.py 的
    // COMMON_ALIAS）只覆盖被导进 VMatrixSkills.json 的那批；其余走本方法的 IMG 兜底，
    // 而兜底是按字段猜类型（damage/fixdamage/mad/attackCount → attack…）：
    // 一个都读不到就留在 passive ⇒ 玩家装备了核心、按下去只回「被动技能无需施放」。
    //
    // 这张表就是同一条规则的 C# 侧：先补别名，再让下面的分类判类型。
    // 每条的依据都是 282 h 原文，改之前先回去读那一段（与 python 表同一口径）。
    static readonly Dictionary<int,Dictionary<string,string>> FieldAliases =
        new Dictionary<int,Dictionary<string,string>>
    {
        // 協力攻擊 h：「1打：#q%的傷害攻擊#q2次／2打：#v%…#v2次／3打：#w%…#w2次」
        // 只取第一打当主段（与落雷凝聚「只取主段」同一口径，多段各算需要 multiAttackInfo）。
        { 400011039, new Dictionary<string,string> { {"q","damage"}, {"q2","attackCount"} } },
        // [靈魂]部分解放：黑翼破天 h：「最多對#dummyStr名敵人發動以#dummyStr2%傷害
        // 攻擊#dummyStr3次的黑色波動#dummyStr4次」
        { 400021078, new Dictionary<string,string> {
            {"dummyStr2","damage"}, {"dummyStr","mobCount"}, {"dummyStr3","attackCount"} } },
        // [靈魂]部分解放：絕魂毒牙 h：「對最多#dummyStr名敵人造成#dummyStr2%傷害
        // 攻擊#dummyStr3次的斬擊，觸發#dummyStr4次」
        { 400021155, new Dictionary<string,string> {
            {"dummyStr2","damage"}, {"dummyStr","mobCount"}, {"dummyStr3","attackCount"} } },
        // 幽靈之刃-修羅 h：「最多對#y名敵人以#z%傷害攻擊#u次」
        { 400041075, new Dictionary<string,string> {
            {"z","damage"}, {"u","attackCount"}, {"y","mobCount"} } },
        // ICBM h：「以#q%的傷害攻擊最多#u名敵人#q2次」（污染区域那段是 z% ，不并进主伤害）
        { 400051024, new Dictionary<string,string> {
            {"q","damage"}, {"q2","attackCount"}, {"u","mobCount"} } },
    };

    // 只在自身没有目标字段时写入，绝不覆盖已有定义（同 apply_alias 的 setdefault 口径）。
    static void ApplyFieldAliases(int id,SkillData d)
    {
        if(!FieldAliases.TryGetValue(id,out var map)) return;
        foreach(var entry in d.levels.Values)
        {
            if(!(entry is Dictionary<string,object> row)) continue;
            foreach(var pair in map)
                if(row.TryGetValue(pair.Key,out var value) && !row.ContainsKey(pair.Value))
                    row[pair.Value]=value;
        }
    }

    // common（282 的公式层）→ 与 level/N 同形的等级行。    //   · 常数（mpCon=250、maxLevel=30）原样搬
    //   · 公式（"30+x"、"50+d(x/2)"、"log20(x)*20"）按等级求值，整数就写 int，
    //     真的带小数（"1.8"、x/3）才写 float，保持与 WZ 原始 level 行同一取值口径
    //   · 矢量（lt/rb）保留 {x,y} 列表，和 level 行解析出来的形状一致
    // 求值失败（不是公式也不是数）的字段直接跳过：宁可没有这个键，也不写个 0 骗下游。
    static Dictionary<string,object> ExpandCommon(ImgNode common, int level)
    {
        var row=new Dictionary<string,object>();
        foreach (var field in common.Children)
        {
            var value=field.TryResolve();
            if (value==null) continue;
            if (value.Kind=="Shape2D#Vector2D") { row[field.Name]=new List<object>{value.X,value.Y}; continue; }
            if (value.Value is int number) { row[field.Name]=number; continue; }
            if (value.Value is float single) { row[field.Name]=single; continue; }
            if (value.Value is double real) { row[field.Name]=real; continue; }
            if (!(value.Value is string formula) || formula.Length==0) continue;
            float expanded=EvalCommon(formula,level,float.NaN);
            if (float.IsNaN(expanded)) continue;
            float rounded=Mathf.Round(expanded);
            row[field.Name]=Mathf.Abs(expanded-rounded)<0.0001f ? (object)(int)rounded : expanded;
        }
        return row;
    }

    public static int MaxLevelOf(int id)
    {
        var d = Get(id);
        return d == null ? 0 : d.maxLevel;
    }

    // 282 有一批「只有 common、没有 level」的技能：3141005 魔幻箭筒VI、3140006 满弹爆发、
    // 3140010/3140011 两支追加箭。Get()/Level() 对它们返回 null，数值只能从 common 取，
    // 而 common 写的是公式字符串（"292+5*x"）而不是逐级整数。
    //
    // 支持 282 实际出现的写法：常量（"60"、"1.8"）、一次式（"292+5*x"、"1350+52*x"）。
    // 公式里的 x 是等级；字段本身也叫 x 的那个（3140006.common/x=2）不受影响，
    // 因为那是「读字段值」不是「求公式」。
    public static float Common(int id, string key, int level, float fallback = 0f)
    {
        var field = CommonNode(id, key);
        if (field == null) return fallback;
        if (field.Value is int number) return number;
        if (field.Value is float single) return single;
        if (field.Value is double real) return (float)real;
        var text = field.Value as string;
        return string.IsNullOrEmpty(text) ? fallback : EvalCommon(text, level, fallback);
    }

    // 3140006.common/lt、rb 是 Shape2D#Vector2D，不能走数值口径。
    public static Vector2 CommonVector(int id, string key, Vector2 fallback)
    {
        var field = CommonNode(id, key);
        if (field == null || field.Kind != "Shape2D#Vector2D") return fallback;
        return new Vector2(field.X, field.Y);
    }

    static ImgNode CommonNode(int id, string key)
        => WzAssets.FileRoot("Skill/" + (id / 10000).ToString("D3") + ".img")
            ?.Get("skill/" + id.ToString("D7") + "/common")?.Get(key)?.TryResolve();

    // 把公式里的 x 换成等级，再按四则运算求值（+ - * / 与括号，足够覆盖 282 的 common）。
    static float EvalCommon(string text, int level, float fallback)
    {
        var expression = new System.Text.StringBuilder(text.Length + 8);
        string value = level.ToString(System.Globalization.CultureInfo.InvariantCulture);
        foreach (char c in text) expression.Append(c == 'x' || c == 'X' ? value : c.ToString());
        string source = expression.ToString();
        int at = 0;
        float result = ParseCommonSum(source, ref at, fallback);
        while (at < source.Length && source[at] == ' ') at++;
        return at >= source.Length ? result : fallback;
    }

    static float ParseCommonSum(string text, ref int at, float fallback)
    {
        float value = ParseCommonProduct(text, ref at, fallback);
        while (true)
        {
            while (at < text.Length && text[at] == ' ') at++;
            if (at >= text.Length || (text[at] != '+' && text[at] != '-')) return value;
            char op = text[at++];
            float right = ParseCommonProduct(text, ref at, fallback);
            value = op == '+' ? value + right : value - right;
        }
    }

    static float ParseCommonProduct(string text, ref int at, float fallback)
    {
        float value = ParseCommonAtom(text, ref at, fallback);
        while (true)
        {
            while (at < text.Length && text[at] == ' ') at++;
            if (at >= text.Length || (text[at] != '*' && text[at] != '/')) return value;
            char op = text[at++];
            float right = ParseCommonAtom(text, ref at, fallback);
            if (op == '*') value *= right;
            else if (Mathf.Abs(right) > 0.0001f) value /= right;
        }
    }

    static float ParseCommonAtom(string text, ref int at, float fallback)
    {
        while (at < text.Length && text[at] == ' ') at++;
        if (at >= text.Length) return fallback;
        if (text[at] == '(')
        {
            at++;
            float inner = ParseCommonSum(text, ref at, fallback);
            if (at < text.Length && text[at] == ')') at++;
            return inner;
        }
        if (text[at] == '-') { at++; return -ParseCommonAtom(text, ref at, fallback); }
        // 282 的 common 公式里只有三种函数（全库 7272 个公式字段扫出来的全集）：
        //   d(v)    向下取整        353 处，例 15-2*d(x/2)
        //   u(v)    向上取整        145 处，例 50+u(x/2)
        //   logN(v) 以 N 为底取整  939 处，例 log20(x)*20+log30(x)*30
        // 之前只认四则运算，这些公式一律返回 fallback，等于技能数值读不到。
        if (char.IsLetter(text[at]))
        {
            int start = at;
            while (at < text.Length && (char.IsLetterOrDigit(text[at]) || text[at] == '_')) at++;
            string name = text.Substring(start, at - start);
            while (at < text.Length && text[at] == ' ') at++;
            if (at >= text.Length || text[at] != '(') return fallback;
            at++;
            float inner = ParseCommonSum(text, ref at, fallback);
            if (at < text.Length && text[at] == ')') at++;
            return ApplyCommonFunction(name, inner, fallback);
        }
        int digits = at;
        while (at < text.Length && (char.IsDigit(text[at]) || text[at] == '.')) at++;
        if (at == digits) return fallback;
        return float.TryParse(text.Substring(digits, at - digits), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out float number) ? number : fallback;
    }

    static float ApplyCommonFunction(string name, float value, float fallback)
    {
        if (name == "d") return Mathf.Floor(value);
        if (name == "u") return Mathf.Ceil(value);
        if (name.Length > 3 && name.StartsWith("log") &&
            int.TryParse(name.Substring(3), System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out int radix) && radix > 1 && value > 0f)
            return Mathf.Floor(Mathf.Log(value) / Mathf.Log(radix));
        return fallback;
    }

    // BeiDouSkillCompatCore 6728149A..672818F9: explorer warrior/thief/
    // pirate extensions dispatch physically; 212/222/232 and Blaze Wizard
    // dispatch to 96928B. Hidden replay components use the same server family.
    static bool MagicDamageOf(int id, bool original)
    {
        int job = id / 10000;
        if (job == 1112 || job == 1212 || job == 1312 || job == 1412 || job == 1512)
            return job == 1212;
        return original;
    }

    /// <summary>源端 Skill.isFourthJob()：技能所属职业 job%10==2（112/212/312…）。</summary>
    public static bool IsFourthJobSkill(int skillId) => skillId / 10000 % 10 == 2;

    /// <summary>
    /// 加点上限。源端 AssignSPProcessor.java:82：
    ///   curLevel + 1 &lt;= (skill.isFourthJob() ? player.getMasterLevel(skill) : skill.getMaxLevel())
    /// 四转技能的 masterLevel 来自 Skill.wz 的 masterLevel 节点（1121001=10、1221012=30），
    /// 再被技能册（Item/Consume/0228.img，SkillBookHandler）抬到 20/30。
    /// 没有 masterLevel 节点的技能（1121006 等）不受限，直接用 maxLevel。
    /// </summary>
    public static int MasterCapOf(int id)
    {
        var d = Get(id);
        if (d == null) return 0;
        int cap = d.maxLevel;
        if (IsFourthJobSkill(d.id) && d.masterLevel > 0) cap = Mathf.Min(cap, d.masterLevel);
        int book = SkillBookItems.MasterLevelFor(d.id);
        return book > cap ? book : cap;
    }

    // 当前职业对应哪条线。cls 字符串 → job id（见 JobDefs.LegacyCls）。
    // 新手（job 0）技能固定为 团队治疗/疾风步/蜗牛投掷术/精灵的祝福；
    // 页签名取自 String.wz/Skill.img/<job>/bookName。
    public static Dictionary<string, object> LineFor(string cls) => LineForJob(JobDefs.JobIdOf(cls));

    public static Dictionary<string, object> LineForJob(int jobId)
    {
        Ensure();
        return lineByJob.TryGetValue(jobId, out var line) ? line : lines.Count > 0 ? lines[0] : null;
    }

    public static Dictionary<string, object> TreeForJob(int jobId)
    {
        Ensure();
        if (lineByJob.TryGetValue(jobId, out var line))
        {
            // Keep every acquired job book, using the exported source parent chain.
            var tabs = new List<object>();
            var visited = new HashSet<int>();
            for (int job = jobId; job >= 0 && visited.Add(job); job = JobDefs.ParentOf(job))
                if (lineByJob.TryGetValue(job, out var ancestor))
                    tabs.Insert(0, ((List<object>)ancestor["tabs"])[0]);
            var result = new Dictionary<string, object>(line);
            result["tabs"] = tabs;
            return result;
        }
        var known = new List<string>();
        foreach (var l in lines) known.Add(l.TryGetValue("id", out var id) ? id.ToString() : "?");
        Debug.LogWarning("[SkillBook] 没有职业线 " + jobId + "，可用：" + string.Join(",", known.ToArray()));
        return lines.Count > 0 ? lines[0] : null;
    }

    // MiniJson 的数组统一是 List<object>，元素是 Dictionary<string,object>；
    // 直接强转 List<Dictionary<..>> 会抛 InvalidCastException，只能逐个转。
    public static List<Dictionary<string, object>> Tabs(Dictionary<string, object> line)
    {
        var result = new List<Dictionary<string, object>>();
        if (line == null || !line.TryGetValue("tabs", out var raw) || !(raw is List<object> list)) return result;
        foreach (var item in list)
            if (item is Dictionary<string, object> tab) result.Add(tab);
        return result;
    }

    public static string JobName(int job)
    {
        Ensure();
        return jobNames.TryGetValue(job.ToString(), out var v) ? v as string : job.ToString();
    }

    // 原版等级数据 → NativeSkillDefinition（伤害/命中数/范围全部来自 WZ 对应等级）
    public static NativeSkillDefinition Native(int id, int level)
    {
        var d = Get(id);
        var row = d?.Level(level);
        if (row == null) return null;
        string kind = SpecialKind(id) ?? (d.IsSummon ? "Summon" : d.IsBuff ? "Buff" : d.IsPassive ? "Passive" : "Attack");
        var wz = WzAssets.Node("Skill/" + (id / 10000).ToString("D3") + ".img/skill/" + id.ToString("D7"));
        var skill = new Dictionary<string, object>
        {
            { "id", d.id }, { "kind", kind }, { "magic", d.magic },
            { "element", WzAssets.FileRoot("Skill/" + (id / 10000).ToString("D3") + ".img")
                .Get("skill/" + id.ToString("D7"))?.Text("elemAttr") ?? "" }, { "summonInterval", 1f },
            // skillType=3 是源端「无固定攻击框」标记，全屏技能靠它判定范围。
            { "skillType", wz?.Int("skillType") ?? 0 },
            // info/areaAttack 是源端「范围攻击」标记（info2 里也写一份）：五六转技能
            // 一律没有 lt/rb，靠它说明「这是大范围攻击」。诊断/守卫用，判定见 NativeSkillCatalog。
            { "areaAttack", Mathf.Max(wz?.Int("info/areaAttack") ?? 0, wz?.Int("info2/areaAttack") ?? 0) },
            // 五六转技能：V 是 id≥400000000，HEXA 由核心等级提升（hexa 标记）。
            { "vMatrix", d.hexa || id / 1000000 >= 400 },
        };
        var data = new Dictionary<string, object>(row);
        data["level"] = level;
        // 只有 attackInfo 型技能会真的写进 damage/attackCount；其余技能这里是空转。
        var jobRow = JobAttackRow(d, level);
        if (jobRow != null) foreach (var pair in jobRow) data[pair.Key] = pair.Value;
        // 技能间伤害强化：h 的被动段「[被动效果：X 技能伤害增加 #field%p]」。
        // 那 12 条技能（愤怒爆发VI → 狂暴攻击VI、勾拳爆破VI → 闪‧连杀VI 等）强化的
        // 是**另一条技能**，字段既不是属性 stat 也产生不了伤害体，只能在这里按百分点
        // 放大目标的 damage。放在 Native() 出口的理由同 attackInfo：proc / 召唤 /
        // 普通施法共用这一个入口；data 每次都是新建副本，不会重复放大。
        int boost = NativeSkillBoosts.Pct(d.id);
        if (boost > 0 && data.ContainsKey("damage"))
            data["damage"] = Art.Num(data, "damage") * (100 + boost) / 100.0;
        return new NativeSkillDefinition(skill, data);
    }

    // 艾尔达斯的降临(400001036) / 蜘蛛之镜(400001039) / 烈阳印记(400001059)：
    // 282 把 common/attackInfo/<四转职业ID>/{damage,attackCount} 按职业拆开
    // （h 模板写的是 #damage% / #attackCount 次，客户端取自己那一行），
    // 所以同一条技能的伤害随职业变化 —— 不能用某一个职业的数摊平。
    //
    // 取值顺序：当前职业精确命中 → 同一条支线（113 六转英雄 / 112 四转英雄 都落到 112 那一行）→ 无。
    // 都取不到就保持原样（DamagePercent=0），不会拿别的职业的伤害冒充。
    // 注意除数是 10 不是 100：五六转编码是「四转 ID + 1」（113/123/…/1213），
    // 用 /100 会把 123 圣骑士错算到 112 英雄那一条。
    static Dictionary<string, object> JobAttackRow(SkillData d, int level)
    {
        if (d?.attackInfo == null) return null;
        string key = level.ToString();
        if (d.attackInfo.TryGetValue(Profile.JobId.ToString(), out var exact) &&
            exact is Dictionary<string, object> exactLevels &&
            exactLevels.TryGetValue(key, out var exactRow))
            return exactRow as Dictionary<string, object>;
        int family = Profile.JobId / 10, best = int.MaxValue;
        foreach (var pair in d.attackInfo)
            if (int.TryParse(pair.Key, out int other) && other / 10 == family && other < best) best = other;
        if (best == int.MaxValue) return null;
        return d.attackInfo.TryGetValue(best.ToString(), out var fallback) &&
               fallback is Dictionary<string, object> fallbackLevels &&
               fallbackLevels.TryGetValue(key, out var fallbackRow)
            ? fallbackRow as Dictionary<string, object> : null;
    }
}

/// <summary>
/// 四转技能册：Item/Consume/0228.img/&lt;0228xxxx&gt;/info/{masterLevel, skill/&lt;n&gt;=&lt;skillid&gt;}。
/// 源端 SkillBookHandler.java:71-87 —— 用了技能册就把该技能的 masterLevel 抬到册上的值。
/// 直读 IMG，不落任何导出表。
/// </summary>
public static class SkillBookItems
{
    static readonly Dictionary<int, List<KeyValuePair<int, int>>> bySkill =
        new Dictionary<int, List<KeyValuePair<int, int>>>();   // skillId → [(itemId, masterLevel)]
    static bool tried;

    public static void Ensure()
    {
        if (tried) return;
        tried = true;
        var root = WzAssets.FileRoot("Item/Consume/0228.img");
        if (root == null) return;
        foreach (var item in root.Children)
        {
            if (!int.TryParse(item.Name, out int itemId)) continue;
            var info = item.Get("info");
            if (info == null) continue;
            int master = info.Int("masterLevel");
            var skills = info.Get("skill");
            if (master <= 0 || skills == null) continue;
            foreach (var s in skills.Children)
            {
                if (!int.TryParse(s.Value == null ? s.Name : s.Value.ToString(), out int skillId)) continue;
                if (!bySkill.TryGetValue(skillId, out var list))
                    bySkill[skillId] = list = new List<KeyValuePair<int, int>>();
                list.Add(new KeyValuePair<int, int>(itemId, master));
            }
        }
    }

    /// <summary>Only a successfully consumed book raises the persisted master level.</summary>
    public static int MasterLevelFor(int skillId)
    {
        return GameSession.Character?.skills.Find(s=>s.skillId==skillId)?.masterLevel ?? 0;
    }
}

// 技能等级与技能点。等级/已花费的 SP 落在 PlayerPrefs，和键位一样是本地存档；
// 剩余 SP 是存档累计字段（PlayerProfileData.sp，Profile.sp 读取）。
//
// 源端模型（Character.levelUpGainSp / changeJob / resetStats）：
//   新手升级不给 SP（三个新手技能免费，源端靠任务/教官传授）；
//   一转起每级 +3，转职另按 JobSystem 授予（一转 +1、四转 +3）；
//   一转时 resetStats 按 1+(等级-10)*3 重算（法师 1+(等级-8)*3）。
// 本地为兼容旧存档保留「新手虚拟 SP」：等级-1、封顶 6（旧公式，见 docs 适配表）。
public static class SkillProgress
{
    public const int SpPerLevel = 3;   // 源端 level_up_sp_gain

    // 新手虚拟 SP 总量（旧客户端公式，源端新手本无 SP）
    public static int BeginnerSp => Mathf.Clamp(Profile.level - 1, 0, 6);

    /// <summary>
    /// **生效**等级：存档等级 + 实用的战斗命令（400001004）的临时加成。
    /// 所有战斗/增益/伤害结算都读这一份 —— 战斗命令生效期间 1121008 就是 11 级，
    /// 拿 10 级的表行算伤害会少一档。
    /// 加点、洗点、技能册、SP 统计一律走 <see cref="BaseLevelOf"/>：那是存档里的真实等级，
    /// 拿加成后的等级去比上限会让「+1 之后还能不能加点」判错（SP 还被白扣）。
    /// </summary>
    public static int LevelOf(int id)
    {
        int level = BaseLevelOf(id);
        if (level <= 0) return 0;
        int bonus = NativeCombatOrders.Bonus(id);
        return bonus <= 0 ? level : Mathf.Min(level + bonus, NativeCombatOrders.Cap(id, bonus));
    }

    /// <summary>存档里的等级（不含任何临时加成）。</summary>
    public static int BaseLevelOf(int id)
    {
        // 282 套装档位授予的 8000xxxx 技能：不写存档，等级来自当前穿着重算出的授予表。
        // 必须在 SkillBook.Get() 之前判，因为这些技能不在 GMS083 的 Skill.wz 树里。
        if(NativeSetItemSkills.IsSkill(id))return NativeSetItemSkills.LevelOf(id);
        if(NativeSoulWeapon.IsSkill(id))return NativeSoulWeapon.LevelOf(id);
        // 五转核心连接的技能：等级跟核心走（服务端 NativeMatrixSkills 投影进 skills 表，
        // 面板快照给了更及时的一份，见 VMatrixLevels）。
        if (VMatrixLevels.TryGet(id, out int matrixLevel)) return matrixLevel;
        var d = SkillBook.Get(id);
        int lv = Profile.Data.SkillLevelOf(id);
        return d == null ? 0 : Mathf.Clamp(lv, 0, SkillBook.MasterCapOf(id));
    }

    public static void SetLevel(int id, int lv)
    {
        var d = SkillBook.Get(id);
        if (d == null) return;
        Profile.Data.SetSkillLevel(id, Mathf.Clamp(lv, 0, SkillBook.MasterCapOf(id)));
        Profile.Save();
    }

    // —— DB 桥接（GameSession 进游戏/存档用）——
    public static void ClearAll() { Profile.Data.ClearSkills(); Profile.Save(); }

    public static void SetLearned(int id, int lv) => SetLevel(id, lv);

    public static IEnumerable<KeyValuePair<int, int>> AllLearned()
    {
        var d = Profile.Data;
        for (int i = 0; i < d.skillIds.Count && i < d.skillLevels.Count; i++)
            if (d.skillLevels[i] > 0) yield return new KeyValuePair<int, int>(d.skillIds[i], d.skillLevels[i]);
    }

    /// <summary>总获得 SP（展示用）。新手 = 虚拟公式；其余 = 剩余 + 已花费。</summary>
    public static int TotalSp
        => Profile.JobId == 0 ? BeginnerSp : Profile.Sp + SpentSp;

    public static int SpentSp
    {
        get
        {
            int sum = 0;
            foreach (var d in AllSkills()) sum += BaseLevelOf(d.id);
            return sum;
        }
    }

    /// <summary>
    /// 源端 AssignSPProcessor.java:71-78：新手虚拟 SP 只扣这三个技能的等级，
    ///   total = Lv(1000) + Lv(1001) + Lv(1002)；remainingSp = min(level-1, 6) - total
    /// 新手线里还有「精灵的祝福」(12) 这类自动习得技能，不能算进消耗。
    /// </summary>
    public static int BeginnerSpent
        => BaseLevelOf(1000) + BaseLevelOf(1001) + BaseLevelOf(1002);

    /// <summary>可花费 SP。新手 = 虚拟总量 - 三个新手技能已点等级；其余直接读存档累计字段。</summary>
    public static int AvailableSp
        => Profile.JobId == 0 ? Mathf.Max(0, BeginnerSp - BeginnerSpent) : Mathf.Max(0, Profile.Sp);

    static readonly List<SkillData> cache = new List<SkillData>();
    static bool cacheReady;
    static int cacheJob = -1;

    static List<SkillData> AllSkills()
    {
        if (cacheReady && cacheJob == Profile.JobId) return cache;
        cacheReady = true;
        cacheJob = Profile.JobId;
        cache.Clear();
        var line = SkillBook.LineForJob(Profile.JobId);
        foreach (var tab in SkillBook.Tabs(line))
        {
            if (!tab.TryGetValue("skills", out var list)) continue;
            foreach (var raw in (List<object>)list)
            {
                var d = SkillBook.Get(int.Parse(raw.ToString()));
                if (d != null) cache.Add(d);
            }
        }
        return cache;
    }

    // 前置技能没满点时不能加（原版 req 节点）；自动习得的技能（精灵的祝福）永远不能手动加点；
    // 四转技能还要受 masterLevel 限制（技能册可突破）。新手只能点 1000/1001/1002 三个。
    // 返回 null = 可加点；否则是给玩家看的原因（面板用提示框显示，不能只丢日志）。
    public static string WhyNot(SkillData d)
    {
        if (d == null) return "没有这个技能的数据";
        if (d.autoLearn) return "这个技能会随等级自动习得，不能手动加点";
        if (d.hexa) return d.id >= 400000000
            ? "这个技能只能在 V 矩阵里提升（核心等级）"
            : "六转技能只能在 HEXA 矩阵中提升";
        // 战斗命令加成期间生效等级会临时 +1，但加点上限看的是存档等级，
        // 拿加成后的值比 MasterCap 会把「还能不能再点」判反（还会白扣 SP）。
        if (BaseLevelOf(d.id) >= SkillBook.MasterCapOf(d.id))
        {
            int boosted = LevelOf(d.id) - BaseLevelOf(d.id);
            return "「" + NameOf(d) + "」已达最高等级 " + SkillBook.MasterCapOf(d.id) + " 级"
                + (boosted > 0 ? "（实用的战斗命令生效中，临时 +" + boosted + " 级）" : "");
        }
        bool beginner = IsBeginnerSkill(d.id);
        if ((beginner ? BeginnerSp - BeginnerSpent : AvailableSp) <= 0)
            return beginner ? "可使用的新手技能点不足" : "SP 不足，无法继续提升技能";
        if (Profile.JobId == 0 && d.id != 1000 && d.id != 1001 && d.id != 1002)
            return "新手只能提升基础技能";
        foreach (var r in d.req)
        {
            if (LevelOf(r[0]) >= r[1]) continue;
            var pre = SkillBook.Get(r[0]);
            return "需要先把「" + (pre != null ? NameOf(pre) : r[0].ToString()) + "」提升到 " + r[1] + " 级";
        }
        return null;
    }

    static string NameOf(SkillData d) => string.IsNullOrEmpty(d.name) ? d.id.ToString() : d.name;

    public static bool CanRaise(SkillData d) => WhyNot(d) == null;

    public static bool Raise(SkillData d)
    {
        if (!CanRaise(d)) return false;
        SetLevel(d.id, BaseLevelOf(d.id) + 1);
        if (!IsBeginnerSkill(d.id)) Profile.AddSp(-1);
        return true;
    }
    static bool IsBeginnerSkill(int id) => id % 10000000 >= 1000 && id % 10000000 <= 1002;

    // 重置当前职业技能等级，已花费 SP 返还（源端 resetSkillsAndResetSP(returnSp=true)）
    public static void Reset()
    {
        int refund = 0;
        foreach (var d in AllSkills())
        {
            if (d.hexa) continue;              // 六转技能等级来自 HEXA 核心，重置 SP 时既不退点也不清零
            refund += BaseLevelOf(d.id);
            Profile.Data.SetSkillLevel(d.id, 0);
        }
        if (Profile.JobId != 0 && refund > 0) Profile.AddSp(refund);
        Profile.Save();
    }
}

// 技能施放统一入口：技能窗双击、虚拟按键、快捷格三条路都走这里，
// 失败原因统一由 SkillSystem.Report 报到 HUD 上，不会出现"点了没反应也没提示"。
public static class SkillCast
{
    public static bool TryCast(int skillId, out string fail)
    {
        fail = null;
        var player = GameLoop.I != null ? GameLoop.I.player : null;
        var sys = player != null ? player.GetComponent<SkillSystem>() : null;
        if (sys == null) { fail = "当前无法施放技能"; return false; }

        var data = SkillBook.Get(skillId);
        if (data == null) { fail = "缺少技能数据"; return false; }
        if (data.IsPassive && skillId!=1016 && !NativeArrowMode.IsToggle(skillId)) { fail = "被动技能无需施放"; sys.Report(fail); return false; }
        if (SkillProgress.LevelOf(skillId) <= 0) { fail = "技能尚未学习（技能窗里加点）"; sys.Report(fail); return false; }

        if (sys.TryCastSkill(skillId)) return true;
        fail = sys.LastFailure;
        if (!string.IsNullOrEmpty(fail)) sys.Report(fail);
        return false;
    }
}

// 右上八个快捷格 → 技能 id 的绑定。0 表示空格。
// 和原版一样：技能图标从技能窗拖到格子上完成绑定，格子再被键盘键触发。
public static class QuickSlots
{
    public const int Count = 26;
    public static GameAction Action(int index) => index < 8
        ? (GameAction)((int)GameAction.Slot1 + index)
        : (GameAction)((int)GameAction.Slot9 + index - 8);
    public static int Index(GameAction action) => action >= GameAction.Slot1 && action <= GameAction.Slot8
        ? (int)action - (int)GameAction.Slot1
        : action >= GameAction.Slot9 && action <= GameAction.Slot26 ? (int)action - (int)GameAction.Slot9 + 8 : -1;
    static readonly int[] ids = new int[Count];
    static bool loaded;

    public static void Ensure()
    {
        if (loaded) return;
        loaded = true;
        for (int i = 0; i < Count; i++) ids[i] = PlayerPrefs.GetInt("mxd.qs." + i, 0);
    }

    public static void LoadCharacter(int[] skills)
    {
        loaded = true;
        System.Array.Clear(ids, 0, ids.Length);
        System.Array.Copy(skills, ids, System.Math.Min(skills.Length, ids.Length));
    }

    public static int Id(int slot)
    {
        Ensure();
        return slot >= 0 && slot < Count ? ids[slot] : 0;
    }

    public static void Bind(int slot, int skillId)
    {
        Ensure();
        if (slot < 0 || slot >= Count) return;
        ids[slot] = skillId;
        PlayerPrefs.SetInt("mxd.qs." + slot, skillId);
        PlayerPrefs.Save();
    }

    // Item bindings use the native keymap type 2; keep their payload distinct from skill IDs.
    // 宏绑定也是负数（-1000000 - 宏位），与道具负 id 区间隔离。
    public static bool IsItem(int id) => id < 0 && !NativeSkillMacros.IsMacro(id);

    /// <summary>
    /// 这个 id 能不能绑到快捷格 / 键位上。
    /// 原端只有「按得下去的东西」能进快捷栏：被动技能绑上去之后按一下只会弹
    /// 「被动技能无需施放」，玩家会以为绑定失败。所以在绑定的那一刻就说清，
    /// 不要等按下才报错。道具与宏标记另有一套通道，直接放行。
    /// </summary>
    public static bool CanBindToQuickBar(int id, out string why)
    {
        why = null;
        if (id == 0) { why = "这一格没有可绑定的东西。"; return false; }
        if (IsItem(id) || NativeSkillMacros.IsMacro(id)) return true;
        var data = SkillBook.Get(id);
        if (data == null) { why = "技能数据缺失，不能绑到快捷栏。"; return false; }
        // IsPassive 已经排除了 InstantSelfSkill（箭镞切换 3141005 这类「标 passive 但可主动按」）
        // 与 SpecialKind；剩余的都是真被动 / 强化核心包装技能。
        if (data.IsPassive)
        {
            why = "「" + data.name + "」是被动技能，按不下去，不能绑到快捷栏。";
            return false;
        }
        return true;
    }

    public static Texture2D BindingIcon(int id)
    {
        if (NativeSkillMacros.IsMacro(id))
        {
            int first = NativeSkillMacros.FirstSkill(NativeSkillMacros.MarkerIndex(id));
            var macroData = first != 0 ? SkillBook.Get(first) : null;
            return macroData != null && !string.IsNullOrEmpty(macroData.icon) ? Art.Tex(macroData.icon) : null;
        }
        if (IsItem(id)) return NativeItemSource.Icon(-id);
        var skill = SkillBook.Get(id);
        return skill != null && !string.IsNullOrEmpty(skill.icon) ? Art.Tex(skill.icon) : null;
    }
    public static void Clear(int slot) => Bind(slot, 0);

    public static void Cast(int slot)
    {
        var player = GameLoop.I != null ? GameLoop.I.player : null;
        if (player == null) return;
        int id = Id(slot);
        if (NativeSkillMacros.IsMacro(id)) { NativeSkillMacros.Cast(NativeSkillMacros.MarkerIndex(id)); return; }
        if (IsItem(id)) { NativeInventoryActions.DoubleClick(-id); return; }
        var sys = player.GetComponent<SkillSystem>();
        if (sys == null) return;
        if (id == 0) { sys.Report("快捷格 " + (slot + 1) + " 是空的（技能窗里拖技能上去）"); return; }
        bool ok = sys.TryCastBoundSkill(id,Action(slot));
        if (!ok) sys.Report(sys.LastFailure);
    }
}


// Display selectors are independent of the key-to-function map (native QuickSlot model).
public static class QuickBarKeys
{
    static readonly KeyCode[] defaults = {
        KeyCode.LeftShift, KeyCode.Insert, KeyCode.Home, KeyCode.PageUp,
        KeyCode.LeftControl, KeyCode.Delete, KeyCode.End, KeyCode.PageDown
    };
    public static KeyCode[] Defaults()
    {
        var keys = new KeyCode[QuickSlots.Count];
        System.Array.Copy(defaults, keys, defaults.Length);
        return keys;
    }
    public static KeyCode Of(int index) => NativeKeyLayout.FunctionKey((KeyCode)PlayerPrefs.GetInt("mxd.quickDisplay." + index,
        index < defaults.Length ? (int)defaults[index] : (int)KeyCode.None));
    public static KeyCode[] Copy()
    {
        var keys = new KeyCode[QuickSlots.Count];
        for (int i = 0; i < keys.Length; i++) keys[i] = Of(i);
        return keys;
    }
    public static void Save(KeyCode[] keys)
    {
        for (int i = 0; i < keys.Length; i++) PlayerPrefs.SetInt("mxd.quickDisplay." + i, (int)keys[i]);
        PlayerPrefs.Save();
    }
    public static int SkillSlot(KeyCode key)
    {
        if (key == KeyCode.None) return -1;
        for (int i = 0; i < QuickSlots.Count; i++)
            if (KeyMap.Of(QuickSlots.Action(i)) == key && QuickSlots.Id(i) != 0) return i;
        return -1;
    }
    public static GameAction? Function(KeyCode key)
    {
        if (key == KeyCode.None) return null;
        foreach (var kv in KeyMap.All)
            if (kv.Value == key && QuickSlots.Index(kv.Key) < 0) return kv.Key;
        return null;
    }
    public static bool BindSkill(int displayIndex, int skillId)
    {
        var key = Of(displayIndex);
        // 绑键被拒一律走弹窗：矩阵面板打开时底部 Toast 被面板压住，玩家只看到「拖了没反应」。
        if (key == KeyCode.None) { NativeUtilPrompt.Notice("这一格还没有选择按键，请先在键盘设置里给它指定一个键。"); return false; }
        return BindSkillToKey(key, skillId);
    }

    /// <summary>
    /// 第一个「配了键、还没有技能」的显示格；找不到返回 -1。
    /// 快捷栏 26 格里默认只有前 8 格有键，拖到后面那些空格子上没法直接绑（没有键就没有
    /// 触发入口），落点判定用这个找替代格，别让玩家以为拖上去没反应。
    /// </summary>
    public static int FreeKeyedSlot()
    {
        for (int i = 0; i < QuickSlots.Count; i++)
        {
            var key = Of(i);
            if (key == KeyCode.None) continue;
            // 显示键必须和这一格实际动作键一致，否则绑上去图标会出现在别的格。
            if (KeyMap.Of(QuickSlots.Action(i)) != key) continue;
            if (SkillSlot(key) >= 0) continue;      // 这个键已经绑了技能
            return i;
        }
        return -1;
    }

    public static bool BindSkillToKey(KeyCode key, int skillId)
    {
        // 所有「绑到键盘这一层」的入口最后都汇到这里（技能窗拖快捷格、矩阵面板拖键位、
        // 键盘设定里拖键帽）。被动技能在这一步就拒掉，不要先绑上再让玩家按出报错。
        if (!QuickSlots.CanBindToQuickBar(skillId, out string why))
        {
            if (!string.IsNullOrEmpty(why)) NativeUtilPrompt.Notice(why);
            return false;
        }
        int slot = SkillSlot(key);
        if (slot < 0)
            for (int i = 0; i < QuickSlots.Count; i++)
                if (QuickSlots.Id(i) == 0 || KeyMap.Of(QuickSlots.Action(i)) == KeyCode.None) { slot = i; break; }
        if (slot < 0) { NativeUtilPrompt.Notice("快捷栏的技能格已经满了，先清掉一格再绑定。"); return false; }
        KeyMap.Bind(QuickSlots.Action(slot), key);
        QuickSlots.Bind(slot, skillId);
        return true;
    }
}

/// <summary>
/// V 核心连接技能的等级镜像（skillId → 核心等级）。
///
/// 核心等级 → 技能等级的唯一镜像。**包装技能（400004xxx / 500004xxx）不在 JobTree 里，
/// 拿不到 Profile 里的技能等级**，所以强化核心的加成全靠这张表。
/// 三条写入路径：登录预热（GameSession.SelectChar）、打开 V 面板、打开 HEXA 面板。
/// V 与 HEXA 各一张表，互不覆盖（见下面的注释）。
/// </summary>
public static class VMatrixLevels
{
    // V 强化核心（vmatrix_cores）与 HEXA 强化核心（hexa_skills）各一张表。
    // 必须分开：两边都是「整份重建」语义，共用一张表会互相清掉 ——
    // 打开一次 V 面板就会把 HEXA 的等级冲掉，HEXA 强化的 damR_6th 立刻归零。
    static readonly Dictionary<int, int> matrixLevels = new Dictionary<int, int>();
    static readonly Dictionary<int, int> hexaLevels = new Dictionary<int, int>();
    static int owner = -1;

    public static void Clear()
    {
        matrixLevels.Clear();
        hexaLevels.Clear();
        owner = -1;
    }

    static void Own(int characterId)
    {
        if (owner == characterId) return;
        matrixLevels.Clear();
        hexaLevels.Clear();
        owner = characterId;
    }

    /// <summary>整份重建：快照里没出现的技能自然回落到 skills 表。</summary>
    public static void Capture(VMatrixResponse response, int characterId)
    {
        Own(characterId);
        if (response?.cores == null) return;
        matrixLevels.Clear();
        foreach (var core in response.cores)
        {
            if (core == null) continue;
            // The core id, rather than an old response payload, determines its
            // connected skill.  Keep the level mirror on the same canonical id
            // used by the V-matrix window and quick slots.
            var canonical = VMatrixCatalog.Core(core.coreId);
            var skills = canonical != null ? canonical.skills : core.skills;
            if (skills == null) continue;
            foreach (int skill in skills)
            {
                if (skill == 0) continue;
                if (!matrixLevels.TryGetValue(skill, out int old) || core.level > old) matrixLevels[skill] = core.level;
            }
        }
    }

    /// <summary>
    /// HEXA 核心等级 → 它的包装技能（`500004xxx`，`info/type=50`）等级。
    /// HEXA 强化的 `damR_6th` 靠这条链路生效（65 条 / 138 个被强化技能）。
    /// 注意 HEXA 的核心 id 是 `300000xx`，不是包装技能 id —— 等级必须挂在 `core.skills` 上。
    /// </summary>
    public static void CaptureHexa(HexaResponse response, int characterId)
    {
        Own(characterId);
        if (response?.cores == null) return;
        hexaLevels.Clear();
        foreach (var core in response.cores)
        {
            if (core == null || core.level <= 0 || core.skills == null) continue;
            foreach (int skill in core.skills)
            {
                if (skill == 0) continue;
                if (!hexaLevels.TryGetValue(skill, out int old) || core.level > old) hexaLevels[skill] = core.level;
            }
        }
    }

    public static bool TryGet(int skillId, out int level)
        => matrixLevels.TryGetValue(skillId, out level) || hexaLevels.TryGetValue(skillId, out level);

    /// <summary>调试用：当前镜像里有多少条等级（V / HEXA 分开计）。</summary>
    public static int Count(bool hexa) => hexa ? hexaLevels.Count : matrixLevels.Count;
}
