using UnityEngine;

// BeiDou.exe CTemporaryStatView: 0x7B2BB0, 0x7B2E58, 0x7B44F4.
// Use IMG pixels directly; keep the native single row ending 3px from the right.
public static class TemporaryStatView
{
    public static Rect Slot(int index, int count, float width)
        => new Rect(width-3-count*32+index*32,23,32,32);
    public static void Draw()
    {
        var player=GameLoop.I != null ? GameLoop.I.player : null;
        var buffs=player != null ? player.GetComponent<NativeBuffs>() : null;
        if (buffs==null) return;
        var effects=buffs.Visible;
        // 魔幻箭筒的箭镞是常驻状态（无时限），不进 NativeBuffs，单独占最左一格：
        // 图标取当前箭镞对应的技能（3140010 魔法箭 / 3140011 吸血箭）。
        bool arrow=NativeArrowMode.Active;
        int total=effects.Count+(arrow ? 1 : 0);
        NativeBuffs.Effect hover=null;
        bool arrowHover=false;
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
        if (arrowHover)
        {
            var toggle=SkillBook.Get(NativeArrowMode.Toggle);
            if (toggle!=null) SkillWindow.DrawSkillData(toggle,Pointer.Pos,
                Mathf.Max(1,SkillProgress.LevelOf(NativeArrowMode.Toggle)));
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
