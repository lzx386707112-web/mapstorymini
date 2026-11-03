using System;
using System.Collections.Generic;

// 机器人（Android）装配与外观解析 —— TMS 282 语义的纯 C# 复刻。
//
// 源端依据（TMS282.5/source）：
//   Char.initAndroid                   装备后按槽 53/54 取机器人/心臟，缺心臟弹提示、不生成实体
//   Char.initAndroid                   实际档位规则 heart.grade <= android.grade + 3（与道具文案不一致，照代码）
//   BodyPart.Android(53)/MechanicalHeart(54)   机器人本体/心臟的装备位
//   BodyPart.APHat(1200)..APRing4(1213)        机器人专属的 12 个换装槽（AP = Avatar Preview）
//   Etc/Android/NNNN.img               info/基本穿着/costume 随机池/action 事件台词
//
// 本工程分层：这里只做「装配判定 + 外观取值」，不碰像素也不碰存档；
// 外观模板池由 tools/ms282/import_androids.py 导出成 Bot/AndroidTemplates.json
// （服务端层是纯 C#，只能像 Bot/Items.json 那样读文本资源）。
//
// 外观走确定性哈希而不是 Random：282 服务端每次 initAndroid 随机，本工程没有
// 282 那份运行期 Android 对象，用 characterId+templateId 派生可以做到「同一角色
// 永远同一张脸、不同角色不同脸」，同时不改存档结构。

public sealed class AndroidState
{
    public bool visible;
    public string reason = "";          // 不显示时的原因（装备窗/提示用）
    public int templateId, gender;
    public int skin, hair, face;
    // 生效穿着（AP 槽覆盖模板默认穿着后的结果），0 = 该部位空
    public int hat, cape, faceAccessory, top, bottom, shoes, gloves, weapon;
}

public static class NativeAndroidService
{
    // 装备槽位：机器人 -32（本次迁移分配的空闲槽），心臟 -54（与源端 BodyPart 54 同义）
    public const int RobotSlot = -32;
    public const int HeartSlot = -54;
    // 机器人专属换装槽（源端 BodyPart.APBase..APRing4）
    public const int ApHat = 1200, ApCape = 1201, ApFaceAccessory = 1202, ApTop = 1203,
        ApBottom = 1204, ApShoes = 1205, ApGloves = 1206, ApWeapon = 1207,
        ApRing = 1210, ApRing2 = 1211, ApRing3 = 1212, ApRing4 = 1213;
    public const int ApFirst = ApHat, ApLast = 1213;
    public static readonly int[] ApSlots =
    {
        ApHat, ApCape, ApFaceAccessory, ApTop, ApBottom, ApShoes, ApGloves, ApWeapon,
        ApRing, ApRing2, ApRing3, ApRing4,
    };

    // Local 282 EXE RVA 5C5C618..5C5CA00, ordinary (unmounted) modes 1/2.
    // Mode is refreshed by owner action updates (1F4B0B1..1F4B0D5), not just creation.
    // Coordinates are WZ pixels. modeFacingLeft describes the current controller mode.
    public static int FollowDirection(bool modeFacingLeft, bool facesLeft, int ownerX, int androidX,
        int previousAndroidX, int direction)
    {
        int target = ownerX;
        if (!modeFacingLeft && facesLeft) target -= 145;
        if (modeFacingLeft && !facesLeft) target += 145;
        int threshold = modeFacingLeft ? (facesLeft ? 65 : 105) : (facesLeft ? 105 : 65);
        if (facesLeft ? ownerX > androidX : androidX > ownerX) threshold += 45;
        int distance = Math.Abs(target - androidX);
        if (distance < threshold) return 0;
        if ((androidX == previousAndroidX && distance > threshold) || distance > 80)
            return target > androidX ? 1 : -1;
        return direction;
    }

    public const int RobotIdFirst = 1662000, RobotIdLast = 1669999;
    public const int HeartIdFirst = 1672000, HeartIdLast = 1679999;
    // 项目资源树里兜底用的肤色（282 的皮肤主体画布没导入，见 import_androids.py）
    public const int FallbackSkin = 2000;

    static Dictionary<string, object> templates;
    static readonly object gate = new object();

    public static bool IsRobot(int itemId) => itemId >= RobotIdFirst && itemId <= RobotIdLast;
    public static bool IsHeart(int itemId) => itemId >= HeartIdFirst && itemId <= HeartIdLast;

    /// <summary>Bot/AndroidTemplates.json 的 templates 节点；缺失时返回空表（不抛，按「模板缺失」处理）。</summary>
    static Dictionary<string, object> Templates
    {
        get
        {
            lock (gate)
            {
                if (templates != null) return templates;
                string text = PlatformRes.LoadText("Bot/AndroidTemplates");
                var root = text == null ? null : MiniJsonDb.Parse(text) as Dictionary<string, object>;
                templates = root != null && root.TryGetValue("templates", out var node)
                    ? node as Dictionary<string, object> ?? new Dictionary<string, object>()
                    : new Dictionary<string, object>();
                return templates;
            }
        }
    }

    public static Dictionary<string, object> Template(int templateId)
        => Templates.TryGetValue(templateId.ToString(), out var node)
            ? node as Dictionary<string, object> : null;

    public static int TemplateCount => Templates.Count;

    static Dictionary<string, object> ItemInfo(int itemId)
        => Server.Bot.BotItemCatalog.Shared.Info(itemId);

    static int ItemInt(int itemId, string key, int fallback = 0)
        => Server.Bot.BotItemCatalog.Int(ItemInfo(itemId), key, fallback);

    /// <summary>282 的道具档位：机器人/心臟都用 info/grade。</summary>
    public static int GradeOf(int itemId) => ItemInt(itemId, "grade");

    /// <summary>机器人本体指向的外观模板（info/android）。0 = 没有模板。</summary>
    public static int TemplateOf(int itemId) => ItemInt(itemId, "android");

    /// <summary>
    /// 装配判定 + 外观解析。
    /// slotOf: 负槽位 → 道具 id（本项目由 Profile.EquipOfSlot 提供；测试可传字典）。
    /// </summary>
    public static AndroidState Resolve(int characterId, Func<int, int> slotOf)
    {
        var state = new AndroidState();
        if (slotOf == null) { state.reason = "无效的装备栏"; return state; }
        int robotId = slotOf(RobotSlot), heartId = slotOf(HeartSlot);
        if (robotId <= 0) return state;                      // 没装机器人 = 不显示，不是错误
        if (!IsRobot(robotId)) { state.reason = "机器人道具无效"; return state; }

        // 源端：有机器人无心臟 → 弹「請安裝心臟」并不生成实体
        if (heartId <= 0 || !IsHeart(heartId))
        {
            state.reason = "請安裝心臟";
            return state;
        }

        // Char.initAndroid：heart.grade <= android.grade + 3（不是道具文案写的「相同或提高」）
        int robotGrade = GradeOf(robotId), heartGrade = GradeOf(heartId);
        if (heartGrade > robotGrade + 3)
        {
            state.reason = "心臟等級過高（需不超過機器人等級 +3）";
            return state;
        }

        int templateId = TemplateOf(robotId);
        var template = Template(templateId);
        if (template == null)
        {
            // 源端在模板节点缺失时只打日志、静默不显示（Char.initAndroid :9680 的活坑）
            state.reason = "缺少外观模板 " + templateId;
            return state;
        }

        state.templateId = templateId;
        state.gender = TemplateInt(template, "info", "gender");
        state.skin = TemplateList(template, "skin") is { Count: > 0 } skins
            ? skins[0] : FallbackSkin;
        state.hair = PickFrom(template, "hair", characterId, templateId, 1);
        state.face = PickFrom(template, "face", characterId, templateId, 7);

        ApplyWardrobe(state, template, slotOf);
        state.visible = true;
        return state;
    }

    /// <summary>模板默认穿着 + AP 槽覆盖。AP 槽非空优先（玩家手动换的衣服）。</summary>
    static void ApplyWardrobe(AndroidState state, Dictionary<string, object> template, Func<int, int> slotOf)
    {
        var basic = template.TryGetValue("basic", out var node) ? node as Dictionary<string, object> : null;
        state.hat = BasicInt(basic, "cap");
        state.cape = BasicInt(basic, "cape");
        state.faceAccessory = BasicInt(basic, "faceAccessory", "accessory");
        state.top = BasicInt(basic, "longcoat");
        state.shoes = BasicInt(basic, "shoes");
        state.gloves = BasicInt(basic, "gloves", "glove");

        if (slotOf(ApHat) > 0) state.hat = slotOf(ApHat);
        if (slotOf(ApCape) > 0) state.cape = slotOf(ApCape);
        if (slotOf(ApFaceAccessory) > 0) state.faceAccessory = slotOf(ApFaceAccessory);
        if (slotOf(ApTop) > 0) state.top = slotOf(ApTop);
        if (slotOf(ApBottom) > 0) state.bottom = slotOf(ApBottom);
        if (slotOf(ApShoes) > 0) state.shoes = slotOf(ApShoes);
        if (slotOf(ApGloves) > 0) state.gloves = slotOf(ApGloves);
        if (slotOf(ApWeapon) > 0) state.weapon = slotOf(ApWeapon);
    }

    /// <summary>AP 槽判定：1200..1207 + 1210..1213。1208/1209 是空号（APRing 从 1210 起）。</summary>
    public static bool IsApSlot(int slot)
        => slot >= ApHat && slot <= ApWeapon || slot >= ApRing && slot <= ApRing4;

    /// <summary>
    /// AP 槽收不收这件装备。itemSlot 是道具自身的自然装备位（-1 帽 … -11 武器）。
    /// 源端 BodyPart 表：APHat(1200)/APCape(1201)/APFaceAccessory(1202)/APTop(1203)/
    /// APBottom(1204)/APShoes(1205)/APGloves(1206)/APWeapon(1207)/APRing..APRing4(1210..1213)。
    /// APTop 同时收上衣(104)与套服(105)——套服在 BodyPart 里也落 1203，不另开下装。
    /// </summary>
    public static bool ApAccepts(int apSlot, int itemSlot)
    {
        // 现金装备（islot Cp/Af/…）在本工程是 slot-100（-101/-102/…），机器人换装槽只有一格，
        // 不区分现金/普通，先归一化再比。
        if (itemSlot <= -100) itemSlot += 100;
        switch (apSlot)
        {
            case ApHat: return itemSlot == -1;
            case ApCape: return itemSlot == -9;
            case ApFaceAccessory: return itemSlot == -2;
            case ApTop: return itemSlot == -5;
            case ApBottom: return itemSlot == -6;
            case ApShoes: return itemSlot == -7;
            case ApGloves: return itemSlot == -8;
            case ApWeapon: return itemSlot == -11;
            case ApRing: case ApRing2: case ApRing3: case ApRing4:
                return itemSlot == -12 || itemSlot == -13 || itemSlot == -15 || itemSlot == -16;
        }
        return false;
    }

    /// <summary>模板里的外观池（skin/hair/face）。客户端取不到目标资源时要沿池回退，所以必须能读到。</summary>
    public static List<int> Pool(int templateId, string key)
    {
        var template = Template(templateId);
        return template == null ? null : TemplateList(template, key);
    }

    /// <summary>事件台词表：hello/levelup/dead/bye/job/alert，每项若干变体，按 prob 加权。</summary>
    public static List<Dictionary<string, object>> ActionEntries(int templateId, string action)
    {
        var template = Template(templateId);
        if (template == null || !template.TryGetValue("actions", out var node)) return null;
        if (node is not Dictionary<string, object> actions) return null;
        if (!actions.TryGetValue(action, out var list) || list is not List<object> variants) return null;
        var entries = new List<Dictionary<string, object>>();
        foreach (var variant in variants)
            if (variant is Dictionary<string, object> entry) entries.Add(entry);
        return entries.Count == 0 ? null : entries;
    }

    /// <summary>按 prob 权重摇一条事件台词/表情；没有 prob 的变体等权。</summary>
    public static Dictionary<string, object> RollAction(int templateId, string action, Random random)
    {
        var entries = ActionEntries(templateId, action);
        if (entries == null) return null;
        int total = 0;
        foreach (var entry in entries)
            total += Math.Max(1, Convert.ToInt32(entry.TryGetValue("prob", out var p) ? p : 1));
        int roll = (random ?? new Random()).Next(total);
        foreach (var entry in entries)
        {
            roll -= Math.Max(1, Convert.ToInt32(entry.TryGetValue("prob", out var p) ? p : 1));
            if (roll < 0) return entry;
        }
        return entries[entries.Count - 1];
    }

    // —————————————— 模板字段读取 ——————————————

    static int TemplateInt(Dictionary<string, object> template, string section, string key)
    {
        if (!template.TryGetValue(section, out var node) || node is not Dictionary<string, object> group) return 0;
        return group.TryGetValue(key, out var value) ? Convert.ToInt32(value) : 0;
    }

    static List<int> TemplateList(Dictionary<string, object> template, string key)
    {
        if (!template.TryGetValue(key, out var node) || node is not List<object> list) return null;
        var result = new List<int>(list.Count);
        foreach (var item in list) result.Add(Convert.ToInt32(item));
        return result;
    }

    /// <summary>确定性取一项：同一 characterId+templateId 恒定，不同角色分散。</summary>
    static int PickFrom(Dictionary<string, object> template, string key, int characterId, int templateId, int stride)
    {
        var list = TemplateList(template, key);
        if (list == null || list.Count == 0) return 0;
        unchecked
        {
            // FNV-1a：池子只有 20~30 项，必须让低位也充分混合，否则相邻/倍数
            // characterId 会落到同一个池位（早期乘法哈希就这么翻过车）。
            uint seed = 2166136261u;
            seed = (seed ^ (uint)characterId) * 16777619u;
            seed = (seed ^ (uint)templateId) * 16777619u;
            seed = (seed ^ (uint)stride) * 16777619u;
            seed ^= seed >> 15;
            seed *= 2246822519u;
            seed ^= seed >> 13;
            return list[(int)(seed % (uint)list.Count)];
        }
    }

    static int BasicInt(Dictionary<string, object> basic, params string[] keys)
    {
        if (basic == null) return 0;
        foreach (string key in keys)
            if (basic.TryGetValue(key, out var value) && Convert.ToInt32(value) > 0)
                return Convert.ToInt32(value);
        return 0;
    }
}
