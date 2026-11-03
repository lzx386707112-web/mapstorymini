using System;
using System.Collections.Generic;

// Pure roll operation. Payment and persisted comparison choices belong to NativePotentialService.
public sealed class PotentialRoller
{
    readonly PotentialPool pool;
    readonly CubeRateTable rates;
    public PotentialRoller(PotentialCatalog catalog,CubeRateTable rates)
    {pool=new PotentialPool(catalog);this.rates=rates ?? throw new ArgumentNullException(nameof(rates));}
    public sealed class Result {public int grade;public int[] options;}
    public Result Roll(int cubeId,int itemId,int equipLevel,int grade,int[] main,int[] bonus,Random random)
    {
        var family=CubeRules.GetFamily(cubeId);
        if(family==null||grade<family.minimum||grade>family.maximum||main==null||main.Length!=3||bonus==null||bonus.Length!=3||random==null)
            throw new ArgumentException("Invalid cube roll inputs");
        int configured=rates.TierUpOverride(cubeId,grade);
        bool up=grade<4 && (configured>=0?random.Next(10000)<configured:random.Next(1000)<rates.BuiltinTierUp(family.policy,grade));
        int rolledGrade=up&&grade<family.maximum?grade+1:grade;
        int[] original=family.bonus?bonus:main;
        int count=family.bonus?(original[2]!=0?3:original[1]!=0?2:1):(original[2]!=0?3:2);
        bool boosted=family.policy=="BONUS_BRIGHT_CUBE_TIER_UP_RATES";
        int extra=0;
        if(random.Next(1000)<(boosted?50:10)) {extra=1;if(random.Next(1000)<(boosted?10:0))extra=2;}
        var options=new int[3];
        for(int line=0;line<count;line++)
        {
            options[line]=Pick(cubeId,itemId,equipLevel,rolledGrade,family.bonus,
                family.policy=="SOLID_CUBE_TIER_UP_RATES"||family.policy=="HARD_CUBES_TIER_UP_RATES",
                line,extra,family.bonus?main[0]:options[0],random);
        }
        return new Result {grade=rolledGrade,options=options};
    }
    // Equip.releaseOptions: reveal only negative lines, without a tier-up roll or line-count change.
    // The system source (-1) permits auto-steal and uses the default prime distribution.
    public Result Reveal(int itemId,int equipLevel,int grade,bool bonus,int[] main,int[] additional,Random random)
    {
        if(grade<1||grade>4||main==null||main.Length!=3||additional==null||additional.Length!=3||random==null)
            throw new ArgumentException("Invalid potential reveal inputs");
        var options=(int[])(bonus?additional:main).Clone();
        int extra=random.Next(1000)<10?1:0;
        for(int line=0;line<3;line++)
            if(options[line]<0)
                options[line]=Pick(-1,itemId,equipLevel,grade,bonus,true,line,extra,bonus?main[0]:options[0],random);
        return new Result {grade=grade,options=options};
    }
    int Pick(int cubeId,int itemId,int equipLevel,int rolledGrade,bool bonus,bool steal,int line,int extra,int first,Random random)
    {
        int lineExtra=extra;
        int prime=rates.PrimeOverride(cubeId,rolledGrade,line+1);
        if(line>0&&prime>=0)lineExtra=random.Next(10000)<prime?line:0;
        int tier=line==0||lineExtra>=line?rolledGrade:rolledGrade-1;
        var legal=pool.Legal(itemId,equipLevel,tier,bonus,steal);
        // ItemConstants expands each WZ weight before table category matching.
        legal.RemoveAll(id=>pool.WeightOf(id)<=0);
        var weighted=rates.OptionWeights(cubeId,tier,legal);
        if(weighted!=null)legal.RemoveAll(id=>weighted.WeightOf(id)==0);
        // Source Equip.getRandomOption compares the first main option even for a bonus roll.
        if(line>0 && HasExclusion(first) && random.Next(100)<10)legal.RemoveAll(id=>Excludes(first,id));
        if(legal.Count==0)throw new InvalidOperationException("No supported potential outcome for equipment");
        {
            var parts=new List<string>();foreach(int id in legal)parts.Add(id+":"+(weighted==null?pool.WeightOf(id):weighted.WeightOf(id)));
            var restricted=new CubeRateTable.Weighted(string.Join(",",parts));
            return restricted.Pick(random.Next(restricted.Total));
        }
    }
    static readonly string[] Singles={"damagePercent","criticalDamagePercent","maxHPPercent","mesoObtained","itemDropRate"};
    static readonly string[] Stats={"strPercent","dexPercent","lukPercent","intPercent"};
    bool HasExclusion(int first)
    {
        foreach(string name in Singles)if(rates.InFamily(name,first))return true;
        foreach(string name in Stats)if(rates.InFamily(name,first))return true;
        return rates.InFamily("bossDamagePercent",first)||rates.InFamily("attPercent",first)||rates.InFamily("magicAttPercent",first)
            ||rates.InFamily("ignoreMonsterDEFPercent",first)||rates.InFamily("allStatsPercent",first);
    }
    bool Excludes(int first,int next)
    {
        bool Is(string name,int id)=>rates.InFamily(name,id);
        if(Is("bossDamagePercent",first))return Is("bossDamagePercent",next)||Is("attPercent",next)||Is("magicAttPercent",next);
        if(Is("damagePercent",first))return Is("damagePercent",next);
        if(Is("attPercent",first))return Is("attPercent",next)||Is("bossDamagePercent",next);
        if(Is("magicAttPercent",first))return Is("magicAttPercent",next)||Is("bossDamagePercent",next);
        if(Is("ignoreMonsterDEFPercent",first))return Is("ignoreMonsterDEFPercent",next)||Is("bossDamagePercent",next);
        if(Is("allStatsPercent",first)) {foreach(string name in Stats)if(Is(name,next))return true;return false;}
        foreach(string name in Stats)if(Is(name,first))return Is(name,next)||Is("allStatsPercent",next);
        foreach(string name in Singles)if(Is(name,first))return Is(name,next);
        return false;
    }
}
