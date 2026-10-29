using System;

namespace Server.Bot
{
    // MarketShopTitles.java 原端中文池。
    internal static class MarketShopTitles
    {
        internal static readonly string[] COMMON = {"杂货小店","路过看看","低价出货","随便逛逛","清仓处理","今日特价","便宜好货","来看看货","小本买卖","急出一批","友情价出","打包处理","走过路过","别错过","好货便宜"};
        internal static readonly string[] THIEF = {"飞镖专卖","飞镖手套","盗贼用品","标类好货","飞镖便宜出","暗影装备","飞镖手套店","盗贼杂货"};
        internal static readonly string[] WARRIOR = {"战士装备","力量装专卖","战士杂货","近战装备","力量装便宜"};
        internal static readonly string[] MAGE = {"法师装备","智力装专卖","魔法用品","法师杂货","智力装便宜"};
        internal static readonly string[] BOWMAN = {"弓手装备","敏捷装专卖","弓矢专卖","弓手杂货","敏捷装便宜"};
        internal static readonly string[] CHAIR = {"椅子专卖","好看椅子","休息椅子","椅子便宜出","稀有椅子"};
        internal static readonly string[] SCROLLS = {"卷轴专卖","祝福卷便宜","白卷混沌","砸卷处理","卷轴清仓","黑暗卷轴","成功卷便宜"};
        internal static readonly string[] USEABLE = {"药水专卖","消耗品店","药水便宜","苹果白卷","补给小店"};
        internal static readonly string[] ETC = {"材料处理","杂物便宜","其他材料","材料清仓","路过出材料"};
        internal static readonly string[] CLAN = {"枫之商会","金币联盟","自由市集","夜市商行","红叶商会","南风货栈","北雪商行","青竹铺子","月梨商会","白舟货行"};
        internal static readonly string[] SHORT = {"看看","便宜","可刀","秒出","清仓","好货","急出","来看","别错过","走过路过"};
        internal static readonly string[] EMOJI = {"★☆","※※","～～","嘿嘿","呵呵","哦豁","来来"};
        internal static readonly string[] OFFER = {"可议","可刀","留价","给价","可小刀","价好说"};
        internal static readonly string[] CURRENCY = {"收点券","收抵用","收金币","点券可换","抵用可议"};
        internal static bool HasLatin(string text)
        { if (text != null) foreach (char c in text) if (c >= 'A' && c <= 'Z' || c >= 'a' && c <= 'z') return true; return false; }
    }
}
