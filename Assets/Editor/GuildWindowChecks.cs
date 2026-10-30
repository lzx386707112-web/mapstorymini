using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// 家族面板（UserList 主窗 tab2）几何 + 按钮权限断言：无渲染，batchmode 可跑。
// 期望值全部来自 BeiDou.exe（1b91cf5b…34b2）：
//   0x919B73 主窗构建（312×389、tab 条 (6,23) 宽 270、按钮 12 组锚点）；
//   0x90F17C 家族页绘制：名条画布 (3,53)（徽章 (left,1) 20×20、名字 (left+26,3)、公告底 (0,25)、公告文字 (8,30)）
//            + 列表画布 (6,113)（281×192、组标题 27、列头 +31、行 +49 步长 18、底线 1px、组间距 10、列 x=10/100/170/210）；
//   0x91CF4F 按钮分派（跳转表 index=id-2010），权限分支在 0x90F17C。
//
//   /Applications/Unity/Unity.app/Contents/MacOS/Unity -batchmode -nographics -quit \
//     -projectPath <proj> -executeMethod GuildWindowChecks.Run -logFile /tmp/guildcheck.log
public static class GuildWindowChecks
{
    static void Require(bool ok, string message) { if (!ok) throw new Exception(message); }

    public static void Run()
    {
        int exit = 0;
        try
        {
            Core();
            Debug.Log("[GuildWindowChecks] 全部通过");
        }
        catch (Exception e)
        {
            Debug.LogError("[GuildWindowChecks] 失败：" + e.Message);
            exit = 1;
        }
        finally
        {
            if (Application.isBatchMode) EditorApplication.Exit(exit);
        }
    }

    // 纯断言体：不碰 Debug / EditorApplication，方便在 dotnet 侧直接执行（tools/guild-check）。
    public static void Core()
    {
        {
            // 1) 主窗尺寸与 tab 条
            Require(NativeGuildWindow.SizeOf() == new Vector2(312, 389), "主窗应为 312×389，实为 " + NativeGuildWindow.SizeOf());
            Require(NativeGuildWindow.TabStrip() == new Vector2(6, 23), "tab 条原点应为 (6,23)");

            // 2) 名条：画布原点 (3,53)；徽章必须落在 backgrnd2 的名条内嵌框（52/76 深线，53..75 可用）里
            Require(NativeGuildWindow.ContentOrigin() == new Vector2(3, 53), "家族页内容画布原点应为 (3,53)");
            foreach (float nameWidth in new[] { 12f, 36f, 84f, 150f })
            {
                var markRect = NativeGuildWindow.MarkRect(nameWidth);
                Require(markRect.y >= 52 && markRect.yMax <= 76,
                        "徽章压出背景名条框：y=" + markRect.y + ".." + markRect.yMax + "（框 52..76）");
                // 居中要以「徽章 + 名字」整组算，不是单看徽章
                float groupCenter = markRect.x + (26f + nameWidth) / 2f;
                Require(Mathf.Abs(groupCenter - 156f) <= 1.5f,
                        "徽章+名字整组应居中在 306 宽画布（中心 156），实在 " + groupCenter);
            }
            Require(NativeGuildWindow.MarkRect(36f) == new Rect(125, 54, 20, 20), "名字宽 36 时徽章应在 (125,54)");
            Require(NativeGuildWindow.NameTextLeft(36f) == 151f, "名字应紧贴徽章右侧 26（左侧起点 151）");
            Require(NativeGuildWindow.NameTextTop() == 56, "家族名文字 top 应为 56（画布局部 y=3）");

            // 3) 公告条与公告按钮：不重叠、不出右边框
            var notice = NativeGuildWindow.NoticeBar();
            var noticeButton = NativeGuildWindow.NoticeButton();
            Require(notice == new Rect(3, 78, 282, 20), "公告底 guildinfo6 应在 (3,78) 282×20");
            Require(notice.xMax <= noticeButton.xMin, "公告按钮压住了公告底");
            Require(noticeButton.xMax <= 312 - 5, "公告按钮越过右侧边框");

            // 4) 列表视口：281×192 @(6,113)，右边不越框、下边不压按钮首行(316)
            var view = NativeGuildWindow.ListViewport();
            Require(view == new Rect(6, 113, 281, 192), "列表视口应为 (6,113,281,192)");
            Require(view.xMax <= 312 - 5, "列表视口越右框");
            Require(view.yMax <= NativeGuildWindow.ButtonAnchor(0).y, "列表视口压住第一排按钮");

            // 5) 组高：组标题 27 + 4 + 列头 18 + 行 18*max(1,n) + 底线 1
            Require(NativeGuildWindow.GroupHeightOf(1) == 68, "空组/单人组高应为 68，实为 " + NativeGuildWindow.GroupHeightOf(1));
            Require(NativeGuildWindow.GroupHeightOf(0) == NativeGuildWindow.GroupHeightOf(1), "空组必须仍留一行");
            Require(NativeGuildWindow.GroupHeightOf(5) == 68 + 18 * 4, "5 人组高应按步长 18 增长");
            Require(NativeGuildWindow.RowTop() == 49, "首行 top 应为 49");
            Require(NativeGuildWindow.RowStep() == 18, "行步长应为 18");

            // 6) 四列 x 递增且不越视口宽
            int name = NativeGuildWindow.ColumnX(0), job = NativeGuildWindow.ColumnX(1);
            int level = NativeGuildWindow.ColumnX(2), rank = NativeGuildWindow.ColumnX(3);
            Require(name == 10 && job == 100 && level == 170 && rank == 210,
                    "列 x 应为 10/100/170/210，实为 " + name + "/" + job + "/" + level + "/" + rank);
            Require(rank + 66 <= 281, "地位列越出视口");

            // 7) 12 组按钮锚点（id 2030..2041，含公告按钮在 DrawNoticeBar 里单独画）
            var expect = new Dictionary<string, Vector2>
            {
                { "BtInvite", new Vector2(9, 316) }, { "BtWithdraw", new Vector2(80, 316) },
                { "BtWhere", new Vector2(159, 316) }, { "BtWhisper", new Vector2(238, 316) },
                { "BtUp", new Vector2(9, 336) }, { "BtDown", new Vector2(80, 336) },
                { "BtKick", new Vector2(159, 336) }, { "BtChat", new Vector2(238, 336) },
                { "BtGuildBBS", new Vector2(80, 363) }, { "BtPartyInvite", new Vector2(159, 363) },
                { "BtInfo", new Vector2(238, 363) },
            };
            Require(NativeGuildWindow.ButtonCount == expect.Count, "按钮数应为 " + expect.Count);
            for (int i = 0; i < NativeGuildWindow.ButtonCount; i++)
            {
                string node = NativeGuildWindow.ButtonNode(i);
                Require(expect.ContainsKey(node), "多出未登记按钮 " + node);
                Require(NativeGuildWindow.ButtonAnchor(i) == expect[node],
                        node + " 锚点应为 " + expect[node] + "，实为 " + NativeGuildWindow.ButtonAnchor(i));
                Require(expect[node].y + 18 <= 389, node + " 越过窗口底边");
            }

            // 7b) 绘制矩形 = 锚点 + IMG 尺寸，**不减画布 origin**（BtInfo/BtGuildBBS 的 origin=(23,9)、
            //     BtWhisper/BtChat 的 (0,2) 都只是美术锚点）。减了就变成：第三行 57..104 / 159..212 / 215..262
            //     ——「组队邀请」和「资料」只隔 3px，且 BtPartyInvite 比两侧低 9px（用户截图里的错位）。
            //     期望尺寸即 IMG 的 normal/0 画布实测值。
            var sizes = new Dictionary<string, Vector2>
            {
                { "BtInvite", new Vector2(53, 18) }, { "BtWithdraw", new Vector2(53, 18) },
                { "BtWhere", new Vector2(53, 18) }, { "BtWhisper", new Vector2(46, 17) },
                { "BtUp", new Vector2(53, 18) }, { "BtDown", new Vector2(53, 18) },
                { "BtKick", new Vector2(53, 18) }, { "BtChat", new Vector2(46, 17) },
                { "BtGuildBBS", new Vector2(47, 18) }, { "BtPartyInvite", new Vector2(53, 18) },
                { "BtInfo", new Vector2(47, 18) },
            };
            var rects = new Dictionary<string, Rect>();
            for (int i = 0; i < NativeGuildWindow.ButtonCount; i++)
            {
                string node = NativeGuildWindow.ButtonNode(i);
                var anchor = NativeGuildWindow.ButtonAnchor(i);
                var rect = NativeGuildWindow.PlaceButton(i, sizes[node]);
                Require(rect.x == anchor.x && rect.y == anchor.y, node + " 绘制左上角必须等于锚点（不减 origin）");
                Require(rect.xMax <= 287, node + " 右边缘应齐列表视口右界 287，实为 " + rect.xMax);
                Require(rect.yMax <= 389, node + " 越过窗口底边 389");
                rects[node] = rect;
            }
            // 三行共用同一组列：行1/行2 的四列 = 9/80/159/238，行3 的三列 = 80/159/238
            string[][] rows =
            {
                new[] { "BtInvite", "BtWithdraw", "BtWhere", "BtWhisper" },
                new[] { "BtUp", "BtDown", "BtKick", "BtChat" },
                new[] { "BtGuildBBS", "BtPartyInvite", "BtInfo" },
            };
            for (int r = 0; r < rows.Length; r++)
                for (int c = 1; c < rows[r].Length; c++)
                    Require(rects[rows[r][c - 1]].xMax < rects[rows[r][c]].x, 
                            "第 " + (r + 1) + " 行 " + rows[r][c] + " 与左侧按钮重叠");
            Require(rects["BtGuildBBS"].x == rects["BtWithdraw"].x, "第三行首列应与第 2 列对齐（80）");
            Require(rects["BtPartyInvite"].x == rects["BtWhere"].x, "第三行中列应与第 3 列对齐（159）");
            Require(rects["BtInfo"].x == rects["BtWhisper"].x, "第三行末列应与第 4 列对齐（238）");
            Require(rects["BtInfo"].y == rects["BtGuildBBS"].y && rects["BtInfo"].y == rects["BtPartyInvite"].y,
                    "第三行三个按钮必须同高（同一条 y=363）");

            // 8) 权限规则（服务端 NativeCommunityService + 源端 0x90F17C 同口径）
            Require(!HasAny(NativeGuildWindow.EnabledRules(null, null)), "无状态时所有按钮都应禁用");

            var noGuild = new CommunityResponse { guildId = 0, guild = new List<CommunityMember>() };
            Require(!HasAny(NativeGuildWindow.EnabledRules(noGuild, null)), "未入家族时所有按钮都应禁用");

            var st = Guild();
            var me = st.guild[0];
            var other = st.guild[1];           // rank3 在线，非自己
            var lowOff = st.guild[2];          // rank5 离线

            var eSelf = NativeGuildWindow.EnabledRules(st, me);
            Require(!eSelf[2] && !eSelf[3] && !eSelf[9], "选中自己时位置/悄悄话/组队邀请必须禁用");
            Require(eSelf[0] && !eSelf[1], "族长：邀请可用、退出禁用（只能解散）");
            Require(eSelf[7] && eSelf[10], "族长：家族聊天与资料可用");

            var eOther = NativeGuildWindow.EnabledRules(st, other);
            Require(eOther[2] && eOther[3] && eOther[9], "在线他人：位置/悄悄话/组队邀请可用");
            Require(eOther[4], "targetRank 3 > myRank+1 → 升职可用");
            Require(eOther[5], "targetRank 3 < 5 → 降职可用");
            Require(eOther[6], "targetRank 3 > myRank 1 → 驱逐可用");
            Require(!eOther[8], "公告板（GuildBBS 独立窗）未迁移前必须禁用");

            var eOff = NativeGuildWindow.EnabledRules(st, lowOff);
            Require(!eOff[2] && !eOff[3] && !eOff[9], "离线成员：位置/悄悄话/组队邀请必须禁用");
            Require(!eOff[5], "targetRank 5 已是最低职位，降职必须禁用");

            // 副族长（rank2）：不能任命副族长（rank2 只有族长能给），但可以管 rank3 及以下
            var st2 = Guild(); st2.guild[0].rank = 2;
            var eJr = NativeGuildWindow.EnabledRules(st2, st2.guild[1]);
            Require(!eJr[4], "副族长不能把人升到 rank2（只有族长能任命副族长）");
            Require(eJr[5] && eJr[6], "副族长应能降职/驱逐比自己职位低的 rank3 成员");
            var stSame = Guild(); stSame.guild[0].rank = 2; stSame.guild[1].rank = 2;
            var eSame = NativeGuildWindow.EnabledRules(stSame, stSame.guild[1]);
            Require(!eSame[5] && !eSame[6], "同级（rank2）之间不能降职或驱逐");

            // 普通族员：除了聊天/资料全灰
            var st3 = Guild(); st3.guild[0].rank = 4;
            var eMember = NativeGuildWindow.EnabledRules(st3, st3.guild[1]);
            Require(!eMember[0] && !eMember[4] && !eMember[5] && !eMember[6], "普通族员不应有管理按钮");
            Require(eMember[7] && eMember[10], "普通族员仍可用家族聊天与资料");

            // 9) 资料子窗 GuildGrade：233×304，五行 top=146 步长 18，标题列 146 宽 86
            Require(NativeGuildGradeWindow.SizeOf() == new Vector2(233, 304), "资料窗应为 233×304");
            Require(NativeGuildGradeWindow.RowsTopY() == 146 && NativeGuildGradeWindow.RowPitch() == 18, "资料窗行表 146/18");
            Require(NativeGuildGradeWindow.RowCount() == 5, "资料窗应为五行职位");
            Require(NativeGuildGradeWindow.TitleColumn() + NativeGuildGradeWindow.TitleColumnWidth() <= 233, "资料窗标题列越界");
            Require(NativeGuildGradeWindow.RowsTopY() + (NativeGuildGradeWindow.RowCount() - 1) * NativeGuildGradeWindow.RowPitch() + 18 <= 268, "资料窗行表压住修改/保存按钮");
        }
    }

    static bool HasAny(bool[] flags)
    {
        foreach (bool f in flags) if (f) return true;
        return false;
    }

    // 构造一份判权限用的 DTO：id 0 = 自己（GameSession.CharacterId 在批处理下是 0）
    static CommunityResponse Guild()
    {
        return new CommunityResponse
        {
            guildId = 1, guildName = "未来岛", capacity = 30, gp = 12000,
            titles = new[] { "族长", "副族长", "优秀族员", "正式族员", "实习生" },
            guild = new List<CommunityMember>
            {
                new CommunityMember { id = 0, name = "我", rank = 1, level = 60, job = 112, online = true, map = 100000000 },
                new CommunityMember { id = 7, name = "甲", rank = 3, level = 40, job = 220, online = true, map = 101000000 },
                new CommunityMember { id = 8, name = "乙", rank = 5, level = 10, job = 0, online = false, map = 0 },
            },
        };
    }
}
