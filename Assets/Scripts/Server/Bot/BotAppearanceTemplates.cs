using System;
using System.Collections.Generic;

namespace Server.Bot
{
    // 一套固定的假人外观 —— 等价于「一个真实玩家账号的可见外观」。
    //
    // 为什么需要它：源端假人是每次运行全空间随机生成外观（BotDecorate.ApplyAppearance），
    // 而纸娃娃是 CPU 逐像素合成成单张纹理、缓存 key 由外观 id 拼成
    // （WzCharacter.Compose → appearance://<key>/<pose>/<frame>）。
    // 外观唯一 ⇒ key 唯一 ⇒ 缓存命中率 0 ⇒ 每个假人每个动作帧都要重新合成一次（3~10ms）。
    // 收敛到固定模板池后，同屏假人共享外观，合成次数上限从「人数×帧数」降到「模板数×帧数」。
    //
    // 生成逻辑本身完全没改：模板就是拿原装饰器随机出来的结果快照，所以外观分布、
    // 装备搭配、职业/等级规律都还是源端那一套，只是候选集合变小了。
    public sealed class BotAppearanceTemplate
    {
        public int gender, skinColor, hair, face;
        public int level, job;
        public BotTier tier = BotTiers.Default;
        public int[] slots = Array.Empty<int>();
        public int[] items = Array.Empty<int>();
        // 与 BotAppearance.CharacterIds 输出一致；预合成就靠这两个字段定位缓存。
        public string[] ids = Array.Empty<string>();
        public string key = "";

        // 把外观套到假人身上。equips 必须深拷贝：假人交易会改装备，
        // 共享同一个 Dictionary 会把模板本身改掉（下次同模板假人就变了）。
        public void ApplyTo(BotCharacter bot)
        {
            bot.gender = gender; bot.skinColor = skinColor; bot.hair = hair; bot.face = face;
            bot.level = level; bot.job = job; bot.tier = tier;
            bot.equips.Clear();
            for (int i = 0; i < slots.Length; i++) bot.equips[slots[i]] = items[i];
        }
    }

    public static class BotAppearanceTemplates
    {
        // 固定种子：模板集合每次启动完全一致。这相当于「把假人外观存进了数据库」，
        // 但不新增持久化表 —— 玩家下次进来看到的是同一批脸，联机感更好，
        // 而且预合成的缓存 key 也稳定。
        const int Seed = 20261027;
        public const int DefaultCount = 32;

        static readonly List<BotAppearanceTemplate> pool = new List<BotAppearanceTemplate>();
        static Decorator.BotDecorate builder;
        static int builtCount = -1;

        public static IReadOnlyList<BotAppearanceTemplate> All => pool;
        public static bool Enabled => pool.Count > 0;

        public static void Reset()
        {
            pool.Clear(); builder = null; builtCount = -1;
        }

        // 幂等；模板数由 solo_mapling_appearance_templates 控制，0 = 关闭（回退到每次全随机）。
        public static void Ensure(DbStore db)
        {
            int wanted = Math.Max(0, BotConfig.AppearanceTemplateCount(db));
            if (builtCount == wanted) return;
            pool.Clear(); builtCount = wanted;
            if (wanted == 0) return;
            builder ??= new Decorator.BotDecorate(BotResources.Load(), BotItemCatalog.Shared,
                BotTypeManager.Output, new Random(Seed));
            for (int i = 0; i < wanted; i++) pool.Add(Build(db));
        }

        public static BotAppearanceTemplate Take(Random random)
            => pool.Count == 0 ? null : pool[random.Next(pool.Count)];

        static BotAppearanceTemplate Build(DbStore db)
        {
            var bot = new BotCharacter();
            builder.ApplyAppearance(bot, db);
            var template = new BotAppearanceTemplate
            {
                gender = bot.gender, skinColor = bot.skinColor, hair = bot.hair, face = bot.face,
                level = bot.level, job = bot.job, tier = bot.tier,
            };
            var slots = new List<int>(bot.equips.Count);
            var items = new List<int>(bot.equips.Count);
            foreach (var pair in bot.equips) { slots.Add(pair.Key); items.Add(pair.Value); }
            template.slots = slots.ToArray(); template.items = items.ToArray();
            template.ids = BotAppearance.CharacterIds(bot);
            template.key = "bot_" + string.Join("_", template.ids);
            return template;
        }
    }
}
