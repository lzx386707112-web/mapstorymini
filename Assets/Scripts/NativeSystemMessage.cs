using UnityEngine;

// 源端「系统消息」的落点分派。
//
// BeiDou.exe 只认一个包：SERVERMESSAGE，首字节是 type，后面跟正文。
// 服务端所有提示都从这里走：
//   Character.dropMessage(type, msg)  → PacketCreator.serverNotice(type, msg)
//   AbstractPlayerInteraction.dropMessage / playerMessage / mapMessage 同理
//   Character.message(msg)            → dropMessage(5, msg)
//
// 类型表（PacketCreator.serverNotice 注释 + 实际调用统计）：
//   type 0  [Notice]      聊天窗（浅蓝）+ `[Notice] ` 前缀   服务端 15 处
//   type 1  Popup         居中弹窗                          服务端 164 处 / 脚本 18 处
//   type 2  Megaphone     喇叭（跨频道）                    单机没有跨频道广播，不接
//   type 3  SuperMegaphone 超级喇叭                         同上
//   type 4  Scrolling     顶部滚动                          服务端 0 处
//   type 5  Pink          聊天窗（粉色）                    服务端大量 / 脚本 143 处
//   type 6  Lightblue     聊天窗（浅蓝）                    服务端大量 / 脚本 73 处
//   type 8  ItemMegaphone 道具喇叭（含 item info）           PacketCreator.itemMegaphone
//   type 12 yellowChat    聊天窗（黄）                       BotBossCombatManager
//
// 脚本里的典型分工（scripts/ 抽样）：
//   type 5 —— 阻止/失败：「你不能再往前了」「封印挡住了门」
//   type 6 —— 条件/说明：「需要传送卡才能启动这个传送门」
//   type 1 —— 严重错误：「背包已满」「未知错误」
//
// 迁移状态：
//   0/5/6 已按上表进聊天窗并按类型上色；
//   type 0 还要在客户端侧加 `[Notice] ` 前缀（见下）；
//   type 1 走 CUtilDlg type1000 的 Notice3 原图、14px 居中文字与右下 OK。
public static class NativeSystemMessage
{
    // type 0 的 `[Notice] ` 前缀是**客户端自己加的**，服务端正文里没有。
    // 证据：BeiDou.exe 0xA22785 是消息入口（type = 包体首字节），
    // 0xA229B0 处按 type 跳 14 项表（表在 0xA236D3），case 0 在 0xA229C0：
    //   GetString(0x37A) = "[Notice]"，再与 0xAF1E78 处的字面量 " " 拼接，最后拼正文。
    const string NoticePrefix = "[Notice] ";

    public static void Drop(int type, string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        switch (type)
        {
            case 5:
                // case 5 走 0x4906B5(msg, 12)：不带前缀、粉色
                BottomBar.AddChat(text, BottomBar.ChatPink);
                break;
            case 1:
                // CUtilDlg::OnCreate 991899 (type1000), Draw 99311B.
                NativeUtilPrompt.Notice(text);
                break;
            case 0:
                // case 0 走 0x4906B5("[Notice] " + msg, 10)
                BottomBar.AddChat(NoticePrefix + text, BottomBar.ChatLightBlue);
                break;
            case 6:
                // case 6 走 0x4906B5(msg, 10)：不带前缀、浅蓝
                BottomBar.AddChat(text, BottomBar.ChatLightBlue);
                break;
            case 12:
                // yellowChat（PacketCreator.yellowChat）：BotBossCombatManager 的黄字提示
                // （"假人已开始集火Boss。"等）。色值对齐 BottomBar.ChatYellow。
                BottomBar.AddChat(text, BottomBar.ChatYellow);
                break;
            case 8:
                // 道具喇叭（PacketCreator.itemMegaphone 写的就是 type 8）。
                // BeiDou.exe 0xA229B9 的表项：type 8/9/10 与 3 共用 0xA22D3A，
                // 末端 0xA231AB 是 `push 8 / call 0x4906B5(msg, 8)` —— 和 0/5/6 一样进聊天窗，
                // 只是类型号 8。正文由服务端拼好（假人喇叭是「名字 : 内容」），客户端不再加前缀。
                // 单机世界广播没有别的听众，这一行就是玩家看到的东西。
                BottomBar.AddChat(text, BottomBar.ChatWhite);
                break;
            default:
                BottomBar.AddChat(text, BottomBar.ChatWhite);
                break;
        }
    }

    // 地图广播：源端 mapMessage 发给整张图，单机只有自己一个玩家，等价于发给自己。
    public static void DropMap(int type, string text) { Drop(type, text); }
}
