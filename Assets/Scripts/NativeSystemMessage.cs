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
//   type 0  [Notice]      聊天窗（浅蓝）   服务端 15 处
//   type 1  Popup         居中弹窗         服务端 164 处 / 脚本 18 处
//   type 2  Megaphone     喇叭（跨频道）   单机没有跨频道广播，不接
//   type 3  SuperMegaphone 超级喇叭        同上
//   type 4  Scrolling     顶部滚动         服务端 0 处
//   type 5  Pink          聊天窗（粉色）   服务端大量 / 脚本 143 处
//   type 6  Lightblue     聊天窗（浅蓝）   服务端大量 / 脚本 73 处
//
// 脚本里的典型分工（scripts/ 抽样）：
//   type 5 —— 阻止/失败：「你不能再往前了」「封印挡住了门」
//   type 6 —— 条件/说明：「需要传送卡才能启动这个传送门」
//   type 1 —— 严重错误：「背包已满」「未知错误」
//
// 迁移状态：
//   0/5/6 已按上表进聊天窗并按类型上色；
//   type 1 的居中弹窗还没做（缺 `UI/Basic.img/Notice2` 9 段 + `BtOK` 的版面参数），
//   暂时退回 Toast，等确认版面后再补。
public static class NativeSystemMessage
{
    public static void Drop(int type, string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        switch (type)
        {
            case 5:
                BottomBar.AddChat(text, BottomBar.ChatPink);
                break;
            case 1:
                // 原端是居中弹窗（点 OK 关闭）；弹窗版面未核实，先按提示条显示。
                BottomBar.Toast(text);
                break;
            case 0:
            case 6:
                BottomBar.AddChat(text, BottomBar.ChatLightBlue);
                break;
            default:
                BottomBar.AddChat(text, BottomBar.ChatWhite);
                break;
        }
    }

    // 地图广播：源端 mapMessage 发给整张图，单机只有自己一个玩家，等价于发给自己。
    public static void DropMap(int type, string text) { Drop(type, text); }
}
