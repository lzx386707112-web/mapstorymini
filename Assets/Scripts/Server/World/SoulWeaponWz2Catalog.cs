using System;
using System.Collections.Generic;

// Original 282 tables. Missing server policies are documented in 29-soul-weapon-wz2.md.
public static class SoulWeaponWz2Catalog
{
    static Dictionary<string,object> data;
    public static Dictionary<string,object> Data => data ??= MiniJsonDb.Parse(PlatformRes.LoadText("Enhancement/SoulWeaponWz2")) as Dictionary<string,object>
        ?? throw new InvalidOperationException("缺少282灵魂武器数据");
    public static Dictionary<string,object> Map(Dictionary<string,object> d,string key)
        => d!=null && d.TryGetValue(key,out var value) ? value as Dictionary<string,object> : null;
    public static int Int(Dictionary<string,object> d,string key,int fallback=0)
        => d!=null && d.TryGetValue(key,out var value) && int.TryParse(value?.ToString(),out int number) ? number : fallback;
    public static string Text(Dictionary<string,object> d,string key,string fallback="")
        => d!=null && d.TryGetValue(key,out var value) ? value?.ToString() ?? fallback : fallback;
    public static Dictionary<string,object> Rules => Map(Data,"rules");
    public static Dictionary<string,object> Grade(int grade) => Map(Map(Rules,"GradeInfo"),grade.ToString());
    public static int RequiredExp(int nextLevel) => Int(Map(Map(Rules,"LevelInfo"),nextLevel.ToString()),"ReqExp");
    public static int MaxLevel(int grade) => Int(Grade(grade),"MaxLevel");
    public static int PassiveAttack(int level) => level<1 || level>100 ? 0 : level<=60 ? level+20 : level*2-40; // EXE RVA 6A50E80
    public static Dictionary<string,object> Option(int id) => Map(Map(Data,"options"),id.ToString("D6"));
    public static Dictionary<string,object> OptionStats(int id) => Map(Map(Option(id),"level"),"1");
    public static string DescribeOption(int id)
    {
        var stats=OptionStats(id);
        string text=Text(Map(Option(id),"info"),"string",id==0?"尚未设置灵魂烙印":"未知灵魂烙印");
        if(stats==null)return text;
        // Long keys first: incSTRr must not be partially replaced as incSTR.
        var keys=new List<string>(stats.Keys);keys.Sort((a,b)=>b.Length.CompareTo(a.Length));
        foreach(string key in keys)text=text.Replace("#"+key,stats[key].ToString());
        return text;
    }
    public static List<int> RollOptions(int grade)
    {
        var result=new List<int>();
        foreach(var pair in Map(Data,"options"))
            if(int.TryParse(pair.Key,out int id) && id/10000==grade && id%10000<1000)result.Add(id);
        result.Sort();return result;
    }
    public static Dictionary<string,object> Skill(int grade) => Map(Map(Data,"skills"),Int(Grade(grade),"ActiveSkillID").ToString());
    public static int ActiveAttackPercent(int grade)
    {
        var g=Grade(grade);return GrowthFormula.Evaluate(Text(Map(Skill(grade),"common"),"indiePadR","0"),Int(g,"ActiveSkillLevel"));
    }
    public static string SkillName(int grade) => Text(Map(Skill(grade),"string"),"name","灵魂的斗志");
    public static Dictionary<string,object> Bosses => Map(Rules,"BossInfo");
    public static string BossName(Dictionary<string,object> boss)
    {
        string name=Text(Map(Map(Map(Data,"quests"),Int(boss,"QuestID_Weekly").ToString()),"QuestInfo"),"name");
        return name.Replace("[靈魂武器] ","").Replace("1人擊破","");
    }
}
