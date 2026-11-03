using System;
using System.Collections.Generic;
using System.Globalization;

// 282.5 CubeRateTable policy. Legal option filtering belongs to the caller.
public sealed class CubeRateTable
{
    public sealed class Weighted
    {
        readonly int[] keys, weights;
        public int Total { get; }
        public int Count => keys.Length;
        public Weighted(string spec)
        {
            var k=new List<int>(); var w=new List<int>(); int total=0;
            foreach (string part in spec.Split(','))
            {
                if (string.IsNullOrWhiteSpace(part)) continue;
                string[] pair=part.Split(':');
                if (pair.Length!=2) throw new ArgumentException("Invalid option weight");
                int id=int.Parse(pair[0],CultureInfo.InvariantCulture), weight=int.Parse(pair[1],CultureInfo.InvariantCulture);
                if (weight<0) throw new ArgumentException("Negative option weight");
                if (weight==0) continue;
                total=checked(total+weight); k.Add(id);w.Add(weight);
            }
            if (total<=0) throw new ArgumentException("Empty option weights");
            keys=k.ToArray(); weights=w.ToArray(); Total=total;
        }
        public int Pick(int draw)
        {
            if (draw<0 || draw>=Total) throw new ArgumentOutOfRangeException(nameof(draw));
            for (int i=0;i<keys.Length;i++) {if(draw<weights[i]) return keys[i];draw-=weights[i];}
            throw new InvalidOperationException();
        }
        public int WeightOf(int id)
        {for(int i=0;i<keys.Length;i++)if(keys[i]==id)return weights[i];return 0;}
        internal int Overlap(HashSet<int> legal)
        {int n=0;foreach(int k in keys)if(legal.Contains(k))n++;return n;}
        internal Weighted Restrict(HashSet<int> legal)
        {
            var parts=new List<string>();
            foreach(int k in keys)if(legal.Contains(k))parts.Add(k+":"+WeightOf(k));
            return parts.Count==0?null:new Weighted(string.Join(",",parts));
        }
    }
    readonly Dictionary<string,int[]> builtins=new Dictionary<string,int[]>();
    readonly Dictionary<string,HashSet<int>> families=new Dictionary<string,HashSet<int>>();
    public bool InFamily(string name,int id)=>families.TryGetValue(name,out var set)&&set.Contains(id);
    readonly Dictionary<long,int> tier=new Dictionary<long,int>(), prime=new Dictionary<long,int>();
    readonly HashSet<int> tierCubes=new HashSet<int>(), primeCubes=new HashSet<int>();
    readonly Dictionary<long,List<Weighted>> categories=new Dictionary<long,List<Weighted>>();
    readonly Dictionary<long,Weighted> overrides=new Dictionary<long,Weighted>();
    static long Key(int cube,int grade)=>((long)cube<<8)| (uint)grade;
    static int Number(Dictionary<string,object> row,string field)=>int.Parse((string)row[field],CultureInfo.InvariantCulture);
    public CubeRateTable(string json)
    {
        var root=MiniJsonDb.Parse(json) as Dictionary<string,object>;
        if(root==null || Convert.ToInt32(root["schemaVersion"])!=1 || Convert.ToInt32(root["tierUpUnit"])!=1000)
            throw new ArgumentException("Invalid cube rate data");
        foreach(var pair in (Dictionary<string,object>)root["builtinTierUpRates"])
        {
            var values=(List<object>)pair.Value;
            if(values.Count!=3)throw new ArgumentException("Invalid tier-up array");
            builtins.Add(pair.Key,new[]{Convert.ToInt32(values[0]),Convert.ToInt32(values[1]),Convert.ToInt32(values[2])});
        }
        var tables=(Dictionary<string,object>)root["tables"];
        foreach(var pair in (Dictionary<string,object>)root["optionFamilies"])
        {
            var set=new HashSet<int>();foreach(var value in (List<object>)pair.Value)set.Add(Convert.ToInt32(value));
            families.Add(pair.Key,set);
        }
        List<object> Rows(string name)=>(List<object>)tables[name];
        bool Setting(string name)
        {foreach(Dictionary<string,object> row in Rows("cube-settings"))if((string)row["key"]==name)return bool.Parse((string)row["value"]);return false;}
        if(Setting("tier_up"))foreach(Dictionary<string,object> row in Rows("cube-grade"))
        {
            int cube=Number(row,"cube_id"),grade=Number(row,"grade");
            tierCubes.Add(cube);tier[Key(cube,grade)]=Number(row,"tier_up");
        }
        if(Setting("option_level"))foreach(Dictionary<string,object> row in Rows("cube-option-level"))
        {
            int cube=Number(row,"cube_id"),grade=Number(row,"grade"),line=Number(row,"line");
            primeCubes.Add(cube);prime[(Key(cube,grade)<<2)| (uint)line]=Number(row,"same_grade");
        }
        foreach(Dictionary<string,object> row in Rows("cube-options"))
        {
            long key=Key(Number(row,"cube_id"),Number(row,"grade"));
            if(!categories.TryGetValue(key,out var list))categories.Add(key,list=new List<Weighted>());
            list.Add(new Weighted((string)row["weights"]));
        }
        foreach(Dictionary<string,object> row in Rows("cube-option-override"))
            overrides[Key(Number(row,"cube_id"),Number(row,"grade"))]=new Weighted((string)row["weights"]);
    }
    public int BuiltinTierUp(string policy,int grade)
    {
        if(grade<1||grade>3)throw new ArgumentOutOfRangeException(nameof(grade));
        return builtins[policy][grade-1]; // per 1000; not the disabled TSV per 10000
    }
    public int TierUpOverride(int cube,int grade)
        =>grade<1||grade>3||!tierCubes.Contains(cube)?-1:tier.TryGetValue(Key(cube,grade),out int rate)?rate:0;
    public int PrimeOverride(int cube,int grade,int line)
        =>grade<1||grade>4||line<1||line>3||!primeCubes.Contains(cube)?-1:
          prime.TryGetValue((Key(cube,grade)<<2)|(uint)line,out int rate)?rate:10000;
    public Weighted OptionWeights(int cube,int grade,IEnumerable<int> legalOptions)
    {
        var legal=new HashSet<int>(legalOptions);
        if(legal.Count==0)return null;
        long key=Key(cube,grade);
        if(overrides.TryGetValue(key,out var forced))
        {var restricted=forced.Restrict(legal);if(restricted!=null)return restricted;}
        if(!categories.TryGetValue(key,out var list))return null;
        Weighted best=null;int bestOverlap=0,bestMissing=int.MaxValue;
        foreach(var candidate in list)
        {
            int overlap=candidate.Overlap(legal),missing=candidate.Count-overlap;
            if(overlap>bestOverlap || overlap==bestOverlap && missing<bestMissing)
            {best=candidate;bestOverlap=overlap;bestMissing=missing;}
        }
        return best==null||bestOverlap*2<best.Count?null:best.Restrict(legal);
    }
}
