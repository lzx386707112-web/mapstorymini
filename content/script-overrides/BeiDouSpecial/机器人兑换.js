/**
 * @description 機器人兑换 · 黄金枫叶货架
 *
 * 货币 = 黄金枫叶 4000313（主线里程碑每档 10 枚 + 金币兑换 50 万/枚）。
 * 定价见 docs/tms282-skill/11-android-migration.md §4.2：
 *   銀之心 50 / 青銅之心 100 / 補給型機器人(男/女) 150
 * 朱礦心以上的心臟、高級機器人、Boss 纪念款都走起源之塔积分商店，
 * 这里只放"零门槛入门套装"，不重复上架、也不做活动款轮换。
 *
 * 入口：主菜单 9900001「角色提升 → 机器人兑换」
 *       （cm.openNpc(9900001, "机器人兑换") → 本文件）。
 *
 * 机器人本体是装备（1xxxxxx 段），cm.gainItem 一次只能给 1 件，
 * 所以装备类商品的购买个数上限恒为 1；心臟同属装备段，同样限 1。
 * 全部商品都是装备，这里的上限逻辑只留一处 装备类() 判定。
 */
var 货币 = 4000313;

// [道具ID, 价格(黄金枫叶)]
var 货架 = [
    [1672000, 50],    // 銀之心        grade0 lv30（先有机器人才能装）
    [1672001, 100],   // 青銅之心      grade1 lv30
    [1662000, 150],   // 補給型機器人(男) grade3 lv30
    [1662001, 150]    // 補給型機器人(女) grade3 lv30
];

var status = -1;
var 商品 = null;

function start() {
    status = -1;
    action(1, 0, 0);
}

function action(mode, type, selection) {
    if (mode === -1 || mode === 0) {
        cm.dispose();
        return;
    }
    status++;
    if (status === 0) {
        cm.sendSimple(菜单());
    } else if (status === 1) {
        选商品(selection);
    } else if (status === 2) {
        确认(selection);
    } else if (status === 3) {
        结算();
    } else {
        cm.dispose();
    }
}

// 半角宽度：中文按 2 格、ASCII 按 1 格。对话框没有制表位，用空格补右对齐。
function 宽度(文本) {
    var 宽 = 0;
    for (var i = 0; i < 文本.length; i++) {
        宽 += (文本.charCodeAt(i) > 0x7F) ? 2 : 1;
    }
    return 宽;
}

function 补左(文本, 目标宽) {
    var 缺 = 目标宽 - 宽度(文本);
    return 缺 > 0 ? new Array(缺 + 1).join(" ") + 文本 : 文本;
}

function 持有() {
    return cm.getItemQuantity(货币);
}

function 菜单() {
    var 文本 = "#e#d【機器人兑换 · 黄金枫叶】#k#n\r\n";
    文本 += "持有 #i" + 货币 + "# #b#t" + 货币 + "##k ×#r" + 持有() + "#k\r\n";
    文本 += "#d----------------------------------------#k\r\n";
    for (var i = 0; i < 货架.length; i++) {
        var 价 = 货架[i][1];
        文本 += "#v" + 货架[i][0] + "# #L" + i + "#"
            + 补左(价 + " 叶", 8) + "  #t" + 货架[i][0] + "##l\r\n";
    }
    文本 += "\r\n#d機器人本體要装上之后才能装心臟；心臟等级不可超过机器人等级 +3。#k";
    文本 += "\r\n#d黄金枫叶不够就去打主线/日常，或用金币找兑换员买。#k";
    return 文本;
}

function 选商品(selection) {
    var idx = selection;
    if (idx < 0 || idx >= 货架.length) {
        cm.sendOk("商品不存在。");
        cm.dispose();
        return;
    }
    商品 = { id: 货架[idx][0], price: 货架[idx][1], maxQty: 1 };
    var 文本 = "你想要兑换：\r\n";
    文本 += "#v" + 商品.id + "# #t" + 商品.id + "#\r\n";
    文本 += "#d----------------------------------------#k\r\n";
    文本 += "单价：#r" + 商品.price + "#k 黄金枫叶\r\n";
    文本 += "当前持有：#b" + 持有() + "#k，最多可换 #b" + 可换数量() + "#k 个\r\n\r\n";
    文本 += "请输入兑换个数（上限 " + 商品.maxQty + "）";
    cm.sendGetNumber(文本, 1, 1, 商品.maxQty);
}

function 可换数量() {
    var 个 = Math.floor(持有() / 商品.price);
    return Math.max(0, Math.min(个, 商品.maxQty));
}

function 确认(selection) {
    var 个 = selection > 0 ? selection : 1;
    if (个 > 商品.maxQty) {
        个 = 商品.maxQty;
    }
    商品.qty = 个;
    var 花费 = 商品.price * 个;
    var 文本 = "确定花费 #r" + 花费 + "#k 个 #i" + 货币 + "# #b#t" + 货币 + "##k 兑换 "
        + 个 + " 个 #b#t" + 商品.id + "##k 吗？\r\n";
    if (!cm.canHold(商品.id, 个)) {
        文本 += "\r\n#r#e注意：背包空间不足，先清出一格再来。#k#n\r\n";
    }
    cm.sendYesNo(文本);
}

function 结算() {
    var 花费 = 商品.price * 商品.qty;
    if (!cm.haveItem(货币, 花费)) {
        cm.sendOk("黄金枫叶不足，还差 #r" + (花费 - 持有()) + "#k 个。");
        cm.dispose();
        return;
    }
    if (!cm.canHold(商品.id, 商品.qty)) {
        cm.sendOk("背包空间不足，兑换取消。");
        cm.dispose();
        return;
    }
    cm.gainItem(货币, -花费);
    cm.gainItem(商品.id, 商品.qty);
    var 文本 = "兑换成功！\r\n";
    文本 += "获得 #i" + 商品.id + "# #b#t" + 商品.id + "##k ×" + 商品.qty + "。\r\n";
    文本 += "剩余黄金枫叶：#r" + 持有() + "#k 个。";
    cm.sendOk(文本);
    cm.dispose();
}
