using System;
using System.Collections.Generic;
using UnityEngine;
using Server.Field;

// Only invoked after the pure server dispatcher has checked command_info and GM rank.
public static partial class NativeGmCommandHost
{
    static int N(string[] a,int i=0) { if(a.Length<=i)throw new ArgumentException("缺少命令参数");return NativeGmCommands.Number(a[i]); }
    static void Need(string[] a,int count,string usage) {if(a.Length<count)throw new ArgumentException("用法："+usage);}
    public static NativeCommandResult Execute(CharRecord c,string name,string[] a,string raw)
    {
        var player=GameLoop.I?.player;
        var field=GameLoop.I?.field;
        if(player==null||field?.Map==null)return NativeCommandResult.Fail("请先进入地图");
        var r=new NativeCommandResult();
        var db=GameSession.Server.Db;
        switch(name)
        {
            case "WarpCommand":
                Warp(field,player,N(a),a.Length>1?a[1]:null);break;
            case "GotoCommand":
                if(a.Length==0) {r.messages.Add(string.Join(", ",NativeGmDestinations.Maps.Keys));return r;}
                if(!NativeGmDestinations.Maps.TryGetValue(a[0],out int destination))throw new ArgumentException("未知地点，可用地点："+string.Join(", ",NativeGmDestinations.Maps.Keys));
                Warp(field,player,destination,null);break;
            case "PosCommand":r.messages.Add("地图 "+field.Map.id+"，x="+Mathf.RoundToInt(player.FeetPos.x*Art.PPU)+"，y="+Mathf.RoundToInt(-player.FeetPos.y*Art.PPU));return r;
            case "BossHpCommand":case "MobHpCommand":
                foreach(var mob in EnemyUnit.All.ToArray())if(mob!=null && !mob.dead && (name=="MobHpCommand" || mob.IsBoss))
                    r.messages.Add(mob.fieldMobId+" "+mob.label+" HP "+(mob.NativeController?.CurrentHp ?? (long)mob.hp)+"/"+(mob.NativeController?.MaximumHp ?? (long)mob.hpMax));
                if(r.messages.Count==0)r.messages.Add("当前地图没有符合条件的怪物");return r;
            case "SpawnCommand":
                int id=N(a),count=a.Length>1?N(a,1):1;
                if(count<1)throw new ArgumentException("召唤数量必须大于0");
                FieldCatalog.LoadMob(id);if(!FieldCatalog.Mobs.ContainsKey(id))throw new ArgumentException("怪物不存在："+id);
                for(int i=0;i<count;i++) if(field.SpawnNativeMob(id,player.transform.position)==null)throw new ArgumentException("怪物生成失败："+id);
                break;
            case "PinkbeanCommand":case "PapCommand":case "PianusCommand":
                id=name=="PinkbeanCommand"?8820001:name=="PapCommand"?8500001:8510000;
                if(field.SpawnNativeMob(id,player.transform.position)==null)throw new ArgumentException("怪物生成失败："+id);break;
            case "KillAllCommand":
                count=0;
                foreach(var mob in EnemyUnit.All.ToArray())
                    if(mob!=null && !mob.dead && NativeCombatData.Int(mob.CombatStats,"friendly")==0 && !(mob.fieldMobId>=8810010 && mob.fieldMobId<=8810018))
                    {mob.TakeBotHit((int)c.id,int.MaxValue,0);count++;}
                r.messages.Add("已攻击 "+count+" 只怪物");return r;
            case "BuffMeCommand":case "BuffMapCommand":case "EmpowerMeCommand":case "BuffCommand":case "HideCommand":case "UnHideCommand":
                int[] buffs=name=="BuffMeCommand"?new[]{4101004,2311003,1301007,2301004,1005}:
                    name=="BuffMapCommand"?new[]{9101001,9101002,9101003,9101008,1005}:
                    name=="EmpowerMeCommand"?new[]{2311003,2301004,1301007,4101004,2001002,1101007,1005,2301003,5121009,1111002,4111001,4111002,4211003,4211005,1321000,2321004,3121002}:
                    name=="HideCommand"||name=="UnHideCommand"?new[]{9101004}:new[]{N(a)};
                foreach(int skill in buffs)
                {
                    if(skill==9101004 && NativeBuffs.For(player).HasSource(skill)) {NativeBuffs.For(player).Cancel(skill);continue;}
                    int level=SkillBook.MaxLevelOf(skill);if(level<=0)throw new ArgumentException("技能不存在："+skill);
                    if(!NativeBuffs.For(player).ApplySkill(skill,level,NativeBuffs.Now))throw new ArgumentException("技能效果无法应用："+skill);
                }
                if(name=="BuffMeCommand") {player.hp=player.hpMax;player.mp=player.mpMax;}
                GameSession.SaveToDb();break;
            case "MusicCommand":
                Need(a,1,"!music <Bgm00/SleepyWood>");
                // Original preserves LastCommandMessage casing and matches against GAME_SONGS.
                string song=null;foreach(var candidate in NativeGmDestinations.Songs)if(string.Equals(raw,candidate,StringComparison.OrdinalIgnoreCase)){song=candidate;break;}
                if(song==null||!AudioManager.Has(song))throw new ArgumentException("音乐不存在："+raw);
                AudioManager.PlayBgm(song);break;
            case "ItemDropCommand":
                id=N(a);int quantity=a.Length>1?N(a,1):1;
                if(Server.Bot.BotItemCatalog.Shared.Info(id)==null || quantity<1||quantity>short.MaxValue)throw new ArgumentException("物品或数量无效");
                bool pet=id/1000==5000;
                if(pet && a.Length<2)throw new ArgumentException("用法：!drop <宠物ID> <天数>");
                if(GameConfigDao.GetBool(db,"block_generate_cash_item") && Server.Bot.BotItemCatalog.Int(Server.Bot.BotItemCatalog.Shared.Info(id),"cash")!=0)throw new ArgumentException("禁止生成现金物品");
                var item=Server.Bot.BotItemCatalog.Shared.CleanItem(id,pet?1:quantity);item.owner=c.name;
                if(pet)
                {
                    item.petId=(int)db.Table("pets").Insert(new DbRow {["name"]=Server.Bot.BotItemCatalog.Shared.Name(id),["level"]=1L,["closeness"]=0L,["fullness"]=100L,["summoned"]=0L,["flag"]=0L});
                    item.expiration=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()+quantity*86400000L;item.owner="";db.Save();
                }
                if(c.gm<3) {item.flag|=8|256|64;item.owner="TRIAL-MODE";}
                MapItemService.SpawnItemDrop(c.map,(int)c.id,(int)c.id,0,item,player.FeetPos.x*Art.PPU,-player.FeetPos.y*Art.PPU,true,true);break;
            case "DropLimitCommand":r.messages.Add("当前掉落 "+MapItemService.DroppedItemCount(c.map)+" / "+MapItemService.ItemLimitOnMap);return r;
            case "ClearDropsCommand":MapItemService.ClearMap(c.map);break;
            case "LevelCommand":case "LevelProCommand":
                // 上限跟 ExpTable.MaxLevel（300）；骑士团仍按源端的 120 级
                int target=Mathf.Clamp(N(a),1,Profile.JobId>=1000 && Profile.JobId<2000?120:ExpTable.MaxLevel);
                if(name=="LevelCommand") {Profile.Data.level=target-1;Profile.Data.experience=0;}
                LevelSystem.SetLevel(Profile.Data,target);player.ApplyProgression();player.hp=player.hpMax;player.mp=player.mpMax;GameSession.SaveToDb();break;
            case "JobCommand":
                Need(a,1,"!job [角色名] <职业ID>");Self(c,a.Length>1?a[0]:c.name);
                id=N(a,a.Length>1?1:0);if(id<0||id>=2200||!JobDefs.IsValid(id))throw new ArgumentException("职业不存在："+id);
                Profile.SetJob(id);player.ApplyProgression();player.RefreshFieldAppearance();GameSession.SaveToDb();break;
            case "MaxSkillCommand":case "ResetSkillCommand":
                // 源端是按「新手技能 + 当前职业技能」打满/清零，不枚举 String/Skill.img。
                // 旧写法直接读 String/Skill.img 的节点名：真机只带直读索引时这一层不存在，
                // 于是 !maxskill 抛「缺少 String/Skill.img」→ 聊天里看着像指令没反应。
                SkillBook.Ensure();
                var learn=new List<int>();
                var beginner=JobDefs.Get(0);
                if(beginner!=null && beginner.allSkills!=null)learn.AddRange(beginner.allSkills);
                var current=JobDefs.Get(Profile.JobId);
                if(current!=null && current.allSkills!=null)foreach(int skill in current.allSkills)if(!learn.Contains(skill))learn.Add(skill);
                if(learn.Count==0)throw new ArgumentException("当前职业没有可加的技能："+Profile.JobId);
                foreach(int skill in learn)
                {
                    int max=SkillBook.MaxLevelOf(skill);if(max>0)SkillProgress.SetLearned(skill,name=="MaxSkillCommand"?max:0);
                }
                // Original removes the incompatible mount skill.
                SkillProgress.SetLearned(Profile.JobId>=2000 && Profile.JobId<2200?5001005:21001001,0);
                // 源端这里有一句 yellowMessage。带职业名回显：技能窗的页签是按 Profile.JobId
                // 的职业链取的，玩家要是看到「二三转技能书」，这行就能立刻暴露职业不对。
                r.messages.Add((name=="MaxSkillCommand"?"技能全满：":"技能清零：")+JobDefs.NameOf(Profile.JobId)+
                               "（job "+Profile.JobId+"）共 "+learn.Count+" 个");
                player.ApplyProgression();GameSession.SaveToDb();break;
            case "FaceCommand":case "HairCommand":
                Need(a,1,"!face / !hair [角色名] <ID>");Self(c,a.Length>1?a[0]:c.name);id=N(a,a.Length>1?1:0);
                string part=name=="FaceCommand"?"Face":"Hair";
                if((part=="Face"?id/10000!=2:id/10000!=3) || WzAssets.FileRoot("Character/"+part+"/"+id.ToString("D8")+".img")==null)throw new ArgumentException("外观不存在："+id);
                if(part=="Face")c.face=id;else c.hair=id;CharactersDao.Save(db,c);db.Save();r.changed=true;break;
            case "SearchCommand":case "IdCommand":
                Need(a,2,"!search <item|map|mob|skill> <关键词>");string query=string.Join(" ",a,1,a.Length-1);count=0;
                if(a[0]=="item")foreach(int itemId in Server.Bot.BotItemCatalog.Shared.ItemIds)
                {var label=Server.Bot.BotItemCatalog.Shared.Name(itemId);if(Match(itemId,label,query)) {r.messages.Add(itemId+" "+label);if(++count>=100)break;}}
                else if(a[0]=="map")foreach(var entry in FieldCatalog.Maps) {if(Match(entry.Key,entry.Value.name,query)) {r.messages.Add(entry.Key+" "+entry.Value.name);if(++count>=100)break;}}
                else if(a[0]=="mob")foreach(var entry in FieldCatalog.Mobs) {if(Match(entry.Key,entry.Value.name,query)) {r.messages.Add(entry.Key+" "+entry.Value.name);if(++count>=100)break;}}
                else if(a[0]=="skill")foreach(int skill in NativeSkillIds()) {var data=SkillBook.Get(skill);if(Match(skill,data?.name,query)) {r.messages.Add(skill+" "+data?.name);if(++count>=100)break;}}
                else throw new ArgumentException("搜索类型：item、map、mob、skill");
                if(count==0)r.messages.Add("没有搜索结果");return r;
            case "WhatDropsFromCommand":
                id=N(a);if(!FieldCatalog.Drops.TryGetValue(id,out var drops)) {r.messages.Add("没有掉落记录");return r;}
                foreach(var entry in drops)r.messages.Add(entry.item+" "+Server.Bot.BotItemCatalog.Shared.Name(entry.item)+" × "+entry.min+"~"+entry.max+" 概率 "+entry.chance+"/999999");return r;
            case "WhoDropsCommand":
                id=N(a);foreach(var pair in FieldCatalog.Drops)if(pair.Value.Exists(d=>d.item==id))r.messages.Add(pair.Key+" "+(FieldCatalog.Mobs.TryGetValue(pair.Key,out var mob)?mob.name:""));
                if(r.messages.Count==0)r.messages.Add("没有掉落记录");return r;
            case "ReloadEventsCommand":ScriptSource.ClearCache();break;
            case "ClearQuestCacheCommand":QuestCatalog.ClearCache();break;
            case "ClearQuestCommand":QuestCatalog.ClearCache(N(a));break;
            case "QuestStartCommand":case "QuestCompleteCommand":case "QuestResetCommand":
                id=N(a);if(!QuestCatalog.Exists(id))throw new ArgumentException("任务不存在："+id);
                int status=Profile.QuestStatus(id);
                if(name=="QuestStartCommand") {if(status!=0)throw new ArgumentException("任务已开始或完成");Profile.StartQuest(id);}
                else if(name=="QuestCompleteCommand") {if(status!=1)throw new ArgumentException("任务尚未开始");Profile.CompleteQuest(id);QuestNotice.ShowCompleteEffect();}
                else Profile.ResetQuest(id);
                GameSession.SaveToDb();break;
            case "GmShopCommand":NativeNpcShopWindow.OpenShop(1337,false);if(!NativeNpcShopWindow.Visible)throw new ArgumentException("GM商店1337无法打开");break;
            case "GachaListCommand":NpcSession.Talk(9900001,"gachaponInfo");if(NpcSession.Active?.LastError!=null)throw new ArgumentException(NpcSession.Active.LastError);break;
            case "ReloadMapCommand":field.ReloadCurrentMap();break;
            case "ReloadPortalsCommand":ScriptSource.ClearCache();break;
            case "DisposeCommand":case "UnBugCommand":
                NpcSession.CloseActive();player.CancelRush();player.FinishHeldSkill();break;
            case "NoticeCommand":
                Need(a,1,"!notice <内容>");NativeSystemMessage.Drop(6,raw);return r;
            case "DebugCommand":case "CheckDmgCommand":
                if(a.Length>0)Self(c,a[0]);
                r.messages.Add(c.name+" Lv."+Profile.level+" Job."+Profile.JobId+" HP "+player.hp+"/"+player.hpMax+" MP "+player.mp+"/"+player.mpMax+" ATK "+player.EffectiveAtk);return r;
            default:return Extended(c,name,a,raw);
        }
        r.messages.Add("命令执行完成");return r;
    }
    static bool Match(int id,string label,string query)=>id.ToString().Contains(query) || (label??"").IndexOf(query,StringComparison.OrdinalIgnoreCase)>=0;
    static IEnumerable<int> NativeSkillIds()
    {
        // 直读索引里没有 String/Skill.img 时退回职业表里的技能集合（真机常见），
        // 不能因为缺一层字符串表就让 !search skill / !maxskill 直接抛错。
        var root=WzAssets.FileRoot("String/Skill.img");
        if(root!=null)
        {
            foreach(var child in root.Children)if(int.TryParse(child.Name,out int id))yield return id;
            yield break;
        }
        var seen=new HashSet<int>();
        foreach(var job in JobDefs.All)
        {
            if(job.allSkills==null)continue;
            foreach(int skill in job.allSkills)if(seen.Add(skill))yield return skill;
        }
    }
    static void Self(CharRecord c,string name) {if(!string.Equals(c.name,name,StringComparison.OrdinalIgnoreCase) && c.id.ToString()!=name)throw new ArgumentException("角色不在线："+name);}
    static void Warp(FieldDirector field,PlayerCtrl player,int id,string portal)
    {
        if(player.hp<=0)throw new ArgumentException("死亡状态下不能传送");
        var target=FieldCatalog.GetMap(id);if(target==null)throw new ArgumentException("地图不存在："+id);
        int index=-1;if(portal!=null && target.Portal(portal)==null && int.TryParse(portal,out int n)) {index=n;portal=null;}
        field.WarpTo(id,portal,index);
    }
}
