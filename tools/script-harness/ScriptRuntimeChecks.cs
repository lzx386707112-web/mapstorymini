using System;
using System.Collections.Generic;
using MapleDash.Js;

internal static class ScriptRuntimeChecks
{
    static int passed, failed;
    static void Check(string name, Action body)
    {
        try { body(); passed++; }
        catch (Exception error) { failed++; Console.WriteLine("FAIL " + name + " :: " + error.Message); }
    }
    static void Equal(string expected, JsValue actual)
    {
        string value = JsConvert.ToString(actual);
        if (value != expected) throw new Exception("expected=" + expected + " actual=" + value);
    }
    static JsValue Execute(string source)
    {
        var engine = new JsEngine(source, "regression");
        engine.Run();
        return engine.Call("test");
    }
    static void Throws(Action action)
    {
        try { action(); } catch (JsRuntimeException) { return; }
        throw new Exception("Expected explicit script error");
    }
    public static int Run()
    {
        Check("arrow block/expression, reduce and lexical capture", () => Equal("14", Execute(
            "function test(){var bonus=2; return [1,2,3].map(x=>{return x+bonus;}).reduce((a,x)=>a+x,2);}")));
        Check("nested destructuring and Object.entries", () => Equal("a=5,b=7", Execute(
            "function test(){return Object.entries({a:{qty:5},b:{qty:7}}).map(([id,{qty}])=>id+'='+qty).join(',');}")));
        Check("for-of object pattern, break and continue", () => Equal("3", Execute(
            "function test(){var n=0;for(const {id,qty} of [{id:1,qty:3},{id:2,qty:7}]){if(id==2)continue;n+=qty;}return n;}")));
        Check("spread calls/arrays/objects and number separator", () => Equal("1000001", Execute(
            "function test(){var a={x:2};var b={...a,x:3};return Math.max(...[1_000_000,...[b.x]])+1;}")));
        Check("nullish keeps zero and false; optional member", () => Equal("0,false,9", Execute(
            "function test(){return [0??8,false??8,null?.x??9].join(',');}")));
        Check("default function parameters", () => Equal("8", Execute(
            "function f(a=3,{b}={b:5}){return a+b;}function test(){return f();}")));
        Check("labeled break does not break inner loop only", () => Equal("1", Execute(
            "function test(){var n=0;outer:for(var i=0;i<3;i++){for(var j=0;j<3;j++){n++;break outer;}}return n;}")));
        Check("labeled continue reaches correct loop", () => Equal("3", Execute(
            "function test(){var n=0;outer:for(var i=0;i<3;i++){for(var j=0;j<3;j++){n++;continue outer;}}return n;}")));
        Check("regex class, escaped slash, split and division", () => Equal("a|b|c|d,3,true", Execute(
            @"function test(){var r=/[ ,&\/]+/g;return ['a b,c/d'.split(r).join('|'),6/2,/[\u4e00-\u9fa5]/.test('中')].join(',');}")));
        Check("regex test state", () => Equal("true,true,false", Execute(
            "function test(){var r=/a/g;return [r.test('aa'),r.test('aa'),r.test('aa')].join(',');}")));
        Check("regex match and replacement", () => Equal("2,b_b", Execute(
            "function test(){return ['a-a'.match(/a/g).length,'a_a'.replace(/a/g,'b')].join(',');}")));
        Check("Java map keys preserve primitive identity", () => Equal("number,string,2", Execute(
            "var HashMap=Java.type('java.util.HashMap');function test(){var m=new HashMap();m.put(1,'number');m.put('1','string');return [m.get(1),m.get('1'),m.size()].join(',');}")));
        Check("Java point via package and type", () => Equal("4,7", Execute(
            "function test(){var Point=Java.type('java.awt.Point');var p=new Point(4,7);return [p.getX(),new java.awt.Point(4,7).getY()].join(',');}")));
        Check("Date timestamps and local setHours", () => Equal("true,true", Execute(
            "function test(){var d=new Date(0);var n=d.setHours(0,0,5,0);return [d.getTime()==n,new Date(n)<=new Date(n+1)].join(',');}")));
        Check("direct eval reuses current scope without runtime code generation", () => Equal("8", Execute(
            "function test(){var n=3;eval('n+=2');function reward(){return n+3;}return eval('reward')();}")));
        Check("unknown Java class must not silently succeed", () => Throws(() => Execute(
            "function test(){return Java.type('missing.ServerClass');}")));
        Check("null access remains a real error", () => Throws(() => Execute("function test(){return null.getId();}")));
        Check("missing source brace remains a syntax error", () => Throws(() => new JsEngine("function start(){", "broken")));
        Check("step budget resets between user replies", () => {
            var engine = new JsEngine("function test(){var n=0;for(var i=0;i<5;i++)n++;return n;}", "budget");
            engine.Interp.MaxSteps=6; engine.Run(); for(int n=0;n<20;n++) Equal("5", engine.Call("test"));
        });
        Check("BeiDou nextLevel select/text/yes-no/cancel routing", () => {
            var route = new NativeScriptRoute();
            route.Set(NativeScriptRouteType.Select, selectionPrefix: "reward");
            if(!route.Resolve(1,7,"",out var fn,out var args)||fn!="levelreward7"||args.Length!=0)throw new Exception("selection route");
            route.Set(NativeScriptRouteType.Text, after:"name");
            if(!route.Resolve(1,0,"原文",out fn,out args)||fn!="levelname")throw new Exception("text route");
            Equal("原文",args[0]);
            if(route.Resolve(0,0,"",out fn,out args))throw new Exception("text cancel");
            route.Set(NativeScriptRouteType.YesNo,"no","yes");
            route.Resolve(0,0,"",out fn,out args);if(fn!="levelno")throw new Exception("no route");
            route.Resolve(1,0,"",out fn,out args);if(fn!="levelyes")throw new Exception("yes route");
            if(route.Resolve(-1,0,"",out fn,out args))throw new Exception("close route");
        });
        Check("map entry scope cache, first-user gate and original start(ms) argument", () => {
            var service=new NativeMapScriptService();
            int loads=0,calls=0,last=0;
            Func<string,string> load=key=>{loads++;return "var n=0;function start(ms){ms.hit(++n);}";};
            var host=new JsHostObject(null);
            host.Set("hit",JsFunction.FromNative("hit",c=>{calls++;last=JsConvert.ToInt32(c.Arg(0));return JsUndefined.Value;}));
            if(!service.Run(1,100,"onFirstUserEnter/a",true,load,host) || calls!=1 || last!=1)throw new Exception("first entry");
            if(service.Run(1,100,"onFirstUserEnter/a",true,load,host) || calls!=1)throw new Exception("duplicate first entry");
            service.Run(1,101,"onFirstUserEnter/a",true,load,host);
            service.Run(2,100,"onFirstUserEnter/a",true,load,host);
            if(loads!=1 || calls!=3 || last!=3)throw new Exception("map/character key or cached globals");
            service.Run(1,100,"onUserEnter/a",false,load,host);
            service.Run(1,100,"onUserEnter/a",false,load,host);
            if(loads!=2 || calls!=5 || last!=2)throw new Exception("ordinary entry/cache separation");
            service.EndCharacter(1);
            service.Run(1,100,"onFirstUserEnter/a",true,load,host);
            if(loads!=2 || last!=4)throw new Exception("logout gate reset must retain script cache");
        });
        Check("missing map first-user source is marked before loading", () => {
            var service=new NativeMapScriptService();int loads=0;
            Func<string,string> missing=key=>{loads++;return null;};
            service.Run(1,100,"onFirstUserEnter/missing",true,missing,JsNull.Value);
            service.Run(1,100,"onFirstUserEnter/missing",true,missing,JsNull.Value);
            if(loads!=1)throw new Exception("missing source retried first entry");
        });
        Check("map script exception preserves cached scope and entry marker", () => {
            var service=new NativeMapScriptService();int loads=0;
            Func<string,string> source=key=>{loads++;return "var n=0;function start(ms){if(++n==1)throw 'first';ms.hit(n);}";};
            var host=new JsHostObject(null);int value=0;
            host.Set("hit",JsFunction.FromNative("hit",c=>{value=JsConvert.ToInt32(c.Arg(0));return JsUndefined.Value;}));
            Throws(()=>service.Run(1,100,"onFirstUserEnter/broken",true,source,host));
            if(service.Run(1,100,"onFirstUserEnter/broken",true,source,host))throw new Exception("failed first reentered");
            service.Run(2,100,"onFirstUserEnter/broken",true,source,host);
            if(loads!=1 || value!=2)throw new Exception("failed invocation discarded global state");
        });
        Check("server sessions isolated, owned and released", () => {
            var server = new NativeScriptService();
            string source="var n=0;function start(){return ++n;}function action(){return ++n;}";
            var one=server.Handle(new ScriptRequest{operation=ScriptOperation.Open,characterId=1,source=source,name="one"});
            var two=server.Handle(new ScriptRequest{operation=ScriptOperation.Open,characterId=1,source=source,name="two"});
            Equal("1",server.Handle(new ScriptRequest{operation=ScriptOperation.Call,characterId=1,sessionId=one.sessionId,function="start"}).value);
            Equal("2",server.Handle(new ScriptRequest{operation=ScriptOperation.Call,characterId=1,sessionId=one.sessionId,function="action"}).value);
            Equal("1",server.Handle(new ScriptRequest{operation=ScriptOperation.Call,characterId=1,sessionId=two.sessionId,function="start"}).value);
            Throws(()=>server.Handle(new ScriptRequest{operation=ScriptOperation.Call,characterId=2,sessionId=one.sessionId,function="action"}));
            server.EndSession(1); if(server.ActiveCount!=0)throw new Exception("session leak");
        });
        Check("server script values isolate character/account and roll daily records", () => {
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "mapledash-script-values-"+Guid.NewGuid()+".json");
            try {
                var db = DbStore.Create(path);
                int first=(int)CharactersDao.Insert(db,new CharRecord { accountid=12,name="ScriptOne" });
                int second=(int)CharactersDao.Insert(db,new CharRecord { accountid=12,name="ScriptTwo" });
                var now=new DateTimeOffset(2026,10,26,23,59,0,TimeSpan.FromHours(8));
                var values=new NativeScriptValues(db,()=>now);
                values.Handle(new ScriptValueRequest{characterId=first,name="reward",value="永久",write=true});
                values.Handle(new ScriptValueRequest{characterId=first,name="reward",value="今日",write=true,account=true,period=1});
                if(values.Handle(new ScriptValueRequest{characterId=second,name="reward"}).value!=null)throw new Exception("character value leak");
                if(values.Handle(new ScriptValueRequest{characterId=second,name="reward",account=true,period=1}).value!="今日")throw new Exception("account value not shared");
                now=now.AddMinutes(2);
                if(values.Handle(new ScriptValueRequest{characterId=first,name="reward",account=true,period=1}).value!=null)throw new Exception("daily reset failed");
                if(values.Handle(new ScriptValueRequest{characterId=first,name="reward"}).value!="永久")throw new Exception("permanent value reset");
                db.Save();
                var reopened=new NativeScriptValues(DbStore.Open(path),()=>now);
                if(reopened.Handle(new ScriptValueRequest{characterId=first,name="reward"}).value!="永久")throw new Exception("value not persisted");
            } finally { if(System.IO.File.Exists(path))System.IO.File.Delete(path); }
        });
        Check("server top-level failure leaves no session", () => {
            var server=new NativeScriptService();
            Throws(()=>server.Handle(new ScriptRequest{operation=ScriptOperation.Open,source="throw 'bad';"}));
            if(server.ActiveCount!=0)throw new Exception("failed open leaked");
        });
        Console.WriteLine("runtime checks passed="+passed+" failed="+failed);
        return failed==0?0:1;
    }
}
