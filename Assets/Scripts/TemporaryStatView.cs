using System.Collections.Generic;
using UnityEngine;

// BeiDou.exe CTemporaryStatView: 0x7B2BB0, 0x7B2E58, 0x7B44F4.
// Use IMG pixels directly; keep the native single row ending 3px from the right.
public static class TemporaryStatView
{
    // 护盾不是 buff（不进 NativeBuffs，没有 stat、没有图标槽位的语义），但玩家必须
    // 看得见还剩多少 —— 否则「按下去什么也没发生」和「按下去有盾但不知道有没有」
    // 在体感上是一样的。这里沿用箭筒那一套：借同一排图标位，底边压一条存量条。
    static readonly List<NativeShields.Entry> shields = new List<NativeShields.Entry>();

    public static Rect Slot(int index, int count, float width)
        => new Rect(width-3-count*32+index*32,23,32,32);
    public static void Draw()
    {
        var player=GameLoop.I != null ? GameLoop.I.player : null;
        var buffs=player != null ? player.GetComponent<NativeBuffs>() : null;
        if (buffs==null) return;
        var effects=buffs.Visible;
        NativeShields.Snapshot(player,shields);
        // 魔幻箭筒的箭镞是常驻状态（无时限），不进 NativeBuffs，单独占最左一格：
        // 图标取当前箭镞对应的技能（3140010 魔法箭 / 3140011 吸血箭）。
        bool arrow=NativeArrowMode.Active;
        int total=effects.Count+(arrow ? 1 : 0)+shields.Count;
        NativeBuffs.Effect hover=null;
        bool arrowHover=false;
        int hoverShield=-1;
        for (int i=0;i<effects.Count;i++)
        {
            var effect=effects[i];
            var rect=Slot(i,total,UiKit.W);
            var skill=effect.Id>0 ? SkillBook.Get(effect.Id) : null;
            if (skill==null && effect.Id>0) continue;
            var previous=GUI.color;
            int remaining=effect.RemainingMs(NativeBuffs.Now);
            // Icon/shadow layer color D2FFFFFF; InsertCanvas alpha0=210,
            // alpha1=64, delay=500. GA_REPEAT starts on the final 3000ms crossing.
            float canvasAlpha=remaining<=3000 ? Mathf.Lerp(210,64,Mathf.Repeat((3000-remaining)/500f,1f)) : 210;
            GUI.color=new Color(previous.r,previous.g,previous.b,previous.a*(210f/255f)*(canvasAlpha/255f));
            var icon=effect.Id<0 ? NativeItemSource.Icon(-effect.Id) : Art.Tex(skill.icon);
            if (icon!=null) DrawCentered(rect,icon);
            var shadow=effect.NoShadow ? null : Art.Tex("wz://UI/UIWindow.img/Skill/CoolTime/"+effect.Shadow(NativeBuffs.Now));
            if (shadow!=null) DrawCentered(rect,shadow);
            GUI.color=previous;
            if (rect.Contains(Pointer.Pos)) hover=effect;
        }
        if (arrow)
        {
            var rect=Slot(effects.Count,total,UiKit.W);
            var data=SkillBook.Get(NativeArrowMode.ActiveFx);
            var previous=GUI.color;
            GUI.color=new Color(previous.r,previous.g,previous.b,previous.a*(210f/255f));
            var icon=data!=null ? Art.Tex(data.icon) : null;
            if (icon!=null) DrawCentered(rect,icon);
            GUI.color=previous;
            DrawQuiverCharge(rect);
            if (rect.Contains(Pointer.Pos)) arrowHover=true;
        }
        // 护盾排在箭筒左侧：两者都是「常驻/时效的额外状态」，优先级低于 buff 图标。
        int shieldStart=effects.Count+(arrow ? 1 : 0);
        for (int i=0;i<shields.Count;i++)
        {
            var shield=shields[i];
            var rect=Slot(shieldStart+i,total,UiKit.W);
            var data=SkillBook.Get(shield.Id);
            var previous=GUI.color;
            GUI.color=new Color(previous.r,previous.g,previous.b,previous.a*(210f/255f));
            var icon=data!=null ? Art.Tex(data.icon) : null;
            if (icon!=null) DrawCentered(rect,icon);
            GUI.color=previous;
            DrawShieldBar(rect,shield);
            if (rect.Contains(Pointer.Pos)) hoverShield=i;
        }
        if (arrowHover)
        {
            var toggle=SkillBook.Get(NativeArrowMode.Toggle);
            if (toggle!=null) SkillWindow.DrawSkillData(toggle,Pointer.Pos,
                Mathf.Max(1,SkillProgress.LevelOf(NativeArrowMode.Toggle)));
            return;
        }
        if (hoverShield>=0)
        {
            // 悬停直接复用技能说明框：h 原文里已经写了「生成相当于最大HP的#x%的护盾，
            // 持续#time秒」，剩下的实时量由底边那条存量条表达，不再自造第二套文案。
            var data=SkillBook.Get(shields[hoverShield].Id);
            if (data!=null) SkillWindow.DrawSkillData(data,Pointer.Pos,shields[hoverShield].Level);
            return;
        }
        if (hover==null) return;
        if (hover.Id<0) ItemTooltip.DrawForStatus(-hover.Id);
        else SkillWindow.DrawSkillData(SkillBook.Get(hover.Id),Pointer.Pos,hover.Level);
        if (Event.current.type==EventType.Repaint && Pointer.RightPressed)
        {
            buffs.CancelByUser(hover.Id);
            Pointer.RightPressed=false;
            Pointer.ConsumeClick();
        }
    }
    // 护盾存量条：底边 2px，比例 = 当前存量 / 分母（pool 初始量、stack 上限、elements 初始元素数）。
    // 颜色沿用游戏里已有的语义色（金=可累积、青=计数型、蓝=定量），不再新造调色板。
    static void DrawShieldBar(Rect slot,NativeShields.Entry shield)
    {
        if (shield.Max<=0f) return;
        float fill=Mathf.Clamp01(shield.Value/shield.Max);
        if (fill<=0f) return;
        var previous=GUI.color;
        var bar=new Rect(slot.x+3f,slot.yMax-4f,(slot.width-6f)*fill,2f);
        GUI.color=shield.Mode=="stack" ? new Color(1f,.82f,.25f,.95f)
            : shield.Mode=="elements" ? new Color(.45f,.95f,.9f,.95f)
            : new Color(.62f,.78f,1f,.95f);
        GUI.DrawTexture(bar,Texture2D.whiteTexture);
        GUI.color=previous;
    }
    // 3141009 追加魔幻箭筒的装填进度：格子底边一条 2px 的条。
    // 不开 buff 时蓝（在弹匣里攒着），开着时金（命中会消耗）。
    static void DrawQuiverCharge(Rect slot)
    {
        int capacity = NativeQuiverBurst.Capacity;
        if (capacity <= 0) return;
        float fill = Mathf.Clamp01(NativeQuiverBurst.Loads / (float)capacity);
        if (fill <= 0f) return;
        var previous = GUI.color;
        var bar = new Rect(slot.x + 3f, slot.yMax - 4f, (slot.width - 6f) * fill, 2f);
        GUI.color = NativeQuiverBurst.Active ? new Color(1f, .82f, .25f, .95f) : new Color(.62f, .78f, 1f, .9f);
        GUI.DrawTexture(bar, Texture2D.whiteTexture);
        GUI.color = previous;
    }
    static void DrawCentered(Rect slot,Texture2D texture)
    {
        GUI.DrawTexture(new Rect(slot.x+(32-texture.width)/2f,slot.y+(32-texture.height)/2f,texture.width,texture.height),texture);
    }
}
