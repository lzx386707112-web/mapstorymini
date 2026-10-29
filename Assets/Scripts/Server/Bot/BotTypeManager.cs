// 假人类型管理。逐条对齐源端 soloMapling.ArtificialPlayer.BotTypeManager：
//   17 种 BotType 枚举 + createAndSetBot（创建具体 BotSM 子类并注册到 BotStore）
//   + manuallyStartBot/manuallyStopBot/convertBotType + setAndStartBots/startBots/stopBots/setBotTypes。
// SOCIAL_BOT使用具体实现，其余类型的GenericBot仍表示未迁移完成。
using System;
using System.Collections.Generic;
using Server.Bot.Social;
using Server.Bot.Types;
using Server.Bot.Types.Blackjack;
using Server.Bot.Decorator;
using Server.Bot.Trade;

namespace Server.Bot
{
    public enum BotType
    {
        DICE_BOT,
        TUTORIAL_BOT,
        FM_BOT,
        SCROLL_BOT,
        SELLING_MERCHANT_BOT,
        BUYING_MERCHANT_BOT,
        NX_MERCHANT_BOT,
        GACHA_BOT,
        HENESYS_BOT,
        HENESYS_JQ_BOT,
        GAME_ZONE_HOST_BOT,
        BLACKJACK_DEALER,
        DROP_GAME_BOT,
        OPQ_BOT,
        SOCIAL_BOT,
        TOWN_WANDERER_BOT,
        COMPANION_GRIND_BOT,
    }

    public static class BotTypeManager
    {
        public static Func<int, bool> RealPlayersOnMap;
        static readonly Random rng = new Random();
        public static readonly BotMessages Messages = new BotMessages();
        public static readonly BotOutput Output = new BotOutput();
        public static readonly BotTradeLogic Trades = new BotTradeLogic();
        public static readonly MarketBotFlavor MarketFlavor = new MarketBotFlavor();
        public static readonly MarketBotDirector Market = new MarketBotDirector(() => BotClient.GetBotClient()?.db);
        static BotDialogue dialogue;
        /// <summary>共享对话资源（各类型按需取同一份，避免重复解析 YAML 包）。</summary>
        public static BotDialogue Dialogue => dialogue ??= new BotDialogue(BotResources.Load());
        static MarketItemPool items;
        static MarketEconomy economy;
        static ScrollingBot CreateScrollingBot(BotCharacter actor)
        {
            var catalog=BotItemCatalog.Shared;
            var pool=items ??= new MarketItemPool(BotResources.Load());
            var equipment = new MarketEquipmentPool(pool,catalog,new EquipMetadataCache(catalog,EquipMetadataCache.LoadPools()),BotClient.GetBotClient().db);
            return new ScrollingBot(actor,()=>equipment.ForJob("A",0),new MarketScrollPricing(pool,catalog),
                economy ??= new MarketEconomy(),dialogue ??= new BotDialogue(BotResources.Load()));
        }
        public static BotPartyGrindService PartyGrind;
        public static BotPartyService Parties;
        public static BotTradeService TradeService;
        public static Func<BotActor, bool> GrantTutorialAdmin;
        public static Action<BotActor, int> ApplySkillEffect;
        public static BotMessenger Messenger = new BotMessenger();
        static MarketBotAmbient ambient;
        public static MarketBotAmbient Ambient => ambient ??= new MarketBotAmbient(BotResources.Load(), () => BotClient.GetBotClient()?.db);
        public static readonly Events.EventBus Events = new Events.EventBus();
        public static readonly BotFlavor Flavor = new BotFlavor();

        // 创建并注册具体 BotSM 子类。
        public static BotStateMachine CreateAndSetBot(BotCharacter c, BotType t)
        {
            BotStateMachine sm = t switch
            {
                BotType.COMPANION_GRIND_BOT => new CompanionGrindBot(c, PartyGrind),
                BotType.SCROLL_BOT => CreateScrollingBot(c),
                BotType.FM_BOT => new FMBot(c, Ambient),
                BotType.SELLING_MERCHANT_BOT => new MerchantBot(c, false, items ??= new MarketItemPool(BotResources.Load()), economy ??= new MarketEconomy(), Ambient),
                BotType.BUYING_MERCHANT_BOT => new MerchantBot(c, true, items ??= new MarketItemPool(BotResources.Load()), economy ??= new MarketEconomy(), Ambient),
                BotType.NX_MERCHANT_BOT => new NXMerchantBot(c, Ambient, Messenger, new BotNXCodes(BotClient.GetBotClient().db), dialogue ??= new BotDialogue(BotResources.Load())),
                BotType.SOCIAL_BOT => new SocialBot(c, dialogue ??= new BotDialogue(BotResources.Load()),
                    Output, Messages, () => BotFurniture.RandomChair(rng)),
                BotType.TOWN_WANDERER_BOT => new TownWandererBot(c, dialogue ??= new BotDialogue(BotResources.Load()), Flavor, Events),
                BotType.HENESYS_BOT => new HenesysBot(c, dialogue ??= new BotDialogue(BotResources.Load()), Flavor,
                    BotConfig.HenesysBotsChangeMapsEnabled(BotClient.GetBotClient()?.db)),
                BotType.BLACKJACK_DEALER => new BlackjackDealerBot(c, Messages),
                BotType.OPQ_BOT => new OpqBot(c),
                BotType.HENESYS_JQ_BOT => new HenesysJQBot(c),
                BotType.DROP_GAME_BOT => new DropGameBot(c, Parties),
                BotType.TUTORIAL_BOT => new TutorialBot(c, TradeService),
                BotType.DICE_BOT => new DiceBot(c, Messages),
                BotType.GACHA_BOT => new GachaBot(c, Events, Dialogue),
                BotType.GAME_ZONE_HOST_BOT => new GameZoneHostBot(c, Events, Dialogue, Messages),
                _ => new GenericBot(c),
            };
            if (sm is GenericBot) sm.botType = t.ToString();
            sm.IsMapObserved = () => Environment.BotMovementService.Observed(c.mapId);
            sm.HasPlayersOnMap = () => RealPlayersOnMap != null && RealPlayersOnMap(c.mapId);
            BotStore.AddActiveBot(c.id, sm);
            return sm;
        }

        public static void ManuallyStartBot(BotCharacter c)
        {
            if (c == null) return;
            BotStateMachine bot = BotStore.GetBotById(c.id);
            if (bot == null || bot.running) return;
            bot.running = true;
            // 延迟首 tick 越过出生动画窗口（对齐源端），随机错峰
            long initialDelay = BotGeneration.SpawnChoreographyMaxMs + rng.Next(0, 3000);
            bot.StartScheduledTask(initialDelay);
        }

        public static void ManuallyStopBot(BotCharacter c)
        {
            if (c == null) return;
            BotStateMachine bot = BotStore.GetBotById(c.id);
            if (bot == null) return;
            bot.running = false;
            bot.StopScheduledTask();
        }

        public static void ConvertBotType(BotCharacter c, BotType t)
        {
            ManuallyStopBot(c);
            CreateAndSetBot(c, t);
            ManuallyStartBot(c);
        }

        public static void SetBotTypes(List<int> botIds, BotType t)
        {
            foreach (int id in botIds)
            {
                BotCharacter c = BotRegistry.Get(id);
                if (c == null) continue;
                CreateAndSetBot(c, t);
            }
        }

        public static void StartBots(List<int> botIds)
        {
            foreach (int id in botIds)
            {
                BotCharacter c = BotRegistry.Get(id);
                if (c == null) continue;
                ManuallyStartBot(c);
            }
        }

        public static void StopBots(List<int> botIds)
        {
            foreach (int id in botIds)
            {
                BotCharacter c = BotRegistry.Get(id);
                if (c == null) continue;
                ManuallyStopBot(c);
            }
        }

        public static void SetAndStartBots(List<int> botIds, BotType t)
        {
            foreach (int id in botIds)
            {
                BotCharacter c = BotRegistry.Get(id);
                if (c == null) continue;
                try
                {
                    CreateAndSetBot(c, t);
                    ManuallyStartBot(c);
                }
                catch (Exception)
                {
                    // BOTLOG: 失败时跳过该 bot（对齐源端 debugprint）
                }
            }
        }
    }

    // 阶段A 占位 BotSM：站桩（无行为）。后续阶段按类型替换为具体子类。
    internal sealed class GenericBot : BotStateMachine
    {
        public GenericBot(BotCharacter c) : base(c) { }
        public override void UpdateState() { /* 站桩；具体行为在对应阶段实现 */ }
    }
}
