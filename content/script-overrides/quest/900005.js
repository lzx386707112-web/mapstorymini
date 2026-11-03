// 机器人玩法任务 900005「神选之人」（2026-11-03，docs/tms282-skill/11-android-migration.md §4.3）
//
// 282 里阿莎机器人 + 鋰之心是任务 38743 的奖励；本项目没有那条任务链，
// 改成「完成改装任务、带着机器人来找金博士」的高阶确认任务。
// 前置：900004 已完成（QuestDef.startReq.quests 判定）。
// 无收集需求，只发奖励：阿莎机器人 1662208 + 鋰之心 1672086。
var status = -1;

var 阿莎 = 1662208;
var 心脏 = 1672086;

function start(mode, type, selection) {
    if (mode != 1) { qm.dispose(); return; }
    status++;
    if (status == 0) {
        qm.sendAcceptDecline("等一下——你身后那台机器人的编号，和二十年前失踪的「阿莎」对得上。\r\n"
            + "那不是量产型号。你从哪儿弄来的？");
    } else {
        qm.forceStartQuest();
        qm.sendOk("把它带来给我看看。我给它配一颗#b鋰之心#k——那才是它原本该有的核心。");
        qm.dispose();
    }
}

function end(mode, type, selection) {
    if (mode != 1) { qm.dispose(); return; }
    status++;
    if (status == 0) {
        qm.sendYesNo("机体我看过了，骨架没动过。\r\n"
            + "把#b#t" + 阿莎 + "##k和#b#t" + 心脏 + "##k拿去——它认你，我拦不住。");
        return;
    }
    qm.forceCompleteQuest();
    qm.gainItem(阿莎, 1);
    qm.gainItem(心脏, 1);
    qm.gainExp(50000);
    qm.sendOk("……成了。它醒了。\r\n阿莎就交给你了，别让它再空转二十年。");
    qm.dispose();
}
