using UnityEngine;

// ————————————————————————————————————————————————————————————
// 战斗力（282 的 atkPow / 名牌上的「战斗力：N」）—— 全工程只此一套公式。
//
// 公式来源是客户端 DLL 里的 PacketCreator.calculateNameplatePower（见 NativeNameplate 头注释）：
//   (str + dex + int + luk) * max(1, max(watk, matk)) + maxHp / 10 + maxMp / 20
// 282 自己的提示框把同一个数字画在 UI/UIToolTip.img/Item/Equip/imgFont/atkPow（plus/minus
// 两套字形 + equipped 标签），运行时路径串还有 atkPow/imgFont、atkPow/highlight、
// baseBefore / baseAfter / diff —— 即「穿上这件之后从多少变成多少」。
// 这里不新开第二套算法：预览直接用同一条公式，只在属性上做「换掉该槽位那件」的差量。
// ————————————————————————————————————————————————————————————
public static class CombatPower
{
    public static long Of(long stats, long attack, long maxHp, long maxMp)
    {
        if (stats < 0) stats = 0;
        if (attack < 1) attack = 1;
        if (maxHp < 0) maxHp = 0;
        if (maxMp < 0) maxMp = 0;
        // 属性 × 攻击力在极端配装下会顶到 long 上限：饱和而不是回绕成负数。
        long product = stats != 0 && attack > long.MaxValue / stats ? long.MaxValue : stats * attack;
        long bonus = maxHp / 10L + maxMp / 20L;
        return product > long.MaxValue - bonus ? long.MaxValue : product + bonus;
    }

    public static long Of(CharacterAttributes a, int watk, int matk)
    {
        if (a == null) return 0;
        long stats = (long)a.str + a.dex + a.intelligence + a.luk;
        return Of(stats, Mathf.Max(watk, matk), a.maxHp, a.maxMp);
    }

    /// <summary>当前角色的战斗力（同一条派生层，不含临时增益）。</summary>
    public static long Current()
    {
        CharacterAttributes attributes = Panel(out int watk, out int matk);
        return Of(attributes, watk, matk);
    }

    /// <summary>
    /// 面板：**不算临时增益**。282 的 baseBefore 是脱掉 buff 的面板，玩家要的也是一个
    /// 「换了装备会变多少」的稳定数；buff 每秒都在变，混进来就没法比。
    /// 所以一律走派生层（基础 AP + 装备 + 星力/星火 + 潜能 + 套装 + 技能被动），
    /// 不读 PlayerCtrl 那份带 buff 的 Attributes。
    /// </summary>
    static CharacterAttributes Panel(out int watk, out int matk)
        => EquipLoadout.Apply(Profile.Attributes, WeaponDefs.Get(Profile.equipWeapon), 0, out watk, out matk);

    /// <summary>是不是可装备物品（决定提示框要不要画「战斗力提升量」标签）。</summary>
    public static bool IsEquippable(int itemId) => itemId > 0 && EquipSlot.SlotOf(itemId) != 0;

    /// <summary>
    /// 穿上 entry 之后战斗力会变成多少：直接拿「换装后的完整面板」重算一遍，
    /// 所以套装条数变化、潜能 %、星火/灵魂、21x 属性被动全部按穿完之后的口径，
    /// 不是把两件的差值手工加减（那样漏掉 % 与套装）。仍然不含临时 buff。
    /// </summary>
    public static bool TryPreview(InventoryEntry entry, out long current, out long after)
    {
        current = after = 0;
        if (entry == null || entry.itemId <= 0 || entry.position < 0) return false;
        int slot = EquipSlot.SlotOf(entry.itemId);
        if (slot == 0) return false;

        current = Current();
        // 换的是武器时必须把 weapon 也换掉：Apply 的武器段是按 weapon 那张模板叠的。
        var weapon = WeaponDefs.Get(Profile.equipWeapon);
        if (slot == EquipSlot.Weapon && WeaponDefs.TryGet(entry.itemId.ToString(), out var swapWeapon))
            weapon = swapWeapon;
        var swapped = EquipLoadout.Apply(Profile.Attributes, weapon, 0, out int watk, out int matk, slot, entry.itemId, entry);
        after = Of(swapped, watk, matk);
        return true;
    }
}
