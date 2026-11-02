using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using MapleDash.Js;

// JDBC read adapter for the original NPC SELECT statements. Unknown SQL is an error,
// never an empty result. All rows come from the live DbStore, not catalogue fixtures.
public static class NativeScriptDatabase
{
    static JsValue Fn(string name,Func<JsCall,JsValue> call)=>JsFunction.FromNative(name,call);
    static JsHostObject Host(object value,string kind)
    {
        var host=new JsHostObject(value);host.Missing=k=>throw new JsRuntimeException("Unmigrated "+kind+" API: "+k);return host;
    }
    static void Open(bool closed) {if(closed)throw new JsRuntimeException("JDBC object is closed");}
    public static JsValue Type(DbStore db)
    {
        var type=Host(null,"DatabaseConnection");
        type.Set("getConnection",Fn("getConnection",_=>Connection(db)));return type;
    }
    static JsValue Connection(DbStore db)
    {
        bool closed=false;var connection=Host(db,"Connection");
        connection.Set("close",Fn("close",_=>{closed=true;return JsUndefined.Value;}));
        connection.Set("isClosed",Fn("isClosed",_=>JsBoolean.Of(closed)));
        connection.Set("prepareStatement",Fn("prepareStatement",c=>
        {Open(closed);return Statement(db,JsConvert.ToString(c.Arg(0)),()=>closed);}));
        return connection;
    }
    static JsValue Statement(DbStore db,string sql,Func<bool> connectionClosed)
    {
        bool closed=false;var parameters=new Dictionary<int,object>();var statement=Host(sql,"PreparedStatement");
        int parameterCount=Regex.Matches(sql,@"\?").Count;
        void Check()=>Open(closed || connectionClosed());
        foreach(string method in new[]{"setInt","setLong","setDouble","setString"})
        {
            string setter=method;
            statement.Set(setter,Fn(setter,c=>
            {
                Check();int index=JsConvert.ToInt32(c.Arg(0));
                if(index<1 || index>parameterCount)throw new JsRuntimeException("Invalid JDBC parameter index: "+index);
                parameters[index]=setter=="setString"?(object)JsConvert.ToString(c.Arg(1)):JsConvert.ToNumber(c.Arg(1));
                return JsUndefined.Value;
            }));
        }
        statement.Set("clearParameters",Fn("clearParameters",_=>{Check();parameters.Clear();return JsUndefined.Value;}));
        statement.Set("close",Fn("close",_=>{closed=true;return JsUndefined.Value;}));
        statement.Set("isClosed",Fn("isClosed",_=>JsBoolean.Of(closed)));
        statement.Set("executeQuery",Fn("executeQuery",_=>
        {
            Check();return Result(Query(db,sql,parameters),()=>closed || connectionClosed());
        }));
        return statement;
    }
    sealed class Rows
    {
        public readonly string[] columns;
        public readonly List<Dictionary<string,object>> values=new List<Dictionary<string,object>>();
        public Rows(params string[] columns){this.columns=columns;}
        public void Add(params object[] fields)
        {
            var row=new Dictionary<string,object>(StringComparer.OrdinalIgnoreCase);
            for(int i=0;i<columns.Length;i++)row[columns[i]]=fields[i];values.Add(row);
        }
    }
    static JsValue Result(Rows rows,Func<bool> parentClosed)
    {
        int index=-1;bool closed=false,wasNull=false;var result=Host(rows,"ResultSet");
        void Check()=>Open(closed || parentClosed());
        object Read(JsCall c)
        {
            Check();if(index<0 || index>=rows.values.Count)throw new JsRuntimeException("JDBC cursor is not on a row");
            string key;
            if(c.Arg(0) is JsNumber)
            {
                int ordinal=JsConvert.ToInt32(c.Arg(0));
                if(ordinal<1 || ordinal>rows.columns.Length)throw new JsRuntimeException("Invalid JDBC column index");
                key=rows.columns[ordinal-1];
            }
            else key=JsConvert.ToString(c.Arg(0));
            if(!rows.values[index].TryGetValue(key,out var value))throw new JsRuntimeException("Unknown JDBC column: "+key);
            wasNull=value==null;return value;
        }
        result.Set("next",Fn("next",_=>{Check();index++;return JsBoolean.Of(index<rows.values.Count);}));
        result.Set("getString",Fn("getString",c=>{var v=Read(c);return v==null?(JsValue)JsNull.Value:new JsString(Convert.ToString(v,CultureInfo.InvariantCulture));}));
        result.Set("getInt",Fn("getInt",c=>new JsNumber(checked((int)Integral(Read(c))))));
        result.Set("getLong",Fn("getLong",c=>new JsNumber(Integral(Read(c)))));
        result.Set("getDouble",Fn("getDouble",c=>new JsNumber(Convert.ToDouble(Read(c) ?? 0,CultureInfo.InvariantCulture))));
        result.Set("wasNull",Fn("wasNull",_=>{Check();return JsBoolean.Of(wasNull);}));
        result.Set("close",Fn("close",_=>{closed=true;return JsUndefined.Value;}));
        result.Set("isClosed",Fn("isClosed",_=>JsBoolean.Of(closed)));
        return result;
    }
    static object Parameter(Dictionary<int,object> parameters,int index)
        =>parameters.TryGetValue(index,out var value)?value:throw new JsRuntimeException("Unbound JDBC parameter: "+index);
    static long Number(Dictionary<int,object> parameters,int index)=>Convert.ToInt64(Parameter(parameters,index),CultureInfo.InvariantCulture);
    static long Integral(object value)=>decimal.ToInt64(decimal.Truncate(Convert.ToDecimal(value ?? 0,CultureInfo.InvariantCulture)));
    static string Normalize(string sql)=>Regex.Replace(sql.Trim().TrimEnd(';').ToLowerInvariant(),@"\s+"," ");
    static Dictionary<long,List<DbRow>> EquipmentIndex(DbStore db)
    {
        var result=new Dictionary<long,List<DbRow>>();
        foreach(var equip in db.Table("inventoryequipment").rows)
        {
            long id=DbStore.Long(equip,"inventoryitemid");
            if(!result.TryGetValue(id,out var group))result[id]=group=new List<DbRow>();
            group.Add(equip);
        }
        return result;
    }
    static Rows Query(DbStore db,string sql,Dictionary<int,object> parameters)
    {
        string query=Normalize(sql);
        if(query.Contains(" from drop_data "))DropStore.EnsureDatabase(db);
        lock(db)
        {
            if(query=="select dropperid, chance, questid from drop_data where itemid = ? order by chance desc limit 30")
            {
                long item=Number(parameters,1);var data=db.Table("drop_data").Select(r=>DbStore.Long(r,"itemid")==item);
                data.Sort((a,b)=>DbStore.Long(b,"chance").CompareTo(DbStore.Long(a,"chance")));
                var rows=new Rows("dropperid","chance","questid");
                foreach(var row in data){if(rows.values.Count==30)break;rows.Add(DbStore.Long(row,"dropperid"),DbStore.Long(row,"chance"),DbStore.Long(row,"questid"));}
                return rows;
            }
            var category=Regex.Match(query,@"^select itemid from drop_data where (\(itemid between \? and \?\)(?: or \(itemid between \? and \?\))*) group by itemid limit 200$");
            if(category.Success)
            {
                int pairs=Regex.Matches(category.Groups[1].Value,"between").Count;
                var ranges=new List<(long low,long high)>();
                for(int i=0;i<pairs;i++)ranges.Add((Number(parameters,i*2+1),Number(parameters,i*2+2)));
                var rows=new Rows("itemid");var seen=new HashSet<long>();
                foreach(var row in db.Table("drop_data").rows)
                {
                    long item=DbStore.Long(row,"itemid");bool match=false;
                    foreach(var range in ranges)if(item>=range.low && item<=range.high){match=true;break;}
                    if(match && seen.Add(item))rows.Add(item);
                    if(rows.values.Count==200)break;
                }
                return rows;
            }
            if(query=="select distinct itemid, dropperid from drop_data where itemid between 4000000 and 4009999 and questid = 0 and chance > 0 order by dropperid, itemid")
            {
                var data=db.Table("drop_data").Select(r=>DbStore.Long(r,"itemid")>=4000000 && DbStore.Long(r,"itemid")<=4009999 && DbStore.Long(r,"questid")==0 && DbStore.Long(r,"chance")>0);
                data.Sort((a,b)=>{int comparison=DbStore.Long(a,"dropperid").CompareTo(DbStore.Long(b,"dropperid"));return comparison!=0?comparison:DbStore.Long(a,"itemid").CompareTo(DbStore.Long(b,"itemid"));});
                var rows=new Rows("itemid","dropperid");var seen=new HashSet<(long,long)>();
                foreach(var row in data){var pair=(DbStore.Long(row,"itemid"),DbStore.Long(row,"dropperid"));if(seen.Add(pair))rows.Add(pair.Item1,pair.Item2);}
                return rows;
            }
            const string equipment="select chr.name, it.itemid, it.position, men.str, men.dex, men.int, men.luk, men.watk, men.matk, men.mdef, men.wdef, men.upgradeslots, men.level from inventoryitems it join inventoryequipment men on it.inventoryitemid = men.inventoryitemid join characters chr on it.characterid = chr.id where it.position < 0 and chr.id = ? order by it.position desc";
            if(query==equipment)
            {
                int cid=(int)Number(parameters,1);var character=CharactersDao.FindById(db,cid);
                var rows=new Rows("name","itemid","position","str","dex","int","luk","watk","matk","mdef","wdef","upgradeslots","level");
                if(character==null)return rows;
                var items=db.Table("inventoryitems").Select(r=>DbStore.Int(r,"characterid")==cid && DbStore.Int(r,"position")<0);
                items.Sort((a,b)=>DbStore.Int(b,"position").CompareTo(DbStore.Int(a,"position")));
                var equipmentIndex=EquipmentIndex(db);
                foreach(var item in items)
                {
                    if(!equipmentIndex.TryGetValue(DbStore.Long(item,"inventoryitemid"),out var equips))continue;
                    foreach(var equip in equips)
                    {
                        if(DbStore.Long(item,"inventoryitemid")!=DbStore.Long(equip,"inventoryitemid"))continue;
                        rows.Add(character.name,DbStore.Long(item,"itemid"),DbStore.Long(item,"position"),
                            DbStore.Long(equip,"str"),DbStore.Long(equip,"dex"),DbStore.Long(equip,"int"),DbStore.Long(equip,"luk"),
                            DbStore.Long(equip,"watk"),DbStore.Long(equip,"matk"),DbStore.Long(equip,"mdef"),DbStore.Long(equip,"wdef"),DbStore.Long(equip,"upgradeslots"),DbStore.Long(equip,"level"));
                    }
                }
                return rows;
            }
            var rank=Regex.Match(query,@"^select chr.id, chr.name, chr.job, sum\( (.+) \) as totalcombatpower from inventoryitems it, inventoryequipment men, characters chr where it.position < 0 and it.inventoryitemid = men.inventoryitemid and chr.id = it.characterid and chr.gm <= 0 group by chr.id order by totalcombatpower desc limit 100$");
            if(rank.Success)
            {
                var weights=new List<(string stat,double weight)>();
                foreach(string term in rank.Groups[1].Value.Split('+'))
                {
                    var match=Regex.Match(term.Trim(),@"^men\.(str|dex|int|luk|watk|matk|wdef|mdef) \* ([0-9]+(?:\.[0-9]+)?)$");
                    if(!match.Success)throw new JsRuntimeException("Unsupported ranking SQL expression: "+term);
                    weights.Add((match.Groups[1].Value,double.Parse(match.Groups[2].Value,CultureInfo.InvariantCulture)));
                }
                var rows=new Rows("id","name","job","totalCombatPower");
                var equipmentIndex=EquipmentIndex(db);
                var totals=new Dictionary<long,double>();
                foreach(var item in db.Table("inventoryitems").rows)
                {
                    if(DbStore.Int(item,"position")>=0 || !equipmentIndex.TryGetValue(DbStore.Long(item,"inventoryitemid"),out var equips))continue;
                    long cid=DbStore.Long(item,"characterid");
                    if(!totals.ContainsKey(cid))totals[cid]=0;
                    foreach(var equip in equips)foreach(var weight in weights)totals[cid]+=DbStore.Dbl(equip,weight.stat)*weight.weight;
                }
                foreach(var character in db.Table("characters").rows)
                {
                    if(DbStore.Int(character,"gm")>0 || !totals.TryGetValue(DbStore.Long(character,"id"),out double total))continue;
                    rows.Add(DbStore.Long(character,"id"),DbStore.Str(character,"name"),DbStore.Long(character,"job"),total);
                }
                rows.values.Sort((a,b)=>Convert.ToDouble(b["totalCombatPower"]).CompareTo(Convert.ToDouble(a["totalCombatPower"])));
                if(rows.values.Count>100)rows.values.RemoveRange(100,rows.values.Count-100);return rows;
            }
            if(query=="select c.name, ev.extend_value as profit from extend_value ev join characters c on c.id = ev.extend_id where ev.extend_type = 21 and ev.extend_name = 'stock_profit' order by profit + 0 desc limit 100")
            {
                var rows=new Rows("name","profit");
                foreach(var value in db.Table("extend_value").rows)
                {
                    if(DbStore.Int(value,"extend_type")!=21 || DbStore.Str(value,"extend_name")!="stock_profit")continue;
                    if(!long.TryParse(DbStore.Str(value,"extend_id"),out long cid))continue;
                    foreach(var character in db.Table("characters").rows)
                        if(DbStore.Long(character,"id")==cid)rows.Add(DbStore.Str(character,"name"),DbStore.Str(value,"extend_value"));
                }
                double Profit(Dictionary<string,object> row)=>double.TryParse(Convert.ToString(row["profit"]),NumberStyles.Float,CultureInfo.InvariantCulture,out double number)?number:0;
                rows.values.Sort((a,b)=>Profit(b).CompareTo(Profit(a)));
                if(rows.values.Count>100)rows.values.RemoveRange(100,rows.values.Count-100);return rows;
            }
            throw new JsRuntimeException("Unmigrated script SQL: "+sql);
        }
    }
}
