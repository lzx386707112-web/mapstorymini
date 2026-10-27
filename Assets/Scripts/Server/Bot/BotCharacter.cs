// 假人活体角色实体。对齐源端 org.gms.client.Character 在 SoloMapling 中使用的最小字段集
// （id/name/level/job/gender/外观/pos/stance/chair/背包钩子）。
// 安卓后端无运行期 Character 实体，此处新建轻量实体承载 bot 状态；渲染由前端 BotView 消费。
using System.Collections.Generic;

namespace Server.Bot
{
    public sealed class BotCharacter : BotActor
    {
        public BotTier tier = BotTiers.Default;
        public BotWander wander;
        internal BotTasks.Handle arrivalTask;
        public bool moving;
        public int facing = -1;
        public int stance = 5;         // 源端 IDLE_LEFT=5，IDLE_RIGHT=4
        public int chair;              // 椅子 id，>0 表示坐着
        public int accId;
        public int gmLevel;
        public int str = 12, dex = 5, intelligence = 4, luk = 4;
        public int meso;
        public readonly List<InventoryEntry> inventory = new List<InventoryEntry>();

        // 外观装备（装饰系统填充，阶段E）：slot(负) -> itemId
        public Dictionary<int, int> equips = new Dictionary<int, int>();
        public readonly Dictionary<int, InventoryEntry> equipment = new Dictionary<int, InventoryEntry>();

        // 独立mock连接（player指向当前bot，standalone host只作为生成模板）
        public BotClient client;

        public override bool IsBot() => true;   // 区分真实玩家
    }
}
