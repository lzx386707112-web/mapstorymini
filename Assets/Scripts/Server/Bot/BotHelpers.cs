// 假人助手。对齐源端 soloMapling.ArtificialPlayer.BotHelpers 的纯逻辑部分：
//   随机中文名、isBot 判断、createRectangle、convertItemIdToName（钩子，阶段H 接 itemPool）。
// 依赖地图/物品的范围查询在 BotLogic 中（阶段C+ 才有地图运行时）。
using System;
using System.Collections.Generic;

namespace Server.Bot
{
    public static class BotHelpers
    {
        static readonly string[] ChineseNamePrefixes = {
            "小","阿","云","星","枫","南","北","青","白","洛",
            "夏","秋","雨","风","月","川","竹","桃","一","七"
        };
        static readonly string[] ChineseNameSuffixes = {
            "枫","糖","鱼","鹿","橙","眠","茶","雪","竹","舟",
            "安","宁","澈","遥","然","梨","月","星","豆","夏"
        };

        static readonly Random rng = new Random();

        public static bool IsBot(BotActor c) => c != null && IsBot(c.id);
        public static bool IsBot(int id) => id > 20000 || id == 999;

        public static string GetRandomChineseBotName() => GetRandomChineseBotName(rng);

        // 带种子的版本：假人档案要「同一份档案永远同一个名字」，
        // 所以生成档案时用固定种子走这条，运行期随机仍走上面那个。
        public static string GetRandomChineseBotName(Random random)
        {
            string p = ChineseNamePrefixes[random.Next(ChineseNamePrefixes.Length)];
            string s = ChineseNameSuffixes[random.Next(ChineseNameSuffixes.Length)];
            if (random.Next(100) < 35)
            {
                string s2 = ChineseNameSuffixes[random.Next(ChineseNameSuffixes.Length)];
                return p + s + s2;
            }
            return p + s;
        }

        // 范围矩形（像素坐标，与源端 java.awt.Rectangle 等价）
        public static (int x0, int y0, int x1, int y1) CreateRectangle(int px, int py, int w, int h)
        {
            return (px - w / 2, py - h / 2, px + w / 2, py + h / 2);
        }

        public static string ConvertItemIdToName(int itemId) => BotItemCatalog.Shared.Name(itemId) ?? "NULL";

        public static bool CheckSecondListInsideFirstList<T>(List<T> inner, List<T> outer)
        {
            foreach (var it in inner) if (!outer.Contains(it)) return false;
            return true;
        }

        public static double Distance(int x1, int y1, int x2, int y2)
        {
            int dx = x1 - x2, dy = y1 - y2;
            return Math.Sqrt(dx * dx + dy * dy);
        }
    }
}
