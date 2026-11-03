using System;
using System.Collections.Generic;

// 282.5 ItemData.createFilteredOptions and ItemConstants.getOptionsByEquip.
// Grade index is 0=rare secondary, 1=rare, 2=epic, 3=unique, 4=legendary.
public sealed class PotentialPool
{
    readonly PotentialCatalog catalog;
    static readonly HashSet<int> Removed = new HashSet<int> {
        40081,10222,10227,10232,10237,10242,10247,32661,42661,20396,40057,
        42061,42062,22056,32052,32054,32058,32071,42052,42054,42058,42071
    };
    public PotentialPool(PotentialCatalog catalog) {this.catalog=catalog ?? throw new ArgumentNullException(nameof(catalog));}
    public int WeightOf(int id)
    {
        var info=catalog.Info(id);
        int weight=info!=null&&info.TryGetValue("weight",out var value)?Convert.ToInt32(value):0;
        return weight==0?1:weight; // ItemData.loadItemOptionsFromWZ normalizes zero/missing to one.
    }
    public static int GradeOf(int id)
        =>id>0&&id<10000?0:id>10000&&id<20000?1:id>20000&&id<30000?2:id>30000&&id<40000?3:id>40000&&id<50000?4:-1;
    public static bool IsBonus(int id)=>id>2000 && id/1000%10==2;
    public static bool SourceAllows(int id)
    {
        int suffix=id%1000;
        return id>0 && suffix!=14 && suffix!=54 && (suffix!=7 || id==41007) && (suffix!=47 || id==12047)
            && suffix!=8 && (suffix!=48 || id==12048) && suffix!=202 && suffix!=207 && suffix!=801 && suffix!=802
            && id%10000!=2056 && !Removed.Contains(id) && !(id>14&&id<900)
            && !(id>20000&&id<20014) && !(id>30000&&id<30014) && !(id>40000&&id<40014);
    }
    static int Value(Dictionary<string,object> row,string key)=>row.TryGetValue(key,out var v)?Convert.ToInt32(v):0;
    public List<int> Legal(int itemId,int level,int grade,bool bonus,bool allowAutoSteal)
    {
        if(level<0 || grade<0 || grade>4)throw new ArgumentException("Invalid potential pool request");
        int prefix=itemId/10000;
        bool secondary=prefix==109||prefix==135;
        bool weapon=itemId>=1210000&&itemId<1600000||prefix==170;
        bool emblem=prefix==119;
        bool accessory=prefix==111||prefix==112||prefix==101||prefix==102||prefix==103;
        bool sourceAccessory=itemId>=1010000&&itemId<1040000 || itemId>=1122000&&itemId<1153000
            || itemId>=1112000&&itemId<1113000 || itemId>=1670000&&itemId<1680000;
        bool armor=!sourceAccessory&&!weapon&&prefix!=118&&prefix!=167;
        var result=new List<int>();
        foreach(int id in catalog.Ids)
        {
            if(!SourceAllows(id)||GradeOf(id)!=grade||IsBonus(id)!=bonus)continue;
            var info=catalog.Info(id);
            if(Value(info,"reqLevel")>level)continue;
            int type=Value(info,"optionType");
            bool legal=type==0;
            if(secondary)legal|=(type==10||type==20)&&id!=42060;
            else if(weapon)legal|=type==10;
            else if(emblem)
            {
                string text=info.TryGetValue("string",out var s)?s as string:"";
                legal|=type==10 && !(text??"").Contains("Boss");
            }
            else
            {
                legal|=type==11;
                if(accessory)legal|=type==40;
                else
                {
                    legal|=type==51&&prefix==100 || type==52&&(prefix==104||prefix==105)
                        || type==53&&prefix==106 || type==55&&prefix==107;
                    if(prefix==108)
                        legal|=grade>=3?(type==54||type==20)&&id!=42060:type==54;
                    else if(armor||prefix==115||prefix==113)legal|=type==20;
                }
            }
            if(legal && prefix==108 && grade>=3 && !allowAutoSteal
                && info.TryGetValue("string",out var textValue) && ((string)textValue).Contains("Auto Steal"))legal=false;
            if(legal)result.Add(id);
        }
        return result;
    }
}
