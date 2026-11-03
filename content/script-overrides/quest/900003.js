// 机器人玩法任务 900003「机器人初启动」（2026-11-03，docs/tms282-skill/11-android-migration.md §4.3）
//
// 收集 100 块 4000357 钢铁碎片 → 补给型机器人(男/女 任选) + 銀之心 1672000。
//
// 扣材料必须写在脚本里：交付走 qm.forceCompleteQuest() → QuestLogic.Complete(..., false)，
// 只改任务状态、不跑 Act/1，也不会动 Check/1 的收集条件。
// 奖励同样在脚本里发（数据层 QuestActions.items 是固定列表，表达不了「男/女任选」）。
var status = -1;
var 选择 = -1;

var 零件 = 4000357;
var 需要 = 100;
var 男款 = 1662000;
var 女款 = 1662001;
var 心脏 = 1672000;

function start(mode, type, selection) {
    if (mode != 1) { qm.dispose(); return; }
    status++;
    if (status == 0) {
        qm.sendAcceptDecline("你就是那个到处收集零件的冒险家吧？\r\n"
            + "我在给一台老式机器人做复原，可手上全是活。帮我从那些报废的机械兵身上"
            + "拆 " + 需要 + " 块#t" + 零件 + "#来，我让它跟你上路。");
    } else {
        qm.forceStartQuest();
        qm.sendOk("玩具工厂里的机械兵身上最多。\r\n"
            + "别急着一次凑齐，回来的时候我这儿还开着炉子。");
        qm.dispose();
    }
}

function end(mode, type, selection) {
    if (mode != 1) { qm.dispose(); return; }
    status++;
    if (status == 0) {
        if (!qm.haveItem(零件, 需要)) {
            qm.sendOk("#t" + 零件 + "#还不够 " + 需要 + " 块，凑齐了再来。");
            qm.dispose();
            return;
        }
        qm.sendSimple("够了够了，这些够装一台。挑个顺手的体型吧——\r\n\r\n"
            + "#L0#男款#l\r\n#L1#女款#l");
        return;
    }
    if (status == 1) {
        if (selection != 0 && selection != 1) { qm.dispose(); return; }
        选择 = selection;
        qm.sendYesNo("确定要这台#b#t" + (选择 == 0 ? 男款 : 女款) + "##k吗？\r\n"
            + "#d（机器人和心脏是两件装备，装完机器人才能装心脏）");
        return;
    }
    // status == 2：确认
    qm.gainItem(零件, -需要);
    qm.forceCompleteQuest();
    qm.gainItem(选择 == 0 ? 男款 : 女款, 1);
    qm.gainItem(心脏, 1);
    qm.gainExp(1200);
    qm.sendOk("装好了，它叫……算了，名字你自己起。\r\n"
        + "把#b机器人#k和#b心脏#k都装到装备栏上，它就跟着你走了。");
    qm.dispose();
}
