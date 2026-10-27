using System;
using System.Collections.Generic;
namespace Server.Bot
{
    // Monster.distributePartyExperience/distributePlayerExperience; status/world rates are supplied by the existing character layer.
    public static class BotPartyExperience
    {
        public readonly struct Award
        {
            public readonly int personal, party, reputationCalls;
            public readonly bool eligible;
            public Award(int personal,int party,bool eligible,int reputationCalls=1)
            { this.personal=personal; this.party=party; this.eligible=eligible; this.reputationCalls=eligible ? reputationCalls : 0; }
        }
        static int Rounded(float value) => value >= int.MaxValue ? int.MaxValue : value <= int.MinValue ? int.MinValue : (int)Math.Floor((double)value+.5);
        public static Award Calculate(DbStore db,BotPartyService parties,BotActor player,int mobLevel,int mobExp,long totalHp,
            IReadOnlyDictionary<int,long> damage,float personalRate=1,float partyRate=1)
        {
            if (player==null || totalHp<=0) return default;
            var party=parties.For(player.id);
            long dealt=0,maxDamage=0; int mvp=0;
            if (party==null)
            {
                damage.TryGetValue(player.id,out dealt);
                return new Award(Rounded(dealt*((float)mobExp/totalHp)*personalRate),0,dealt>0);
            }
            foreach (var entry in damage)
            {
                var actor=parties.Get(entry.Key);
                if (actor==null || !actor.online || actor.world!=player.world || actor.channel!=player.channel || actor.mapId!=player.mapId || parties.For(actor.id)!=party) continue;
                dealt+=entry.Value;
                if (entry.Value>maxDamage) { maxDamage=entry.Value; mvp=entry.Key; }
            }
            if (dealt<=0) return default;
            int totalLevel=0,count=0;
            bool eligible=false;
            int minimum=mobLevel-GameConfigDao.GetInt(db,"exp_split_level_interval",5);
            foreach (var actor in parties.Members(player.id))
            {
                if (!actor.online || actor.world!=player.world || actor.channel!=player.channel || actor.mapId!=player.mapId) continue;
                if (actor.level<minimum && (!damage.TryGetValue(actor.id,out long contribution) || contribution<=0)) continue;
                totalLevel+=actor.level; count++; if (actor.id==player.id) eligible=true;
            }
            if (!eligible || totalLevel<=0) return default;
            float share=(float)GameConfigDao.GetFloat(db,"exp_split_common_mod",.8)*player.level/totalLevel;
            if (mvp==player.id) share+=(float)GameConfigDao.GetFloat(db,"exp_split_mvp_mod",.2);
            float personal=share*(dealt*((float)mobExp/totalHp));
            float bonus=count>1 ? .05f*count*personal : 0;
            return new Award(Rounded(personal*personalRate),Rounded(bonus*partyRate*(float)GameConfigDao.GetFloat(db,"party_bonus_exp_rate",1)),true,2);
        }
    }
}
