using System;

// Only families verified in 282.5 Tms282CubeRuntime. Special cubes require their own rules.
public static class CubeRules
{
    public sealed class Family
    {
        public readonly bool bonus, choice, karma;
        public readonly int minimum, maximum;
        public readonly string policy;
        internal Family(bool bonus,bool choice,int maximum,bool karma,string policy,int minimum=1)
        {this.bonus=bonus;this.choice=choice;this.maximum=maximum;this.karma=karma;this.policy=policy;this.minimum=minimum;}
    }
    public static Family GetFamily(int id)
    {
        switch(id)
        {
            case 2711000:case 2711001:case 2711009:case 2711011:case 2711027:
                return new Family(false,false,2,false,"MYSTICAL_CUBES_TIER_UP_RATES");
            case 2711003:case 2711005:case 2711012:
                return new Family(false,false,3,false,"HARD_CUBES_TIER_UP_RATES");
            case 2711023:case 2711026:
                return new Family(false,false,3,true,"HARD_CUBES_TIER_UP_RATES");
            case 2711004:case 2711006:case 2711013:
                return new Family(false,false,4,false,"SOLID_CUBE_TIER_UP_RATES");
            case 2711017:case 2711021:case 2711028:
                return new Family(false,false,4,true,"SOLID_CUBE_TIER_UP_RATES");
            case 2711024:case 2711025:case 5062032:
                return new Family(false,true,4,true,"BRIGHT_CUBES_TIER_UP_RATES");
            case 5062030:
                return new Family(false,true,4,false,"BRIGHT_CUBES_TIER_UP_RATES");
            case 2730000:case 2730002:case 2730004:case 2730013:
                return new Family(true,false,2,false,"BONUS_MYSTICAL_CUBE_TIER_UP_RATES");
            case 2730006:case 2730020:
                return new Family(true,false,2,true,"BONUS_MYSTICAL_CUBE_TIER_UP_RATES");
            case 2730011:case 2730012:case 5062521:
                return new Family(true,true,4,true,"BONUS_BRIGHT_CUBE_TIER_UP_RATES");
            case 5062507:case 5062508:
                return new Family(true,false,4,id==5062508,"BONUS_POT_CUBE_TIER_UP_RATE",2);
            case 5062031:
                return new Family(true,true,4,false,"BONUS_BRIGHT_CUBE_TIER_UP_RATES",2);
            default:return null;
        }
    }
    public static long AppraisalCost(int equipLevel,int cubeId,int insightLevel=0)
    {
        var family=GetFamily(cubeId);
        if(family==null || equipLevel<0 || equipLevel>1000 || insightLevel<0 || insightLevel>100)
            throw new ArgumentException("Unsupported cube appraisal inputs");
        if(equipLevel<=30 || cubeId/1000000==5 && !family.bonus)return 0;
        int freeThrough=insightLevel>=90?120:insightLevel>=60?70:insightLevel>=30?30:0;
        if(equipLevel<=freeThrough)return 0;
        long square=(long)equipLevel*equipLevel;
        long raw=equipLevel<=70?square/2:equipLevel<=120?square*5/2:square*20;
        return raw/100*100;
    }
}
