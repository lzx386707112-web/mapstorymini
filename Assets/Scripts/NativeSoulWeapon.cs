using System;
using System.Collections.Generic;
using UnityEngine;

public static class NativeSoulWeapon
{
    public static bool IsSkill(int id)=>id>=80012853 && id<=80012856;
    public static int LevelOf(int id)
    {
        var c=GameSession.Character;var s=c?.soulWeapon;
        return s!=null && s.Equipped(c) && SoulWeaponWz2Catalog.Int(SoulWeaponWz2Catalog.Grade(s.grade),"ActiveSkillID")==id
            ? SoulWeaponWz2Catalog.Int(SoulWeaponWz2Catalog.Grade(s.grade),"ActiveSkillLevel") : 0;
    }
    public static SkillData Definition(int id)
    {
        var source=SoulWeaponWz2Catalog.Map(SoulWeaponWz2Catalog.Map(SoulWeaponWz2Catalog.Data,"skills"),id.ToString());
        var common=SoulWeaponWz2Catalog.Map(source,"common");var text=SoulWeaponWz2Catalog.Map(source,"string");
        var data=new SkillData {id=id,job=8001,maxLevel=SoulWeaponWz2Catalog.Int(common,"maxLevel"),type="buff",autoLearn=true,
            name=SoulWeaponWz2Catalog.Text(text,"name","灵魂的斗志"),desc=SoulWeaponWz2Catalog.Text(text,"desc"),
            icon="wz://Skill/800128.img/skill/"+id+"/icon"};
        for(int lv=1;lv<=data.maxLevel;lv++)
        {
            var row=new Dictionary<string,object>();foreach(var pair in common)
                if(pair.Key!="maxLevel")row[pair.Key]=GrowthFormula.Evaluate(pair.Value.ToString(),lv);
            data.levels[lv.ToString()]=row;
        }
        return data;
    }
    public static string Cast(int id)
    {
        var player=GameLoop.I?.player;
        if(!GameSession.InGame || player==null || player.hp<=0 || GameLoop.I.gameOver || GameLoop.I.state!=GameLoop.GameState.Playing)return "当前无法施放灵魂技能";
        if(LevelOf(id)<=0)return "请装备已启用的灵魂武器";
        var s=GameSession.Character.soulWeapon;
        var response=GameSession.Net.Soul(new SoulRequest {characterId=GameSession.CharacterId,operation=SoulOperation.CastModern,commit=true,revision=s.revision});
        if(response.modern!=null)GameSession.Character.soulWeapon=response.modern;
        if(!response.ok)return response.error;
        NativeGrowthStats.Invalidate();Tick();player.PlayNativeSkill(id);player.RefreshBuffAttributes();return null;
    }
    public static void Tick()
    {
        var player=GameLoop.I?.player;if(player==null)return;
        var buffs=player.GetComponent<NativeBuffs>();var c=GameSession.Character;var s=c?.soulWeapon;
        long now=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        for(int id=80012853;id<=80012856;id++)
        {
            bool active=s!=null && s.activeUntil>now && LevelOf(id)>0;
            if(active)
            {
                buffs=buffs??NativeBuffs.For(player);
                if(!buffs.HasSource(id))buffs.ApplySoulWeapon(id,LevelOf(id),(int)Math.Min(40000,s.activeUntil-now));
            }
            else if(buffs?.HasSource(id)==true)buffs.Cancel(id);
        }
    }
}
