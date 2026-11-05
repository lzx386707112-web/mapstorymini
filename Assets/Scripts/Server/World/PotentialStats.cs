using System;
using System.Collections.Generic;

// Permanent scalar effects only. Conditional effects retain their own proc/state semantics.
public static class PotentialStats
{
    static readonly Dictionary<string,string> Keys = new Dictionary<string,string> {
        {"incSTR","STR"},{"incDEX","DEX"},{"incINT","INT"},{"incLUK","LUK"},
        {"incMHP","HP"},{"incMMP","MP"},{"incPAD","PAD"},{"incMAD","MAD"},
        {"incPDD","PDD"},{"incMDD","MDD"},{"incACC","ACC"},{"incEVA","EVA"},
        {"incSTRr","STRPct"},{"incDEXr","DEXPct"},{"incINTr","INTPct"},{"incLUKr","LUKPct"},
        {"incMHPr","HPPct"},{"incMMPr","MPPct"},{"incPADr","PADPct"},{"incMADr","MADPct"},
        {"incPDDr","PDDPct"},{"incMDDr","MDDPct"},{"incACCr","ACCPct"},{"incEVAr","EVAPct"},
        {"incCr","CriticalChance"},{"incCriticaldamage","CriticalDamage"},
        {"incSpeed","Speed"},{"incJump","Jump"},{"incEXPr","ExpRate"},
        {"incMesoProp","MesoRate"},{"incRewardProp","DropRate"},
        {"ignoreTargetDEF","IgnoreDefense"}
    };
    static readonly Dictionary<string,string> LevelKeys=new Dictionary<string,string> {
        {"incSTRlv","STR"},{"incDEXlv","DEX"},{"incINTlv","INT"},{"incLUKlv","LUK"},{"incMHPlv","HP"}
    };
    public static Dictionary<string,int> Calculate(PotentialCatalog catalog,int equipLevel,IEnumerable<int> options,int characterLevel=0)
    {
        var result=new Dictionary<string,int>();
        int level=PotentialCatalog.OptionLevel(equipLevel);
        foreach(int id in options)
        {
            if(id<=0)continue; // zero=absent, negative=unappraised
            var values=catalog.ValuesAt(id,level);
            if(values==null)throw new InvalidOperationException("潜能等级档不存在："+id+"/"+level);
            foreach(var pair in CalculateValues(values))Add(result,pair.Key,pair.Value);
            // User-approved correction: the client descriptions specify every nine levels.
            foreach(var pair in LevelKeys)
                if(values.TryGetValue(pair.Key,out var value))Add(result,pair.Value,checked(Convert.ToInt32(value)*(Math.Max(0,characterLevel)/9)));

        }
        return result;
    }
    public static Dictionary<string,int> CalculateValues(Dictionary<string,object> values)
    {
        var result=new Dictionary<string,int>();if(values==null)return result;
        foreach(var pair in Keys)
            if(values.TryGetValue(pair.Key,out var value))Add(result,pair.Value,Convert.ToInt32(value));
        if(values.TryGetValue("incDAMr",out var damage))
            Add(result,values.TryGetValue("boss",out var boss)&&Convert.ToInt32(boss)!=0?"BossDamage":"Damage",Convert.ToInt32(damage));
        return result;
    }
    public static void Add(Dictionary<string,int> values,string key,int amount)
        =>values[key]=checked((values.TryGetValue(key,out int current)?current:0)+amount);
}
