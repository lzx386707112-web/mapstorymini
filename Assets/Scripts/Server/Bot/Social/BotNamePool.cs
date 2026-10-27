using System;
using System.Collections.Generic;
using System.IO;

namespace Server.Bot.Social
{
    // FMShopDescGen的名称池；中文生成名另由BotGeneration选择。
    public sealed class BotNamePool
    {
        static readonly string[] fallback = {
            "小枫", "阿糖", "云鹿", "星橙", "枫眠", "南茶", "北雪", "青竹",
            "白舟", "洛安", "夏宁", "秋澈", "雨遥", "风然", "月梨", "川豆",
            "阿禾", "晚晴", "疏影", "清欢", "拾光", "半夏", "听雨", "折柳",
            "顾南", "沈夜", "林深", "江晚", "苏晚", "陈舟", "陆青", "叶安"
        };
        readonly BotResources resources;
        readonly Random random;
        readonly List<string> assigned = new List<string>();
        List<string> pool;
        int index;
        public BotNamePool(BotResources resources, Random random = null)
        { this.resources = resources; this.random = random ?? new Random(); }
        public string CharacterName() { string name = Next(); assigned.Add(name); return name; }
        public string ShopOwnerName() => assigned.Count > 0 && random.Next(100) < 35 ? assigned[random.Next(assigned.Count)] : Next();
        public string Next()
        {
            if (pool == null || pool.Count == 0 || index >= pool.Count)
            {
                pool = new List<string>(); index = 0;
                string text = resources.ReadText("soloMapling/FreeMarket/FMNameDesc/randomRealMaplestoryIGNs.txt");
                using (var reader = new StringReader(text ?? ""))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                        if (line.Length > 0 && line.Length <= 12 && !HasLatin(line)) pool.Add(line);
                }
                if (pool.Count == 0) pool.AddRange(fallback);
                for (int i = pool.Count; i > 1; i--) { int j = random.Next(i); string value = pool[i - 1]; pool[i - 1] = pool[j]; pool[j] = value; }
            }
            return pool[index++];
        }
        static bool HasLatin(string value)
        { foreach (char c in value) if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z')) return true; return false; }
    }
}
