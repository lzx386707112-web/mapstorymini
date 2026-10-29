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
        NativeBuffs.Effect hover=null;
        for (int i=0;i<effects.Count;i++)
        {
            var effect=effects[i];
            var rect=Slot(i,effects.Count,UiKit.W);
            var skill=SkillBook.Get(effect.Id);
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
    static void DrawCentered(Rect slot,Texture2D texture)
    {
        GUI.DrawTexture(new Rect(slot.x+(32-texture.width)/2f,slot.y+(32-texture.height)/2f,texture.width,texture.height),texture);
    }
}
