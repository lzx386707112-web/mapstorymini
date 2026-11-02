using UnityEngine;

// CField::ShowBossHP 5336CA; Monster.hasBossHPBar: boss && hpTagColor > 0.
public static class NativeBossHpBar
{
    const string Root="wz://UI/UIWindow.img/MobGage/";
    public static void Draw()
    {
        EnemyUnit selected=null;
        foreach(var mob in EnemyUnit.All)
            if(mob!=null && mob.fieldMode && !mob.dead && mob.IsBoss && Art.Num(mob.CombatStats,"hpTagColor",0)>0) selected=mob;
        if(selected==null)return;
        float s=BottomBar.S;
        float x=(UiKit.W-800*s)*.5f;
        void Image(string node,float px,float py,float width=-1)
        {
            var texture=Art.Tex(Root+node); if(texture==null)return;
            GUI.DrawTexture(new Rect(x+px*s,py*s,(width<0?texture.width:width)*s,texture.height*s),texture);
        }
        // 35px portrait base; 4px left + 758 repeated pixels + 5px right.
        Image("backgrnd",0,0);
        Image("backgrnd2",35,0);
        Image("backgrnd3",39,0,758);
        Image("backgrnd4",797,0);
        int id=selected.fieldMobId;
        if(id==8880700 || id==8880803)id=8870000;
        Image("Mob/"+id,5,6);
        int front=(int)Art.Num(selected.CombatStats,"hpTagColor",0);
        int back=(int)Art.Num(selected.CombatStats,"hpTagBgcolor",0);
        long hp=selected.NativeController!=null?selected.NativeController.CurrentHp:(long)selected.hp;
        long max=selected.NativeController!=null?selected.NativeController.MaximumHp:(long)selected.hpMax;
        int filled=max>0?(int)System.Math.Min(758,System.Math.Max(0,758.0*hp/max)):0;
        // Native paints the 1px cap and the repeated 12px gage at y=4.
        Image("Gage/"+front+"/0",38,4);
        if(filled>0)Image("Gage/"+front+"/1",39,4,filled);
        if(filled<758)Image("Gage/"+back+"/1",39+filled,4,758-filled);
        Image("Gage/"+(filled==758?front:back)+"/0",797,4);
    }
}
