/**
 * @description 称号试炼（项目自创玩法，设计稿 docs/title-trial-design-2026-11-04.md）
 *
 * 入口：拍卖行中心 9900001 → 角色成长 → 称号试炼（selection 44）。
 * 点亮规则：战力达标（与 .titles 阈值一致）+ 系列试炼计数达标，第 1 档免计数。
 * 数据全部来自 cm.getTitleTrial / cm.getTitleTrialNeed / cm.claimTitleTier，
 * 校验在服务端（NativeTitleTrials.Claim），本脚本只做展示，不自己判定成败。
 * 每点亮 1/5/8/10 档发一枚里程碑名牌戒指（服务端发放，位掩码防重）。
 */
var status = -1;
var series = 0;

var 系列名 = ["玄幻", "斗罗", "龙珠", "火影", "海贼", "死神", "鬼灭", "咒术", "英雄学院"];
var 试炼名 = ["破境 · 累计击杀", "猎魂 · 成长BOSS击杀", "集珠 · 图鉴收集", "忍务 · 完成任务",
    "远征 · 副本积分", "锻刀 · 星力升星", "讨伐 · 每日任务轮数", "聚灵 · 钓鱼等级", "认证 · 累计签到"];

// 战力阈值（与 NativeNameplate / patch_nameplate_series.py 三方一致），第 10 档 100 亿。
var 战力阈值 = [10000, 100000, 1000000, 10000000, 50000000, 200000000, 500000000, 2000000000];
var 战力阈值10 = 10000000000;

function start() {
    status = -1;
    action(1, 0, 0);
}

function action(mode, type, selection) {
    if (mode === 1) {
        status++;
    } else if (mode === -1) {
        status--;
    } else {
        cm.dispose();
        return;
    }
    if (status === 0) {
        cm.sendSimple(系列列表());
    } else if (status === 1) {
        if (selection < 0 || selection >= 系列名.length) {
            cm.dispose();
            return;
        }
        series = selection;
        cm.sendSimple(档位列表(series));
    } else if (status === 2) {
        if (selection < 1 || selection > 10) {
            cm.dispose();
            return;
        }
        var 结果 = cm.claimTitleTier(series);
        if (结果.ok) {
            cm.sendOk("#g" + 结果.message + "#k");
        } else {
            cm.sendOk("#r" + 结果.message + "#k");
        }
    } else {
        cm.dispose();
    }
}

function 系列列表() {
    var 文本 = "\r\n\t\t\t#e#b【称号试炼】#k#n\r\n\r\n";
    文本 += "#d点亮 = 战力达标 + 试炼计数达标；每点亮 1/5/8/10 档发名牌戒指里程碑。#k\r\n\r\n";
    for (var i = 0; i < 系列名.length; i++) {
        var t = cm.getTitleTrial(i);
        var 状态;
        if (t.lit >= 10) {
            状态 = "#g已满档#k";
        } else if (t.canClaim) {
            状态 = "#b可点亮第 " + (t.lit + 1) + " 档！#k";
        } else {
            状态 = "已点亮 #r" + t.lit + "#k 档";
        }
        文本 += "#L" + i + "##b【" + 系列名[i] + "】#k" + 试炼名[i] + " － " + 状态 + "#l\r\n";
    }
    return 文本;
}

function 档位列表(s) {
    var 头 = cm.getTitleTrial(s);
    var 战力 = 取战力(cm.getPlayer());
    var 文本 = "\r\n#e#b【" + 系列名[s] + "】#k#n " + 试炼名[s] + "\r\n";
    文本 += "#d当前战力：#r" + 格式化数值(战力) + "#d　已点亮：#r" + 头.lit + "#d 档#k\r\n\r\n";
    for (var t = 1; t <= 10; t++) {
        var need = cm.getTitleTrialNeed(s, t);
        var 阈值 = t >= 10 ? 战力阈值10 : 战力阈值[t - 1];
        var 计数 = t <= 头.lit ? need : 头.cnt;
        var 行 = "第 " + t + " 档｜战力 ≥ " + 格式化数值(阈值) + "｜计数 " + 格式化数值(计数) + " / " + 格式化数值(need);
        if (t <= 头.lit) {
            文本 += "#g【已点亮】#k" + 行 + "　#d" + 称号名(s, t) + "#k\r\n";
        } else if (t === 头.lit + 1) {
            文本 += "#L" + t + "##b【可点亮】#k" + 行 + "#l\r\n";
        } else {
            文本 += "#L" + t + "##d【未解锁】#k" + 行 + "#l\r\n";
        }
    }
    return 文本;
}

// 第 t 档（1..10）的称号文本（与 9900001 称号表一致，仅展示用）
function 称号名(s, t) {
    return 称号表[s][t - 1];
}

// 角色战力：与 PacketCreator.calculateNameplatePower 一致
function 取战力(p) {
    try {
        var 属性 = p.getTotalStr() + p.getTotalDex() + p.getTotalInt() + p.getTotalLuk();
        var 攻击 = Math.max(1, Math.max(p.getTotalWatk(), p.getTotalMagic()));
        var 血蓝 = Math.floor(p.getMaxHp() / 10) + Math.floor(p.getMaxMp() / 20);
        return Math.max(0, 属性 * 攻击 + 血蓝);
    } catch (e) {
        return 0;
    }
}

// 数值：过万显示"万"，过亿显示"亿"
function 格式化数值(值) {
    var 数 = Number(值);
    if (!isFinite(数)) return "0";
    数 = Math.max(0, 数);
    if (数 >= 100000000) return 去尾零((数 / 100000000).toFixed(2)) + "亿";
    if (数 >= 10000) return 去尾零((数 / 10000).toFixed(1)) + "万";
    return String(数);
}

function 去尾零(文本) {
    if (文本.indexOf(".") < 0) return 文本;
    return 文本.replace(/0+$/, "").replace(/\.$/, "");
}

// 9×10 称号表（与 9900001.js / NativeNameplate.cs / patch_nameplate_series.py 三方一致）
var 称号表 = [
    ["乾元玄阶", "坤极天域", "天元墟境", "玄枢天宿", "星墟玄阙", "太白星河", "苍元帝宿", "昊天玄宿", "九曜神王", "无敌大帝"],
    ["魂师", "大魂师", "魂尊", "魂王", "魂圣", "封号斗罗", "极限斗罗", "三级神祇", "神帝", "神帝"],
    ["赛亚人", "超级赛亚", "赛亚二阶", "赛亚三阶", "超赛神境", "超赛蓝境", "蓝级进化", "破坏神位", "自在极意", "自在极意"],
    ["学院生徒", "下忍", "中忍", "上忍", "影级强者", "仙人模式", "尾兽化身", "六道之力", "大筒木境", "大筒木境"],
    ["海贼新秀", "超新星", "七武海", "中将", "四皇候补", "四皇", "大将", "觉醒者", "海贼王", "海贼王"],
    ["席官", "副队长", "队长", "破面十刃", "归刃解放", "完现术者", "零番队级", "灵王断片", "灵王", "灵王"],
    ["鬼杀队员", "十人众级", "柱级", "下弦之鬼", "上弦之鬼", "上弦首座", "鬼舞辻无", "赫刀觉醒", "缘壹之境", "缘壹之境"],
    ["四级术师", "三级术师", "二级术师", "一级术师", "准特级位", "特别一级", "特级术师", "领域展开", "最强之境", "最强之境"],
    ["无个性者", "普通学生", "雄英生徒", "实习英雄", "职业英雄", "前十强者", "前五英雄", "第一英雄", "超觉醒者", "超觉醒者"]
];
