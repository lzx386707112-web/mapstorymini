using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// 组队面板（UserList 组员 tab）的无渲染断言，batchmode 可直接跑：
//   /Applications/Unity/Unity.app/Contents/MacOS/Unity -batchmode -quit \
//     -projectPath <proj> -executeMethod PartyPanelChecks.Run -logFile /tmp/party-panel.log
//
// 这组数字全部来自 BeiDou.exe：按钮来自 OnCreate 0x919B73 的 CreateButton 调用，
// 列表合成位置来自 0x91CCBB（(6,53)、高上限 278），组内偏移来自 0x90BDDB，分组来自 0x90B8D0。
// 断言的作用是防止后续改动把这些已核实的几何写错（例如把行高改成 20、把按钮换成居中对齐）。
public static class PartyPanelChecks
{
    const int WindowW = 312, WindowH = 389;
    const int ListX = 6, ListY = 53, ListW = 281, ListH = 278;

    public static void Run()
    {
        int pass = 0, fail = 0;
        void Check(bool ok, string name)
        {
            if (ok) { pass++; return; }
            fail++; Debug.LogError("[PartyPanelChecks] FAIL " + name);
        }

        // 1. 窗口/列表盒
        var box = NativeSocialWindow.PartyListBox();
        Check(Mathf.RoundToInt(box.x) == ListX && Mathf.RoundToInt(box.y) == ListY, "列表盒起点 (6,53)");
        Check(Mathf.RoundToInt(box.z) == ListW && Mathf.RoundToInt(box.w) == ListH, "列表盒尺寸 281x278");
        Check(WindowW == 312 && WindowH == 389, "窗口 312x389");

        // 2. 按钮：九颗、指定 id、互不重叠、在窗口内、按源端底边对齐
        var buttons = NativeSocialWindow.PartyButtons();
        Check(buttons.Length == 9, "按钮数量 9，实际 " + buttons.Length);
        var expect = new Dictionary<int, Vector2>
        {
            { 2200, new Vector2(13, 355) }, { 2201, new Vector2(72, 365) }, { 2202, new Vector2(190, 344) },
            { 2203, new Vector2(190, 364) }, { 2204, new Vector2(249, 345) }, { 2205, new Vector2(249, 365) },
            { 2206, new Vector2(131, 365) }, { 2207, new Vector2(131, 345) }, { 2208, new Vector2(72, 345) },
        };
        foreach (var button in buttons)
        {
            if (!expect.TryGetValue(button.id, out var at)) { Check(false, "未知按钮 id " + button.id); continue; }
            Check(Mathf.Approximately(button.x, at.x) && Mathf.Approximately(button.y, at.y),
                button.node + " 坐标 " + button.x + "," + button.y + " 应为 " + at.x + "," + at.y);
            Check(button.x >= 0 && button.y >= 0 && button.x + button.w <= WindowW && button.y + button.h <= WindowH,
                button.node + " 越界");
        }
        // 源端上排 y=344/345、下排 y=364/365，底边 363/364 与 383/384 天然对不齐：
        // 素材本身就混了 19 高（BtCreate..BtChat）和 18 高（BtChangeBoss/BtHP/BtSearch），
        // 而 OnCreate 是把 y 写死的。这里只校验行，不要再"拉成统一底线"，那会把源端坐标改错。
        foreach (var button in buttons)
        {
            if (button.id == 2200) continue;                  // 开设独占一列，y=355
            bool upper = Mathf.Approximately(button.y, 344) || Mathf.Approximately(button.y, 345);
            bool lower = Mathf.Approximately(button.y, 364) || Mathf.Approximately(button.y, 365);
            Check(upper || lower, button.node + " 行 y=" + button.y + " 不在上排 344/345 或下排 364/365");
        }
        // 四列 x=72/131/190/249，每列上下两颗；开设在 x=13。
        foreach (int column in new[] { 72, 131, 190, 249 })
        {
            int count = 0;
            foreach (var button in buttons) if (Mathf.Approximately(button.x, column)) count++;
            Check(count == 2, "列 x=" + column + " 应有 2 颗按钮，实际 " + count);
        }
        foreach (var button in buttons)
            if (button.id == 2200) Check(Mathf.Approximately(button.x, 13) && Mathf.Approximately(button.y, 355), "开设在 (13,355)");
        for (int i = 0; i < buttons.Length; i++)
            for (int j = i + 1; j < buttons.Length; j++)
            {
                var a = new Rect(buttons[i].x, buttons[i].y, buttons[i].w, buttons[i].h);
                var b = new Rect(buttons[j].x, buttons[j].y, buttons[j].w, buttons[j].h);
                Check(!a.Overlaps(b), buttons[i].node + " 与 " + buttons[j].node + " 重叠");
            }

        // 3. 列表纵向规模：组员最多 6 人，两组都要塞进 278
        Check(NativeSocialWindow.PartyContentHeight(0, 0) <= ListH, "空队伍总高 " + NativeSocialWindow.PartyContentHeight(0, 0));
        Check(NativeSocialWindow.PartyContentHeight(6, 0) <= ListH, "6 人在线总高 " + NativeSocialWindow.PartyContentHeight(6, 0));
        Check(NativeSocialWindow.PartyContentHeight(3, 3) <= ListH, "3+3 总高 " + NativeSocialWindow.PartyContentHeight(3, 3));

        // 4. 分组：channel==-2 归离线；在线组同图优先、组内按名字
        var state = new SocialResponse { partyLeader = 1 };
        state.partyMembers.Add(new SocialMember { id = 1, name = "Zeta", channel = 1, mapId = 10000 });
        state.partyMembers.Add(new SocialMember { id = 2, name = "Alpha", channel = 1, mapId = 10000 });
        state.partyMembers.Add(new SocialMember { id = 3, name = "Bravo", channel = 1, mapId = 20000 });
        state.partyMembers.Add(new SocialMember { id = 4, name = "Gone", channel = -2, mapId = 10000 });
        NativeSocialWindow.GroupMembers(state, 10000, out var online, out var offline);
        Check(online.Count == 3 && offline.Count == 1, "分组数量 " + online.Count + "/" + offline.Count);
        Check(offline.Count == 1 && offline[0].id == 4, "channel==-2 归离线组");
        Check(online.Count == 3 && online[0].name == "Alpha" && online[1].name == "Zeta" && online[2].name == "Bravo",
            "在线组排序（同图优先 + 名字升序）：" + string.Join(",", online.ConvertAll(m => m.name)));

        Debug.Log("[PartyPanelChecks] pass=" + pass + " fail=" + fail);
        if (fail > 0) EditorApplication.Exit(1);
    }
}
