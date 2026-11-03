// 机器人玩法任务 900004「机器人改装」（2026-11-03，docs/tms282-skill/11-android-migration.md §4.3）
//
// 收集 200 块 4000357 钢铁碎片 → 高級机器人(男/女 任选) + 青銅之心 1672001。
// 前置：900003 已完成（由 QuestDef.startReq.quests 判定，脚本不再重复检查）。
// 扣材料与发奖励都写在脚本里，原因同 900003.js。
var status = -1;
var 选择 = -1;

var 零件 = 4000357;
var 需要 = 200;
var 男款 = 1662002;
var 女款 = 1662003;
var 心脏 = 1672001;

function start(mode, type, selection) {
    if (mode != 1) { qm.dispose(); return; }
    status++;
    if (status == 0) {
        qm.sendAcceptDecline("那台补给型还跑得动？想换台高级的，就得给我更多料。\r\n"
            + 需要 + " 块#t" + 零件 + "#，我给你调一台#b高級机器人#k，"
            + "顺带一颗能用的青銅之心。");
    } else {
        qm.forceStartQuest();
        qm.sendOk("机械兵、未来坦克、机械蜘蛛——哪台拆下来的都行，我这儿不挑料。");
        qm.dispose();
    }
}

function end(mode, type, selection) {
    if (mode != 1) { qm.dispose(); return; }
    status++;
    if (status == 0) {
        if (!qm.haveItem(零件, 需要)) {
            qm.sendOk("#t" + 零件 + "#还差一些，要 " + 需要 + " 块。");
            qm.dispose();
            return;
        }
        qm.sendSimple("手艺活做完了。这次男款女款都备好了，你挑一台——\r\n\r\n"
            + "#L0#男款#l\r\n#L1#女款#l");
        return;
    }
    if (status == 1) {
        if (selection != 0 && selection != 1) { qm.dispose(); return; }
        选择 = selection;
        qm.sendYesNo("确定要这台#b#t" + (选择 == 0 ? 男款 : 女款) + "##k吗？\r\n"
            + "#d（心脏等级不可超过机器人等级 +3，青銅之心配高级型刚好）");
        return;
    }
    qm.gainItem(零件, -需要);
    qm.forceCompleteQuest();
    qm.gainItem(选择 == 0 ? 男款 : 女款, 1);
    qm.gainItem(心脏, 1);
    qm.gainExp(8000);
    qm.sendOk("换上试试。旧的那台别丢，说不定哪天还能拆出零件。");
    qm.dispose();
}
