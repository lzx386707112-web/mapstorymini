using System.Collections.Generic;
using Server.Bot;
using UnityEngine;

// The field owns Unity monster identities; the bot server receives no Unity references.
public sealed class BotCombatField : MonoBehaviour
{
    sealed class Number { public EnemyUnit target; public int amount; public double due; }
    readonly Dictionary<int,EnemyUnit> targets=new Dictionary<int,EnemyUnit>();
    readonly List<Number> pending=new List<Number>();
    FieldDirector field;
    BotPartyGrindService service;
    public void Bind(FieldDirector owner,BotRuntime runtime)
    {
        if (service==runtime.partyGrind) return;
        Unbind(); field=owner; service=runtime.partyGrind;
        service.Monsters=Snapshot; service.Hit=Hit; service.Notice=Notice; service.Effect=Effect;
    }
    List<BotGrindMob> Snapshot(int mapId,int world,int channel)
    {
        var result=new List<BotGrindMob>(); targets.Clear();
        if (field==null || field.Map==null || field.Map.id!=mapId || world!=GameSession.World || channel!=GameSession.Channel) return result;
        foreach (var enemy in EnemyUnit.All)
        {
            if (enemy==null || !enemy.fieldMode || enemy.dead) continue;
            int id=enemy.GetInstanceID(); targets[id]=enemy;
            result.Add(new BotGrindMob { id=id,mapId=mapId,world=world,channel=channel,
                x=Mathf.RoundToInt(enemy.transform.position.x*Art.PPU), y=Mathf.RoundToInt(-enemy.transform.position.y*Art.PPU),
                maxHp=enemy.NativeController!=null ? enemy.NativeController.MaximumHp : (long)enemy.hpMax,alive=true,boss=enemy.IsBoss });
        }
        return result;
    }
    void Hit(BotCharacter bot,BotGrindMob mob,int damage,BotAttack.Skill skill,int[] lines)
    {
        if (!targets.TryGetValue(mob.id,out var enemy) || enemy==null || enemy.dead || field.Map==null || field.Map.id!=bot.mapId ||
            bot.world!=GameSession.World || bot.channel!=GameSession.Channel) return;
        if (skill.id>0)
        {
            var presentation=GetComponent<BotField>();
            presentation?.PlaySkillEffect(bot,skill.id);
        }
        if (lines.Length>0) DamageText.Show(enemy.DamageAnchor,lines[0],"red",1);
        for (int i=1;i<lines.Length;i++) pending.Add(new Number { target=enemy,amount=lines[i],due=Time.realtimeSinceStartupAsDouble+i*.08 });
        enemy.TakeBotHit(bot.id,damage,bot.facing);
        mob.alive=!enemy.dead;
    }
    static void Effect(int playerId,bool active)
    { if (GameSession.InGame && GameSession.CharacterId==playerId) GameLoop.I?.player?.SetPartyGrindEffect(active); }
    static void Notice(int playerId,string text)
    { if (GameSession.InGame && GameSession.CharacterId==playerId) NativeSystemMessage.Drop(5,text); }
    void Update()
    {
        double now=Time.realtimeSinceStartupAsDouble;
        for (int i=0;i<pending.Count;)
        {
            var number=pending[i];
            if (number.target==null || number.target.dead) { pending.RemoveAt(i); continue; }
            if (number.due>now) { i++; continue; }
            DamageText.Show(number.target.DamageAnchor,number.amount,"red",1); pending.RemoveAt(i);
        }
    }
    void Unbind()
    {
        if (service!=null)
        {
            if (service.Monsters==Snapshot) service.Monsters=null;
            if (service.Hit==Hit) service.Hit=null;
            if (service.Notice==Notice) service.Notice=null;
            if (service.Effect==Effect) service.Effect=null;
        }
        service=null; pending.Clear(); targets.Clear();
    }
    void OnDisable() => Unbind();
}
