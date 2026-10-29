// TutorialBot.java（599 行）精确移植：迎新人 → 问询 → 主任与否 → 送礼交易 → 三段教学
// （说话/选帽子/看武器）→ 送别 → 走回待客点。
//
// 与源端的差异（全部来自「没有独立线程 / 没有阻塞 sleep」和能力边界）：
//   1. 源端 sleepAmountSeconds(x) 阻塞整个 tick；这里一律换成「BotScheduler.Now() 时间戳 +
//      BotTasks 可取消延时」的续接链，并在链未跑完时用 scripted 标记让内部状态不推进
//      （等价于源端那条线程卡在 sleep 里），与 DropGameBot 的 scripted/Later 同款写法。
//   2. 源端 executeBotDialogue 是阻塞的（说完才往下走）；本端口的 BotDialogue.Run 是分帧的，
//      因此每个状态的迁移都挂在 Run 的完成回调里。
//   3. 源端 processMessages() 轮询共享聊天队列 "secondary"（1 秒超时阻塞）。本端口只有
//      BotRuntime.ReceiveChat 灌的 PRIMARY 队列真正承载玩家聊天（见 BotDispatcher），
//      所以这里按 Blackjack 的「取出来不是我的就放回去」语义先试 SECONDARY 再回落 PRIMARY。
//   4. 角色成长由 GameSession 注入现有 LevelSystem / JobSystem，交易由 BotRuntime 注入。
//      原始对白和录制仍从 SoloMapling 资源包读取，武器强化取原卷轴属性。
using System;
using System.Collections.Generic;
using Server.Bot.Environment;
using Server.Bot.Social;
using Server.Bot.Trade;

namespace Server.Bot.Types
{
    public sealed class TutorialBot : BotStateMachine
    {
        // ≡ TutorialBot.TutorialBotState（18 态）
        public enum TutorialState
        {
            Reset, WaitForPlayerInRange, Start, Inquire, WaitForInquiryResponse, AskAdmin,
            WaitAdminResponse, GrantAdmin, GiftTrade, WaitTradeAccept, Tutorial1, Tutorial2,
            Tutorial2WaitPick, Tutorial3, Tutorial3WaitDrop, Tutorial3Upgrade, SendOff, Return
        }

        const string DialoguePack = "TutorialBotDialogue.yaml";
        // ≡ returnToWaitingSpot 的 getMovementRecording(mapId, "tutorial2")
        const string ReturnRecording = "tutorial2";

        // Mark of Beta, White Maple Dana, Newspaper Hat
        static readonly int[] Hats = { 1002419, 1002515, 1002418 };

        const int PowerElixir = 2000005;
        const int OnyxApple = 2022179;
        const int TownScrollHenesys = 2030000;
        const int IlbiStars = 2070006;
        const int TradeMesos = 1_000_000_000;
        const int MapleKandayo = 1472032;
        const int BlueSaunaRobe = 1050018;
        const int RedSaunaRobe = 1051017;
        // ≡ analyze/scroll 用的固定卷轴 2043003（数据里 success=100 / incPAD 5 / incSTR 3 / incPDD 1）
        const int WeaponScroll = 2043003;
        const int NxCreditPerCode = 25000;

        // ≡ 源端各处 endTime = startTime + N * 1000
        const long InquiryTimeoutMs = 20_000;
        const long AdminTimeoutMs = 30_000;
        const long TradeTimeoutMs = 30_000;
        const long PickTimeoutMs = 40_000;

        // ≡ escort_player_to_portal 的 new Point(927, 485)
        const int PortalX = 927, PortalY = 485;

        // ≡ BodyPart.WEAPON(11) → getSlotDstValueByEquipSlotName → -11（与 CharFactory.EquipPosWeapon 同源）
        const int WeaponSlot = CharFactory.EquipPosWeapon;

        readonly Random random;
        readonly BotDialogue dialogue;
        readonly BotMessages messages;
        readonly BotTradeService trades;
        MarketScrollPricing pricing;          // null → 武器强化降级
        BotNXCodes nxCodes;
        bool pricingTried;

        public TutorialState tutorialBotState { get; private set; } = TutorialState.Reset;

        bool? runTutorial, tutPicked, wantsAdmin;
        string generatedNXCode, generatedNXCode2;
        readonly List<int> noobsHelped = new List<int>();
        List<string> hint;
        int wepId;
        InventoryEntry playersWep;
        long startTime, endTime;
        // 源端在这些地方是阻塞 sleep：期间整条 tick 不推进，这里用同义标记（≡ DropGameBot.scripted）
        bool scripted;
        int generation;
        Func<bool> Guard() { int current = generation; return () => current == generation && Alive(); }
        BotTasks.Handle sequenceTask;

        public TutorialBot(BotCharacter c, BotTradeService trades = null, BotDialogue dialogue = null,
            BotMessages messages = null, Random random = null) : base(c)
        {
            this.trades = trades;
            this.dialogue = dialogue ?? BotTypeManager.Dialogue;
            this.messages = messages ?? BotTypeManager.Messages;
            this.random = random ?? new Random();
            hint = new List<string> { c.name };
            startTime = BotScheduler.Now();
            botType = "TutorialBot";
        }

        // 带着拜访中的玩家时把 tick 钉快一点，别让答复等一个 10 秒周期。
        public override void CheckPrioritySpeed()
        {
            if (interactors.Respondant != null) UpdateScheduleDelay(1000);
            else base.CheckPrioritySpeed();
        }

        // =========================================================================
        // MAIN STATE MACHINE
        // =========================================================================
        public override void UpdateState()
        {
            base.UpdateState();
            if (CheckIfNotRunningOrPaused()) return;
            DebugLog(chr.name + " TutorialBotState: " + tutorialBotState);
            // 正在跑「阻塞 sleep 等价」的续接链：这一轮 tick 不推进。
            if (scripted) return;

            switch (tutorialBotState)
            {
                case TutorialState.Reset:
                    ResetTutorialBotState();
                    SetState(TutorialState.WaitForPlayerInRange);
                    break;
                case TutorialState.WaitForPlayerInRange:
                    WaitForNewPlayer();
                    BotWaitingForPlayerFlavorText();
                    // flavor 还在说（= 源端卡在这个方法的 sleep 里）：下一轮再决定是否开课，避免两段台词叠着播。
                    if (scripted) break;
                    if (interactors.Respondant != null) SetState(TutorialState.Start);
                    break;
                case TutorialState.Start:
                    GreetPlayer();
                    break;
                case TutorialState.Inquire:
                    InquirePlayer();
                    break;
                case TutorialState.WaitForInquiryResponse:
                    WaitForResponse();
                    if (runTutorial == null) break;      // 源端 return
                    Continue(runTutorial.Value ? TutorialState.AskAdmin : TutorialState.SendOff);
                    break;
                case TutorialState.AskAdmin:
                    AskAdminRights();
                    break;
                case TutorialState.WaitAdminResponse:
                    WaitForResponse();
                    if (wantsAdmin == null) break;
                    Continue(wantsAdmin.Value ? TutorialState.GrantAdmin : TutorialState.Tutorial1);
                    break;
                case TutorialState.GrantAdmin:
                    GrantAdminRights();
                    break;
                case TutorialState.GiftTrade:
                    InitiateGiftTrade();
                    break;
                case TutorialState.WaitTradeAccept:
                    if (WaitForTradeAccept()) PlaceTradeItems();
                    break;
                case TutorialState.Tutorial1:
                    Tutorial1();
                    break;
                case TutorialState.Tutorial2:
                    Tutorial2();
                    break;
                case TutorialState.Tutorial2WaitPick:
                    WaitForResponse();
                    if (tutPicked == null) break;
                    if (tutPicked.Value) Continue(TutorialState.Tutorial3);
                    break;
                case TutorialState.Tutorial3:
                    AnalyzePlayersWeapon();
                    break;
                case TutorialState.Tutorial3WaitDrop:
                    WaitForPlayerToDropWeapon();
                    if (playersWep == null) break;
                    Continue(TutorialState.Tutorial3Upgrade);
                    break;
                case TutorialState.Tutorial3Upgrade:
                    UpgradeAndReturnWeapon();
                    break;
                case TutorialState.SendOff:
                    SendOff();
                    break;
                case TutorialState.Return:
                    ReturnOrigin();
                    break;
                default:
                    DebugLog("Unexpected TutorialBot state: " + tutorialBotState);
                    state = State.FINISHED;
                    ResetTutorialBotState();
                    break;
            }
        }

        public override void DisplayCommands(BotActor player)
        {
            if (player == null || hint == null) return;
            output.ShowOptions(player, hint);
        }

        // =========================================================================
        // STATE HELPERS
        // =========================================================================
        BotActor Player => interactors.Respondant;

        void SetState(TutorialState next) => tutorialBotState = next;

        // 一段异步流程的收尾：解开 scripted 闸门并迁移状态。
        void Continue(TutorialState next) { scripted = false; SetState(next); }

        bool Alive() => running && chr.online && ReferenceEquals(BotStore.GetBotById(chr.id), this);

        void StartTimer(long durationMs)
        { startTime = BotScheduler.Now(); endTime = startTime + durationMs; }

        void ResetTutorialBotState()
        {
            SetState(TutorialState.Reset);
            runTutorial = null;
            tutPicked = null;
            wantsAdmin = null;
            generatedNXCode = null;
            generatedNXCode2 = null;
            hint = new List<string> { chr.name };
            wepId = 0;
            playersWep = null;
            startTime = BotScheduler.Now();
            endTime = 0;
            scripted = false;
            sequenceTask?.Cancel();
            sequenceTask = null;
        }

        // 源端 sleepAmountSeconds(x) 的可取消等价物（≡ DropGameBot.Later）
        void After(long ms, Action next)
        {
            scripted = true;
            sequenceTask?.Cancel();
            var keepGoing = Guard();
            sequenceTask = BotTasks.Schedule(() =>
            {
                if (!keepGoing()) return;
                try { next(); }
                catch (Exception e) { DebugLog(chr.name + " sequence error: " + e.Message); scripted = false; }
            }, ms);
        }

        // 不阻塞状态机的单次延时（用于「说话完先睡 2.5 秒再回收帽子」这类副作用）
        void Schedule(long ms, Action work)
        {
            var keepGoing = Guard();
            BotTasks.Schedule(() =>
            {
                if (!keepGoing()) return;
                try { work(); }
                catch (Exception e) { DebugLog(chr.name + " scheduled task error: " + e.Message); }
            }, ms);
        }

        // ≡ getDialogueHandler().executeBotDialogue*：说完（含台词间隔与结束 wait）才回调 then
        void Say(string node, IDictionary<string, string> replacements = null, Action then = null, bool flavor = false)
        {
            BotDialogue.Dialogue entry;
            try { entry = dialogue.Get(DialoguePack, botType, node); }
            catch (Exception e) { DebugLog("dialogue read failed " + node + ": " + e.Message); entry = null; }
            if (entry == null || entry.text == null || entry.text.Count == 0) { then?.Invoke(); return; }
            if (replacements != null)
            {
                try { entry.text = BotDialogue.ReplaceStrings(entry.text, replacements); }
                catch (Exception e) { DebugLog("dialogue replace failed " + node + ": " + e.Message); }
            }
            scripted = true;
            dialogue.Run(chr, entry, output, flavor, () => { if (!Alive()) return; scripted = false; then?.Invoke(); }, Guard());
        }

        void Face(BotActor player)
        { if (player != null) chr.facing = player.x >= chr.x ? 1 : -1; }

        // 基类 ItemName 钩子（≈ BotHelpers.convertItemIdToName）
        string NameOf(int itemId) => ItemName == null ? "NULL" : ItemName(itemId) ?? "NULL";

        // 源端在这些地方会 NPE 死掉；这里改成收摊回 Reset，避免整条状态机卡住。
        void AbandonTutorial()
        {
            ExpireOptions();
            state = State.FINISHED;
            ResetTutorialBotState();
        }

        bool RequirePlayer(out BotActor player)
        {
            player = Player;
            if (player != null) return true;
            AbandonTutorial();
            return false;
        }

        void ExpireOptions()
        {
            var player = Player;
            if (player != null) output.ExpireOptions(player);
        }

        // ——— WAIT_FOR_PLAYER_IN_RANGE ———
        void WaitForNewPlayer()
        {
            if (interactors.Respondant != null) return;
            var noob = BotLogic.WaitForPlayerInRange(chr, 500, 150);
            if (noob == null) return;
            // 降级说明：源端这里只排「已经帮过的人」，理论上会把教程给到同图的另一个假人。
            // 本工程城镇里假人密集，故额外排除假人（它们没有背包，走不到后面的流程），只接待真人玩家。
            if (BotHelpers.IsBot(noob)) return;
            if (noobsHelped.Contains(noob.id)) return;
            interactors.SetRespondant(noob);
        }

        void BotWaitingForPlayerFlavorText()
        {
            // 源端 random.nextInt(100) < 6
            if (random.Next(100) < 6) Say("Flavor", null, null, true);
        }

        // ——— START ———
        void GreetPlayer()
        {
            if (!RequirePlayer(out var player)) return;
            Face(player);
            Say("Greeting", Replacements("%RESP_NAME", player.name), () => Continue(TutorialState.Inquire));
        }

        // ——— INQUIRE ———
        void InquirePlayer()
        {
            if (!RequirePlayer(out var player)) return;
            Say("Inquiry", null, () =>
            {
                SetHint(YesNoHint());
                DisplayCommands(player);
                StartTimer(InquiryTimeoutMs);
                Continue(TutorialState.WaitForInquiryResponse);
            });
        }

        void WaitForResponse()
        {
            if (BotScheduler.Now() < endTime) { ProcessMessages(); return; }
            ExpireOptions();
            output.Speak(chr, "Talk to me again if you're ready.");
            state = State.FINISHED;
            ResetTutorialBotState();
        }

        // ——— ASK_ADMIN / GRANT_ADMIN ———
        void AskAdminRights()
        {
            if (!RequirePlayer(out var player)) return;
            Say("AskAdmin", null, () =>
            {
                SetHint(YesNoHint());
                DisplayCommands(player);
                StartTimer(AdminTimeoutMs);
                Continue(TutorialState.WaitAdminResponse);
            });
        }

        void GrantAdminRights()
        {
            if (!RequirePlayer(out var player)) return;

            if (BotTypeManager.GrantTutorialAdmin?.Invoke(player) != true)
                throw new InvalidOperationException("Tutorial admin progression is not connected");

            // 点券码是真实能力：≡ NXCodeManager.generateGiftCardCode + createCompleteNXCode(code, 25000)
            generatedNXCode = GenerateNxCode();
            generatedNXCode2 = GenerateNxCode();
            var replacements = Replacements(
                "%NX_CODE", generatedNXCode ?? "(兑换码生成失败)",
                "%NX_CODE_2", generatedNXCode2 ?? "(兑换码生成失败)");

            Say("GrantAdmin", replacements, () =>
            {
                DebugLog("[TutorialBot] Granted admin to " + player.name
                    + " | NX code 1: " + (generatedNXCode ?? "-")
                    + " | NX code 2: " + (generatedNXCode2 ?? "-"));
                Continue(TutorialState.GiftTrade);
            });
        }

        string GenerateNxCode()
        {
            try
            {
                var db = BotClient.GetBotClient()?.db;
                if (db == null) { DebugLog("[TutorialBot] NX code skipped: no DbStore"); return null; }
                if (nxCodes == null) nxCodes = new BotNXCodes(db, random);
                string code = nxCodes.Generate();
                return nxCodes.Create(code, NxCreditPerCode) > 0 ? code : null;
            }
            catch (Exception e)
            {
                DebugLog("[TutorialBot] NX code generation failed: " + e.Message);
                return null;
            }
        }

        // ——— GIFT_TRADE ———
        void InitiateGiftTrade()
        {
            if (!RequirePlayer(out var player)) return;
            Say("GiftTradeIntro", null, () =>
            {
                // 降级 2：源端 BotTradeCommands.sendTradeRequestToPlayer 需要 BotTradeService，
                //        状态机层拿不到；未注入就直接跳过交易环节进下一段教学。
                if (trades == null)
                {
                    DebugLog("[TutorialBot] GIFT_TRADE: BotTradeService not wired, skipping gift trade");
                    After(1500, () => Continue(TutorialState.Tutorial1));
                    return;
                }
                StartTimer(TradeTimeoutMs);
                After(2000, () =>
                {
                    // ≡ BotTradeCommands.sendTradeRequestToPlayer(getChr(), player)
                    if (!BotTradeCommands.SendRequest(chr, player, trades))
                        DebugLog("[TutorialBot] trade request to " + player.name + " failed");
                    scripted = false;
                    SetState(TutorialState.WaitTradeAccept);
                });
            });
        }

        bool WaitForTradeAccept()
        {
            // ≡ getChr().getTrade() == null：本端口 session 在「发出邀请」时就挂上了，
            //   只有双方都进交易窗口（FullTrade）才等价于源端「有 Trade 对象」（同 DropGameBot）。
            if (!(chr.trade is BotTradeService.Session session)) return false;
            if (session.FullTrade) return true;
            if (BotScheduler.Now() > endTime)
            {
                BotTradeCommands.Decline(chr);
                output.Speak(chr, "No worries, the gifts will be waiting for you next time.");
                Continue(TutorialState.Tutorial1);
            }
            return false;
        }

        void PlaceTradeItems()
        {
            if (!RequirePlayer(out var player)) return;
            int robeId = player.gender == 0 ? BlueSaunaRobe : RedSaunaRobe;

            // ≡ placeTradeItems 的一串 sleepAmountSeconds：主线程用等效间隔的续接链。
            scripted = true;
            BotTiming.CreateChain().StopUnless(Guard())
                .Pause(1000)
                .Run(() => BotTradeCommands.WriteChat(chr, "Here are some goodies to get you started!"))
                .Pause(1500)
                .Run(() => BotTradeCommands.SetMeso(chr, TradeMesos))
                .Pause(500)
                .Run(() => BotTradeCommands.AddItem(chr, PowerElixir, 1000, 1))
                .Pause(300)
                .Run(() => BotTradeCommands.AddItem(chr, OnyxApple, 100, 2))
                .Pause(300)
                .Run(() => BotTradeCommands.AddItem(chr, TownScrollHenesys, 100, 3))
                .Pause(300)
                .Run(() => BotTradeCommands.AddItem(chr, IlbiStars, 800, 4))
                .Pause(300)
                .Run(() => BotTradeCommands.AddCleanEquip(chr, MapleKandayo, 5))
                .Pause(300)
                .Run(() => BotTradeCommands.AddCleanEquip(chr, robeId, 6))
                .Pause(500)
                .Run(() => BotTradeCommands.WriteChat(chr, "1B mesos, potions, scrolls, stars, and gear. All yours!"))
                .Pause(1000)
                .Run(() => BotTradeCommands.Confirm(chr))
                .Run(() => Continue(TutorialState.Tutorial1))
                .Start();
        }

        // ——— TUTORIAL 1 / 2 ———
        void Tutorial1()
        {
            Say("Tutorial_1", Replacements("%CHR_NAME", chr.name), () => Continue(TutorialState.Tutorial2));
        }

        void Tutorial2()
        {
            Say("Tutorial_2", null, () =>
            {
                Tutorial2Drops();
                StartTimer(PickTimeoutMs);
                Continue(TutorialState.Tutorial2WaitPick);
            });
        }

        void Tutorial2Drops()
        {
            // ≡ botThrowEquipsInARow(getChr(), items, centerPos, 75)
            DropCommands.BotThrowEquipsInARow(chr, Hats, (int)chr.x, (int)chr.y, 75);
            DisplayTutorialDropOptions();
        }

        void DisplayTutorialDropOptions()
        {
            var options = new List<string>();
            foreach (int itemId in Hats) options.Add(NameOf(itemId));
            SetHint(options);
            DisplayCommands(Player);
        }

        // ——— TUTORIAL 3（武器：看 → 等扔 → 强化归还）———
        void AnalyzePlayersWeapon()
        {
            var wep = ReadEquippedWeapon();
            if (wep == null) { Continue(TutorialState.SendOff); return; }     // ≡ catch → return 0（其间不说任何话）
            wepId = wep.itemId;
            Say("AnalyzePlayersWeapon", Replacements("%WPN_NAME", NameOf(wep.itemId)),
                () => Continue(TutorialState.Tutorial3WaitDrop));
        }

        // ≡ readPlayerEquipBySlotName(respondant, BodyPart.WEAPON)
        InventoryEntry ReadEquippedWeapon()
        {
            var player = Player;
            if (player == null || player.IsBot()) return null;
            var inventory = player.character?.inventory;
            if (inventory == null) return null;
            foreach (var item in inventory)
                if (item != null && item.position == WeaponSlot && item.inventoryType == InventoryDao.Equipped) return item;
            return null;
        }

        void WaitForPlayerToDropWeapon()
        {
            if (!RequirePlayer(out var player)) return;
            // ≡ lootPlayersDroppedWeapon(wepId)：先按 itemId 找地面物品，取出实体，再逐件捡走（只移除）
            var onFloor = BotLogic.CheckForItemOnFloor(player.mapId, (int)player.x, (int)player.y, wepId);
            InventoryEntry found = null;
            foreach (var mapItem in onFloor)
                if (mapItem != null && mapItem.item != null && mapItem.ItemId == wepId) found = mapItem.item;
            if (onFloor.Count > 0) DropCommands.LootItemListOnFloor(chr, onFloor);
            playersWep = found;
        }

        void UpgradeAndReturnWeapon()
        {
            // ≡ sleepAmountSeconds(2000) → lootDialog → scroll → returnUpgradedWeapon
            After(2000, () => Say("LootDialogue", Replacements("%WPN_NAME", NameOf(wepId)), () => After(6000, ScrollAndReturn)));
        }

        void ScrollAndReturn()
        {
            bool upgraded = ScrollPlayersDroppedWeapon();
            if (upgraded)
            {
                // ≡ upgradeDialog(); returnUpgradedWeapon(scrolledWep)（中间还有 3000ms）
                After(3000, () => Say("UpgradeDialogue", null, ReturnUpgradedWeapon));
                return;
            }
            // 降级 3：没有 scrollEquipWithId 的等价能力时不伪造成功，武器原样归还。
            DebugLog("[TutorialBot] weapon upgrade skipped: MarketScrollPricing unavailable");
            output.Speak(chr, "Looks like my tools aren't cooperating today. Here's your weapon back, untouched.");
            ReturnUpgradedWeapon();
        }

        bool ScrollPlayersDroppedWeapon()
        {
            if (playersWep == null) return false;
            var rules = ResolvePricing();
            if (rules == null) return false;
            try
            {
                // ≡ ItemInformationProvider.scrollEquipWithId(wep, 2043003, false, 0, false)
                var scrolled = playersWep.Copy();
                if (scrolled.upgradeslots > 0)
                {
                    rules.ImproveStats(scrolled, WeaponScroll);
                    scrolled.upgradeslots = unchecked((sbyte)(scrolled.upgradeslots - 1));
                    if (scrolled.equipmentData == null) scrolled.equipmentData = new Dictionary<string, object>();
                    scrolled.equipmentData["level"] = unchecked((sbyte)(BotItemCatalog.Int(scrolled.equipmentData, "level") + 1));
                }
                scrolled.owner = Player?.name ?? scrolled.owner;
                playersWep = scrolled;
                // ≡ botScrollSuccess(getChr()) + BotEmote(getChr(), 2)
                output.Send(BotOutput.Kind.Scroll, chr, value: 1);
                output.Emote(chr, 2);
                return true;
            }
            catch (Exception e)
            {
                DebugLog("[TutorialBot] weapon scroll failed: " + e.Message);
                return false;
            }
        }

        MarketScrollPricing ResolvePricing()
        {
            if (pricing != null || pricingTried) return pricing;
            pricingTried = true;
            try { pricing = new MarketScrollPricing(new MarketItemPool(BotResources.Load()), BotItemCatalog.Shared, random); }
            catch (Exception e) { DebugLog("[TutorialBot] MarketScrollPricing unavailable: " + e.Message); pricing = null; }
            return pricing;
        }

        void ReturnUpgradedWeapon()
        {
            if (!RequirePlayer(out var player)) return;
            if (playersWep != null)
                DropCommands.BotThrowEquip(chr, playersWep, (int)player.x, (int)player.y);
            Say("ReturnDialogue", null, () => Continue(TutorialState.SendOff));
        }

        // ——— SEND OFF ———
        void SendOff()
        {
            Say("SendoffDialogue", null, EscortPlayerToPortal);
        }

        void EscortPlayerToPortal()
        {
            // ≡ pathFinderBeta(getChr(), new Point(927, 485)) + sleepAmountSeconds(2000)
            scripted = true;
            var keepGoing = Guard();
            BotMovementService.Move(chr, PortalX, PortalY, () =>
            { if (keepGoing()) After(2000, SendOffPartingGift); });
        }

        void SendOffPartingGift()
        {
            if (!RequirePlayer(out var player)) return;
            Say("SendoffPartingGiftDialogue", null, () =>
            {
                DropCommands.BotThrowItem(chr, OnyxApple, (int)player.x, (int)player.y, out _);
                if (!noobsHelped.Contains(player.id)) noobsHelped.Add(player.id);
                After(1000, () => Continue(TutorialState.Return));
            });
        }

        // ——— RETURN ———
        void ReturnOrigin()
        {
            ExpireOptions();
            scripted = true;
            var keepGoing = Guard();
            var frames = LoadMovementRecording(chr.mapId);
            Action finish = () =>
            {
                if (!keepGoing()) return;
                After(1000, () =>
                {
                    interactors.ResetRespondants();
                    Continue(TutorialState.Reset);
                });
            };
            if (frames != null && frames.Count > 0)
                BotMovementPlayback.Play(chr, frames, finished: stopped => { if (!stopped) finish(); });
            else
            {
                DebugLog("[TutorialBot] return recording missing for map " + chr.mapId);
                finish();
            }
        }

        static List<BotMovementRecording.Packet> LoadMovementRecording(int mapId)
        {
            try
            {
                var bytes = BotResources.Load().ReadBytes(BotMovementRecording.ResourcePath(mapId, ReturnRecording, "bin"));
                return bytes == null ? null : BotMovementRecording.ReadBinary(bytes);
            }
            catch (Exception)
            {
                return null;
            }
        }

        // =========================================================================
        // MESSAGES
        // =========================================================================
        // 源端 processMessages()：从共享队列取一条，超时 1 秒就返回。本端口要用的是
        // BotRuntime.ReceiveChat 灌进来的队列，SECONDARY 保留源端语义优先消费。
        void ProcessMessages()
        {
            Consume(QueueType.SECONDARY);
            Consume(QueueType.PRIMARY);
        }

        // Blackjack 同款：不是我的人就把消息原样放回队尾，别抢走别的 bot 的聊天。
        void Consume(QueueType type)
        {
            var message = messages.Poll(type);
            if (message == null) return;
            if (!interactors.IsMessageFromRespondant(message)) { messages.Add(message, type); return; }
            HandleMessage(message);
        }

        void HandleMessage(ChatMessage message)
        {
            string content = message.content == null ? "" : message.content.ToLowerInvariant();
            if (tutorialBotState == TutorialState.WaitForInquiryResponse) HandleInquiryResponse(content);
            else if (tutorialBotState == TutorialState.WaitAdminResponse) HandleAdminResponse(content);
            else if (tutorialBotState == TutorialState.Tutorial2WaitPick) HandleTutPickResponse(content);
        }

        void HandleInquiryResponse(string content)
        {
            if (content.Contains("yes")) runTutorial = true;
            else if (content.Contains("no")) runTutorial = false;
        }

        void HandleAdminResponse(string content)
        {
            if (content.Contains("yes")) wantsAdmin = true;
            else if (content.Contains("no")) wantsAdmin = false;
        }

        void HandleTutPickResponse(string content)
        {
            foreach (int itemId in Hats)
            {
                string itemName = NameOf(itemId);
                if (itemName == "NULL" || !content.Contains(itemName.ToLowerInvariant())) continue;
                HandleSelectedHat(itemName, itemId);
                return;
            }
            output.Speak(chr, "Did you type the name in correctly?");
            output.Emote(chr, 6);
        }

        void HandleSelectedHat(string itemName, int itemIdSelected)
        {
            output.Speak(chr, "You've selected " + itemName + "! Enjoy it!");
            tutPicked = true;
            // 源端 sleepAmountSeconds(2500) 后才收走剩下的帽子
            Schedule(2500, () => LootLeftoverHats(itemIdSelected));
        }

        void LootLeftoverHats(int itemIdSelected)
        {
            var leftovers = new List<int>();
            foreach (int itemId in Hats) if (itemId != itemIdSelected) leftovers.Add(itemId);
            DropCommands.BotLootSelectedItems(chr, leftovers.ToArray());
        }

        // =========================================================================
        // MISC
        // =========================================================================
        static string[] YesNoHint() => new[] { "Yes", "No" };
        void SetHint(IList<string> options) => hint = new List<string>(options);

        static Dictionary<string, string> Replacements(string key, string value)
            => new Dictionary<string, string> { [key] = value };

        static Dictionary<string, string> Replacements(string key1, string value1, string key2, string value2)
            => new Dictionary<string, string> { [key1] = value1, [key2] = value2 };

        public override void StopScheduledTask()
        {
            generation++;
            sequenceTask?.Cancel();
            sequenceTask = null;
            scripted = false;
            ExpireOptions();
            BotMovementPlayback.Cancel(chr);
            BotMovementService.Stop(chr);
            chr.trade?.Decline();
            base.StopScheduledTask();
        }
    }
}
