// 用户截图重建；用户指定完成奖励50经验。
var status = -1;
function start(mode, type, selection) {
    if (mode < 0 || (mode == 0 && (status == 0 || status == 2))) { qm.dispose(); return; }
    status += mode == 1 ? 1 : -1;
    if (status == 0) qm.sendNext("嗨，您是新来的冒险家吗？正如你所见，冒险岛世界存在一个着常年被冰雪覆盖的村子 #b冰封雪域#k。关于这里有个小故事，你感兴趣吗？");
    else if (status == 1) qm.sendNextPrev("据说这里的某深处存在着一个恐怖的存在，它的诅咒导致这里常年下雪。曾经有一个小队前去讨伐这传说中的怪物。这个队伍集结了冒险岛世界最强大的4个职业，在到达这个怪物的藏身之处的洞穴迷宫前中了陷阱，战士队长为了队友，永远留在了那里。他们唯一的牧师更是最后感染了那个怪物的火毒而走火入魔。纵使这般，虽然他们获胜了，但也是惨胜。其中的弓箭手和飞侠是一对夫妻。飞侠怀有身孕，殊不知这怪物竟然在死前将自身最后的邪念注入到了这个飞侠体内，想借此重生。他们回到村里后，村长阿尔卡特听闻此事，竟欲处死弓箭手的妻子，他施展魔法禁锢了她，村民将她绑上祭台。最后这二人也终于死了，讨伐怪物的远征队，无一活口。。。据说这个怪物还是重生了，最后见过它的人，只隐约看到它有8只手。。。");
    else if (status == 2) qm.sendAcceptDecline("怎么样，故事有趣吗？不过这对你来说还是有难度的，怎么样，想热热身吗？");
    else { qm.forceStartQuest(); qm.sendOk("好的，你去狩猎5只#b小白雪鬼#k！"); qm.dispose(); }
}
function end(mode, type, selection) {
    if (mode != 1) { qm.dispose(); return; }
    if (qm.getQuestProgress(900002, "9409000") < 5) {
        qm.sendOk("请先狩猎5只小白雪鬼，完成后再来找我。"); qm.dispose(); return;
    }
    qm.forceCompleteQuest();
    qm.gainExp(50);
    qm.sendOk("你已经完成了打猎的请求。");
    qm.dispose();
}
