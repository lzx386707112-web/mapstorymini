using System;
using MapleDash.Js;

// Stat.java and its generated Java enum accessors. Masks are packet stat identifiers.
public static class NativeScriptStat
{
    public static JsValue Type()
    {
        string[] names={"SKIN","FACE","HAIR","LEVEL","JOB","STR","DEX","INT","LUK","HP","MAXHP","MP","MAXMP","AVAILABLEAP","AVAILABLESP","EXP","FAME","MESO","PET","GACHAEXP"};
        int[] masks={1,2,4,16,32,64,128,256,512,1024,2048,4096,8192,16384,32768,65536,131072,262144,1572872,2097152};
        var type=new JsHostObject(null);
        type.Missing=key=>throw new JsRuntimeException("Unmigrated Stat API: "+key);
        var values=new JsValue[names.Length];
        for(int i=0;i<names.Length;i++)
        {
            int ordinal=i,value=masks[i];string name=names[i];
            var stat=new JsHostObject(name);
            stat.Missing=key=>throw new JsRuntimeException("Unmigrated Stat."+name+" API: "+key);
            stat.Set("getValue",JsFunction.FromNative("getValue",_=>new JsNumber(value)));
            stat.Set("name",JsFunction.FromNative("name",_=>new JsString(name)));
            stat.Set("toString",stat.Get("name"));
            stat.Set("ordinal",JsFunction.FromNative("ordinal",_=>new JsNumber(ordinal)));
            values[i]=stat;type.Set(name,stat);
        }
        JsValue Find(int index)=>index<0?JsNull.Value:values[index];
        type.Set("getByValue",JsFunction.FromNative("getByValue",c=>Find(Array.IndexOf(masks,JsConvert.ToInt32(c.Arg(0))))));
        type.Set("getByString",JsFunction.FromNative("getByString",c=>Find(Array.IndexOf(names,JsConvert.ToString(c.Arg(0))))));
        type.Set("getBy5ByteEncoding",JsFunction.FromNative("getBy5ByteEncoding",c=>
        {
            int value=JsConvert.ToInt32(c.Arg(0));
            return value==64 || value==128 || value==256 || value==512?Find(Array.IndexOf(masks,value)):JsNull.Value;
        }));
        type.Set("values",JsFunction.FromNative("values",_=>new JsArray(values)));
        type.Set("valueOf",JsFunction.FromNative("valueOf",c=>
        {
            int index=Array.IndexOf(names,JsConvert.ToString(c.Arg(0)));
            if(index<0)throw new JsRuntimeException("No enum constant Stat."+JsConvert.ToString(c.Arg(0)));
            return values[index];
        }));
        return type;
    }
}
