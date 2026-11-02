using System;
using MapleDash.Js;

// Item.java:50: a detached item created by a script, not a live inventory slot.
public sealed class NativeScriptItem
{
    public readonly int Id;
    public short Position, Quantity, Flag;
    public string Owner="", GiftFrom="";
    public long Expiration=-1;
    public NativeScriptItem(int id,short position,short quantity)
    { Id=id;Position=position;Quantity=quantity; }
    static void Bind(JsHostObject host,string name,Func<JsCall,JsValue> call)
        => host.Set(name,JsFunction.FromNative(name,call));
    public JsHostObject Host()
    {
        var host=new JsHostObject(this);
        host.Missing=key=>throw new JsRuntimeException("Unmigrated item API: "+key);
        Bind(host,"getItemId",c=>new JsNumber(Id));
        Bind(host,"getPosition",c=>new JsNumber(Position));
        Bind(host,"getQuantity",c=>new JsNumber(Quantity));
        Bind(host,"getFlag",c=>new JsNumber(Flag));
        Bind(host,"getOwner",c=>new JsString(Owner));
        Bind(host,"getGiftFrom",c=>new JsString(GiftFrom));
        Bind(host,"getExpiration",c=>new JsNumber(Expiration));
        Bind(host,"getPetId",c=>new JsNumber(-1));
        Bind(host,"setPosition",c=>{Position=unchecked((short)JsConvert.ToInt32(c.Arg(0)));return JsUndefined.Value;});
        Bind(host,"setQuantity",c=>{Quantity=unchecked((short)JsConvert.ToInt32(c.Arg(0)));return JsUndefined.Value;});
        Bind(host,"setFlag",c=>{Flag=unchecked((short)JsConvert.ToInt32(c.Arg(0)));return JsUndefined.Value;});
        Bind(host,"setOwner",c=>{Owner=JsConvert.ToString(c.Arg(0));return JsUndefined.Value;});
        Bind(host,"setGiftFrom",c=>{GiftFrom=JsConvert.ToString(c.Arg(0));return JsUndefined.Value;});
        Bind(host,"setExpiration",c=>{Expiration=(long)JsConvert.ToNumber(c.Arg(0));return JsUndefined.Value;});
        Bind(host,"copy",c=>new NativeScriptItem(Id,Position,Quantity) {Flag=Flag,Owner=Owner,Expiration=Expiration}.Host());
        return host;
    }
    public static JsValue Constructor() => JsFunction.FromNative("Item",c=>
    {
        if(c.ArgCount!=3)throw new JsRuntimeException("Unmigrated Item constructor overload: "+c.ArgCount);
        return new NativeScriptItem(JsConvert.ToInt32(c.Arg(0)),unchecked((short)JsConvert.ToInt32(c.Arg(1))),
            unchecked((short)JsConvert.ToInt32(c.Arg(2)))).Host();
    });
}
