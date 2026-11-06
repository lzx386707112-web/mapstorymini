// 矩阵核心图标：V / HEXA 面板按「核心 id」取 282 原端图标。
//
// 为什么不能只靠 skillicon/<技能id>：282 的核心图标根本不在 Skill 树里。
//   * Etc/VCore.img/vSkill/CoreData/<coreId>/icon 的 _outlink 是 V 核心的权威图标表
//     （260 条：75 条落在 Etc/_Canvas/VCore.img、185 条落在 Etc/_Canvas/HexaCore.img）；
//   * Etc/HexaCore.img/hexaSkill/coreData/<coreId>/icon 是 HEXA 563 个核心的图标；
//   * 强化核心（type 1）在 VCore 里没有 icon 节点：原端用 core/iconMask 把 psdSkill
//     指向的 1~4 个技能图标切角拼成一张。本工程沿用单图标口径，按 400004021 的
//     先例（结构侧 icon 外链直接指向 psdSkill 里的真实技能图标）由导出期补进来。
// Skill/<img>.img 里 164 个 V 技能的 icon 是 1×1 全透明桩，靠它画出来的就是空格。
//
// 表由 tools/ms282/export_matrix_core_icons.py 从 282 源端导出，像素一律是原生 IMG
// 画布（ARGB4444/8888），不自制、不缩放素材。真机 HasFile 读 NativeAssetIndex 清单，
// 两个 _Canvas 文件都已登记。缺表项时退回 skillicon/<skills[0]>，保持旧行为。
using System;
using System.Collections.Generic;
using UnityEngine;

public static class MatrixCoreIcons
{
    static Dictionary<string, string> v, h;
    static readonly Dictionary<string, Texture2D> cache = new Dictionary<string, Texture2D>();
    static bool loaded;

    public static void Invalidate()
    {
        loaded = false;
        v = null;
        h = null;
        cache.Clear();
    }

    static void Ensure()
    {
        if (loaded) return;
        loaded = true;
        v = new Dictionary<string, string>();
        h = new Dictionary<string, string>();
        var root = MiniJsonDb.Parse(PlatformRes.LoadText("UI/MatrixCoreIcons")) as Dictionary<string, object>;
        if (root == null) return;
        Read(root, "v", v);
        Read(root, "h", h);
    }

    static void Read(Dictionary<string, object> root, string key, Dictionary<string, string> table)
    {
        if (!root.TryGetValue(key, out var raw)) return;
        if (!(raw is Dictionary<string, object> entries)) return;
        foreach (var pair in entries) table[pair.Key] = pair.Value as string;
    }

    /// <summary>按核心 id 取原生图标；表外或资源包未更新时返回 null，不抛异常。</summary>
    public static Texture2D Texture(int coreId, bool hexa)
    {
        Ensure();
        var table = hexa ? h : v;
        if (table == null || !table.TryGetValue(coreId.ToString(), out var path) || string.IsNullOrEmpty(path))
            return null;
        if (cache.TryGetValue(path, out var cached)) return cached;
        var node = WzAssets.Node(path);
        var texture = node == null ? null : WzAssets.Texture(node);
        cache[path] = texture;
        return texture;
    }

    /// <summary>核心图标优先，其次沿用与 SkillBook 同源的 skillicon/&lt;技能id&gt;。</summary>
    public static Texture2D Core(int coreId, bool hexa, int[] skills)
    {
        var icon = Texture(coreId, hexa);
        if (icon != null) return icon;
        return skills != null && skills.Length > 0 ? WzAssets.Texture("skillicon/" + skills[0]) : null;
    }
}
