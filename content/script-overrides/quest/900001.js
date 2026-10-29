// 用户截图重建；用户指定完成奖励50经验。
var status = -1;
function start(mode, type, selection) {
    if (mode != 1) { qm.dispose(); return; }
    status++;
    if (status == 0) qm.sendAcceptDecline("喂你，新来的冒险家，你要不要了解一下这里？");
    else { qm.forceStartQuest(); qm.sendOk("明智的选择，这个地方是一个叫玩具城的地方，这里有很多乐高积木组成的村民和怪物。当然，也有很多怪物，你瞧，右边的就是玩具城最常见的一种#b打鼓兔子#k。你帮我狩猎5只，我会给你一些在冒险路上的报酬…"); qm.dispose(); }
}
function end(mode, type, selection) {
    if (mode != 1) { qm.dispose(); return; }
    if (qm.getQuestProgress(900001, "9409001") < 5) {
        qm.sendOk("请先狩猎5只打鼓兔子，完成后再来找我。"); qm.dispose(); return;
    }
    qm.forceCompleteQuest();
    qm.gainExp(50);
    qm.sendOk("你已经完成了打猎的请求。");
    qm.dispose();
}
