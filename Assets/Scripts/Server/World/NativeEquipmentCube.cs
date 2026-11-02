using System;
using System.Collections.Generic;
using System.Text;

// EquipmentCubeManager.java: flat bonuses replace only the previously stored cube bonuses.
public static class NativeEquipmentCube
{
    public sealed class Roll
    {
        public int itemId, cubeItemId, grade;
        public string data, description;
        public bool rankedUp, canKeepOld;
    }
    sealed class Data
    {
        public int cube, grade;
        public readonly List<KeyValuePair<int,int>> lines = new List<KeyValuePair<int,int>>();
    }
    static readonly string[] Stats={"STR","DEX","INT","LUK","WATK","MATK","HP","MP","WDEF","MDEF","ACC","AVOID","SPEED","JUMP"};
    static readonly string[] Labels={"力量","敏捷","智力","运气","攻击力","魔法力","生命值","魔法值","物理防御","魔法防御","命中","回避","移动速度","跳跃力"};
    static readonly string[] Names={"奇幻魔方","白金奇幻魔方","超级奇幻魔方","星星魔方","太阳魔方","传说魔方","红色魔方","黑色魔方"};
    static readonly string[] Grades={"特殊","稀有","罕见","传说"};
    static readonly int[] MaxGrades={3,4,3,3,4,4,4,4};
    static readonly int[,] Rates={{12,6,0},{20,10,3},{18,8,0},{20,10,0},{25,12,4},{18,8,2},{15,6,1},{20,8,2}};
    static readonly int[] Powers={1,2,4,8};
    static readonly int[] Weapon={0,1,2,3,4,5,6,7,10};
    static readonly int[] Armor={0,1,2,3,6,7,8,9,10,11,12,13};
    static readonly Random Random=new Random();
    public static bool IsCube(int id)=>id>=4007000 && id<=4007007;
    static int Index(int id)
    {
        if(!IsCube(id))throw new ArgumentException("未知魔方道具："+id);
        return id-4007000;
    }
    public static string CubeName(int id)=>IsCube(id)?Names[id-4007000]:"未知魔方";
    public static string Raw(InventoryEntry equip)=>equip?.equipmentData!=null && equip.equipmentData.TryGetValue("expandAttribute4",out var value)?Convert.ToString(value):"";
    static int Num(Dictionary<string,object> row,string key)=>row.TryGetValue(key,out var value)?Convert.ToInt32(value):0;
    static Data Read(string raw)
    {
        if(string.IsNullOrWhiteSpace(raw))return null;
        try
        {
            var root=MiniJsonDb.Parse(raw) as Dictionary<string,object>;
            if(root==null)throw new ArgumentException();
            int version=Num(root,"v"),cube=Num(root,"cube");Index(cube);
            if(version!=1 && version!=2)throw new ArgumentException();
            if(!root.TryGetValue("lines",out var value) || !(value is List<object> lines))throw new ArgumentException();
            int grade=version==1?(cube<=4007001?1:cube<=4007004?2:3):Num(root,"grade");
            if(grade<1 || grade>4 || lines.Count!=(grade==1?1:grade==2?2:3))throw new ArgumentException();
            var data=new Data {cube=cube,grade=grade};
            foreach(var line in lines)
            {
                var row=line as Dictionary<string,object>;
                int stat=row!=null && row.TryGetValue("stat",out var statName)?Array.IndexOf(Stats,Convert.ToString(statName)):-1;
                int bonus=row==null?0:Num(row,"value");
                if(stat<0 || bonus<=0 || bonus>10000)throw new ArgumentException();
                data.lines.Add(new KeyValuePair<int,int>(stat,bonus));
            }
            return data;
        }
        catch(Exception error) {throw new ArgumentException("无法解析装备魔方数据",error);}
    }
    static string Write(Data data)
    {
        var text=new StringBuilder("{\"v\":2,\"cube\":").Append(data.cube).Append(",\"grade\":").Append(data.grade).Append(",\"lines\":[");
        for(int i=0;i<data.lines.Count;i++)
        {
            if(i>0)text.Append(',');
            var line=data.lines[i];
            text.Append("{\"stat\":\"").Append(Stats[line.Key]).Append("\",\"value\":").Append(line.Value).Append('}');
        }
        return text.Append("]}").ToString();
    }
    static string Describe(Data data)
    {
        if(data==null)return "潜能强度：特殊；尚未拥有魔方词条";
        var text=new StringBuilder("潜能强度：").Append(Grades[data.grade-1]).Append('；').Append(CubeName(data.cube)).Append('：');
        for(int i=0;i<data.lines.Count;i++)
        {
            if(i>0)text.Append('；');
            var line=data.lines[i];text.Append(Labels[line.Key]).Append('+').Append(line.Value);
        }
        return text.ToString();
    }
    public static string Describe(InventoryEntry equip)
    {
        if(equip==null)return "无装备";
        try{return Describe(Read(Raw(equip)));}catch{return "魔方数据异常，请联系管理员";}
    }
    public static bool CanRoll(InventoryEntry equip)
    {
        if(equip==null)return false;
        try{Read(Raw(equip));return true;}catch{return false;}
    }
    public static bool CanUse(InventoryEntry equip,int cube)=>CanRoll(equip) && (Read(Raw(equip))?.grade ?? 1)<=MaxGrades[Index(cube)];
    public static string Summary(InventoryEntry equip,int cube)
    {
        int index=Index(cube),grade=Read(Raw(equip))?.grade ?? 1;
        if(grade>MaxGrades[index])return "最高"+Grades[MaxGrades[index]-1]+"，当前强度不可使用";
        int rate=grade>=MaxGrades[index]?0:Rates[index,grade-1];
        return "最高"+Grades[MaxGrades[index]-1]+"，"+(rate==0?"已达该魔方上限":"当前升阶率 "+rate+"%")+(index==7?"，可保留原词条":"");
    }
    public static string Summary(int cube)
    {
        int i=Index(cube);
        return "最高"+Grades[MaxGrades[i]-1]+"，升阶率 "+Rates[i,0]+"%/"+Rates[i,1]+"%/"+Rates[i,2]+"%"+(i==7?"，可保留原词条":"");
    }
    public static Roll CreateRoll(InventoryEntry equip,int cube,Func<int,int> next=null)
    {
        if(equip==null)throw new ArgumentException("装备不存在");
        next=next ?? Random.Next;
        int index=Index(cube),oldGrade=Read(Raw(equip))?.grade ?? 1;
        if(oldGrade>MaxGrades[index])throw new ArgumentException(Names[index]+"最高只能洗练"+Grades[MaxGrades[index]-1]+"强度的装备");
        int rate=oldGrade>=MaxGrades[index]?0:Rates[index,oldGrade-1];
        bool up=next(100)<rate;
        var data=new Data {cube=cube,grade=up?Math.Min(oldGrade+1,4):oldGrade};
        var pool=new List<int>();
        if(equip.itemId>=1302000 && equip.itemId<1493000)pool.AddRange(Weapon);
        else if(MaxGrades[index]==4)for(int i=0;i<Stats.Length;i++)pool.Add(i);
        else pool.AddRange(Armor);
        int power=Powers[data.grade-1],count=data.grade==1?1:data.grade==2?2:3;
        for(int i=0;i<count;i++)
        {
            int chosen=next(pool.Count),stat=pool[chosen];pool.RemoveAt(chosen);
            int low,high;
            if(stat==6 || stat==7){low=power*10;high=power*20;}
            else if(stat==8 || stat==9){low=power*4;high=power*8;}
            else if(stat==4 || stat==5){low=Math.Max(1,(power+1)/2);high=power;}
            else if(stat>=10){low=Math.Max(1,power/2);high=power;}
            else {low=power;high=power*2;}
            data.lines.Add(new KeyValuePair<int,int>(stat,low+next(high-low+1)));
        }
        return new Roll {itemId=equip.itemId,cubeItemId=cube,grade=data.grade,data=Write(data),
            description=Describe(data)+(up?"（升阶成功）":"（强度未提升）"),rankedUp=up,canKeepOld=index==7};
    }
    public static void Apply(InventoryEntry equip,Roll roll)
    {
        if(equip==null || roll==null || equip.itemId!=roll.itemId)throw new ArgumentException("装备已变化，不能应用该魔方结果");
        var next=Read(roll.data);
        if(next==null || next.cube!=roll.cubeItemId || next.grade!=roll.grade)throw new ArgumentException("魔方结果无效");
        ApplyData(equip,next);
    }
    public static void Inherit(InventoryEntry source,InventoryEntry target)
    {
        if(source!=null && target!=null)ApplyData(target,Read(Raw(source)));
    }
    static int[] Totals(Data data)
    {
        var result=new int[Stats.Length];
        if(data!=null)foreach(var line in data.lines)result[line.Key]+=line.Value;
        return result;
    }
    static void ApplyData(InventoryEntry equip,Data data)
    {
        var old=Totals(Read(Raw(equip)));var next=Totals(data);
        var values=new[]{equip.str,equip.dex,equip.int_,equip.luk,equip.watk,equip.matk,equip.hp,equip.mp,equip.wdef,equip.mdef,equip.acc,equip.avoid,equip.speed,equip.jump};
        // Validate the entire replacement before mutating any stat.
        for(int i=0;i<values.Length;i++)
        {
            values[i]=values[i]-old[i]+next[i];
            if(values[i]<0 || values[i]>short.MaxValue)throw new InvalidOperationException("魔方属性超出装备属性范围");
        }
        equip.str=values[0];equip.dex=values[1];equip.int_=values[2];equip.luk=values[3];
        equip.watk=values[4];equip.matk=values[5];equip.hp=values[6];equip.mp=values[7];
        equip.wdef=values[8];equip.mdef=values[9];equip.acc=values[10];equip.avoid=values[11];equip.speed=values[12];equip.jump=values[13];
        if(equip.equipmentData==null)equip.equipmentData=new Dictionary<string,object>();
        equip.equipmentData["expandAttribute4"]=data==null?"":Write(data);
    }
}
