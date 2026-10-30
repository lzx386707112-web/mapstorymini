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
        Check("event original warrior setup/entry/timeout and isolated instance map handles",()=>
        {
            long now=1000;int warped=0,reset=0,clocks=0;
            string path=System.IO.Path.GetFullPath("../../../MapleDashResources/Data/scripts/event/3rdJob_warrior.js");
            var events=new NativeEventScriptService(key=>key=="event/warrior"?System.IO.File.ReadAllText(path):null,()=>now,engine=>
            {
                engine.Interp.ResolveHostType=type=>
                {
                    if(type!="org.gms.util.PacketCreator")throw new Exception(type);
                    var packets=new JsHostObject(null);
                    packets.Set("getClock",JsFunction.FromNative("getClock",c=>c.Arg(0)));return packets;
                };
            });
            var maps=new HashSet<JsValue>();
            events.InstanceMap=(instance,id)=>
            {
                if(id!=108010301)throw new Exception("original map id");
                var map=new JsHostObject(instance);maps.Add(map);
                map.Set("resetPQ",JsFunction.FromNative("resetPQ",c=>{reset++;return JsUndefined.Value;}));return map;
            };
            events.Clock=(instance,seconds)=>{if(seconds==1200)clocks++;};
            JsValue Player(int id)
            {
                var player=new JsHostObject(id);
                player.Set("getId",JsFunction.FromNative("getId",c=>new JsNumber(id)));
                player.Set("changeMap",JsFunction.FromNative("changeMap",c=>{warped=JsConvert.ToInt32(c.Arg(0));return JsUndefined.Value;}));
                player.Set("sendPacket",JsFunction.FromNative("sendPacket",c=>JsUndefined.Value));return player;
            }
            var manager=events.GetManager("warrior");var one=manager.Start(Player(1));
            if(one==null || !one.Started || one.PlayerCount!=1 || events.GetInstance(1)!=one || warped!=108010300 || reset!=1 || clocks!=1)
                throw new Exception("original player entry");
            var two=manager.Start(Player(2));
            if(two==null || two==one || maps.Count!=2 || events.GetInstance(2)!=two)throw new Exception("instance alias");
            now+=1199999;events.Tick();if(one.Disposed || two.Disposed)throw new Exception("premature timeout");
            now++;events.Tick();
            if(!one.Disposed || !two.Disposed || events.GetInstance(1)!=null || events.GetInstance(2)!=null || warped!=105070001)
                throw new Exception("original timeout/unregister/dispose/exit warp");
            if(events.GetManager("absent")!=null)throw new Exception("missing script invented");
            events.Clear();
        });
        Check("event lifecycle ordering, cancellation and strict unsupported API",()=>
        {
            long now=1000;var events=new NativeEventScriptService(key=>
                "var entries=0;function init(){}function playerEntry(e,p){e.setProperty('entryCount',e.getPlayerCount());entries++;}"+
                "function playerUnregistered(e,p){e.setProperty('unregisterCount',e.getPlayerCount());}"+
                "function dispose(e){e.setProperty('disposeCalled',1);em.setProperty('disposed',1);}"+
                "function late(e){em.setProperty('late',1);}",()=>now);
            var manager=events.GetManager("lifecycle");var instance=manager.NewInstance("one");
            var player=new JsHostObject(7);player.Set("getId",JsFunction.FromNative("getId",c=>new JsNumber(7)));
            var interp=new JsInterpreter();
            JsValue Call(JsHostObject host,string method,params JsValue[] args)=>interp.Call(host.Get(method),host,args);
            instance.Register(player);instance.Register(player);
            Equal("1",Call(instance.Host,"getProperty",new JsString("entryCount")));
            instance.Unregister(player);Equal("1",Call(instance.Host,"getProperty",new JsString("unregisterCount")));
            if(instance.PlayerCount!=0 || events.GetInstance(7)!=null)throw new Exception("unregister membership");
            Throws(()=>manager.NewInstance("one"));
            Throws(()=>Call(instance.Host,"getInstanceMap",new JsNumber(100)));
            Throws(()=>instance.Host.Get("inventedMethod"));
            Call(instance.Host,"schedule",new JsString("late"),new JsNumber(10));
            instance.Dispose();now+=10;events.Tick();
            Equal("1",Call(manager.Host,"getProperty",new JsString("disposed")));
            Equal("null",Call(manager.Host,"getProperty",new JsString("late")));
            if(!instance.Disposed)throw new Exception("dispose flag");
            Throws(()=>manager.NewInstance("one"));
            now+=events.LobbyDelayMillis;events.Tick();
            var replacement=manager.NewInstance("one");
            Equal("null",Call(replacement.Host,"getProperty",new JsString("entryCount")));
            events.Clear();
        });
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
        Check("sloppy global this dispatch matches original credits script", () => Equal("7,8,9,true", Execute(
            "var n=7;function writeStaff(){return n;}function test(){var first=this['writeStaff']();this.n=8;var second=n;n=9;return [first,second,this.n,(function(){return this;}).call(null)===this].join(',');}")));
        Check("method receivers and arrow lexical this remain distinct", () => Equal("4,4", Execute(
            "function test(){var o={n:4,f:function(){return [this.n,(()=>this.n)()].join(',');}};return o.f();}")));
        Check("Java imports preserve constructor identity and lexical shadowing", () => Equal("4,true,9", Execute(
            "importClass(java.awt.Point);importPackage(java.awt);function test(){var p=new Point(4,7);function local(){var Point=9;return Point;}return [p.getX(),Point===Java.type('java.awt.Point'),local()].join(',');}")));
        Check("Java package imports resolve registered types only", () => Equal("12", Execute(
            "importPackage(java.lang);importPackage(net.sf.odinms.client);function test(){return Integer.parseInt('12');}")));
        Check("unknown imported Java class remains an error", () => Throws(() => Execute(
            "importPackage(java.util);function test(){return new MissingClass();}")));
        Check("Calendar and date formatting preserve local Java field conventions", () => Equal("2026,9,29,5,20261029,true", Execute(
            "importClass(java.util.Calendar);importClass(java.text.SimpleDateFormat);function test(){var d=new Date(1793248496000);var c=Calendar.getInstance();c.setTime(d);return [c.get(Calendar.YEAR),c.get(Calendar.MONTH),c.get(Calendar.DATE),c.get(Calendar.DAY_OF_WEEK),new SimpleDateFormat('yyyyMMdd').format(c.getTime()),c.getTimeInMillis()==d.getTime()].join(',');}")));
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
        Check("channel variables and transient quest custom data preserve ownership/lifetime", () => {
            string path=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"mapledash-channel-"+Guid.NewGuid()+".json");
            try {
                var db=DbStore.Create(path);
                int one=(int)CharactersDao.Insert(db,new CharRecord{accountid=1,name="One"});
                int two=(int)CharactersDao.Insert(db,new CharRecord{accountid=1,name="Two"});
                var server=new NativeScriptService(db);
                JsValue Value(ScriptOperation operation,int owner,int channel=1,string text=null) => server.Handle(new ScriptRequest {
                    operation=operation, characterId=owner, channel=channel, variableKey=9000049, variableValue=3, questId=150001, customData=text }).value;
                Equal("0",Value(ScriptOperation.GetStoredVar,one));
                Value(ScriptOperation.SetStoredVar,one);
                Equal("3",Value(ScriptOperation.GetStoredVar,two));
                Equal("0",Value(ScriptOperation.GetStoredVar,one,2));
                Equal("null",Value(ScriptOperation.GetQuestCustomData,one));
                Value(ScriptOperation.SetQuestCustomData,one,text:"42");
                Equal("42",Value(ScriptOperation.GetQuestCustomData,one));
                Equal("null",Value(ScriptOperation.GetQuestCustomData,two));
                server.EndSession(one);
                Equal("null",Value(ScriptOperation.GetQuestCustomData,one));
                Equal("3",Value(ScriptOperation.GetStoredVar,two));
                server=new NativeScriptService(db);
                Equal("0",Value(ScriptOperation.GetStoredVar,one));
            } finally { if(System.IO.File.Exists(path))System.IO.File.Delete(path); }
        });
        Check("authorized 2010005 override denies travel without money or ticket", () => {
            string source=System.IO.File.ReadAllText("../../../MapleDashResources/Data/scripts/npc/2010005.js");
            foreach(int selection in new[]{0,1}) {
                int disposed=0,warps=0,charged=0;var cm=new JsHostObject(null);
                cm.Set("sendYesNo",JsFunction.FromNative("sendYesNo",c=>JsUndefined.Value));
                cm.Set("sendOk",JsFunction.FromNative("sendOk",c=>JsUndefined.Value));
                cm.Set("dispose",JsFunction.FromNative("dispose",c=>{disposed++;return JsUndefined.Value;}));
                cm.Set("getMeso",JsFunction.FromNative("getMeso",c=>new JsNumber(0)));
                cm.Set("haveItem",JsFunction.FromNative("haveItem",c=>JsBoolean.False));
                cm.Set("gainMeso",JsFunction.FromNative("gainMeso",c=>{charged++;return JsUndefined.Value;}));
                cm.Set("warp",JsFunction.FromNative("warp",c=>{warps++;return JsUndefined.Value;}));
                var engine=new JsEngine(source,"2010005");engine.Run(scope=>scope.Declare("cm",cm));
                engine.Call("action",new JsNumber(1),new JsNumber(4),new JsNumber(selection));
                engine.Call("action",new JsNumber(1),new JsNumber(1),new JsNumber(-1));
                if(disposed!=1 || warps!=0 || charged!=0)throw new Exception("failed travel must not warp/charge");
            }
        });
        Check("event travel rate uses world settings, Java float arithmetic and truncation", () => {
            string path=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"mapledash-travel-"+Guid.NewGuid()+".json");
            try {
                var db=DbStore.Create(path); var server=new NativeScriptService(db);
                var world=db.Table("world_prop").Find(row=>DbStore.Int(row,"id")==0);
                world["travel_rate"]=0.125d;
                var events=new NativeEventScriptService(key=>"function init(){em.setProperty('travel',em.getTransportationTime(15));}",()=>0);
                events.TransportationTime=time=>server.TransportationTime(0,time);
                var manager=events.GetManager("travel");var interp=new JsInterpreter();
                Equal("1",interp.Call(manager.Host.Get("getProperty"),manager.Host,new JsValue[]{new JsString("travel")}));
                if(server.TransportationTime(0,-15)!=-1)throw new Exception("negative truncation");
                world["travel_rate"]=2d;
                if(server.TransportationTime(0,int.MaxValue)!=int.MaxValue)throw new Exception("overflow clamp");
                Throws(()=>server.TransportationTime(99,1));
                events.Clear();
            } finally { if(System.IO.File.Exists(path))System.IO.File.Delete(path); }
        });
        Check("Gachapon reward queries include public pools and exclude expired pools", () => {
            string path=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"mapledash-gacha-"+Guid.NewGuid()+".json");
            try {
                var db=DbStore.Create(path);
                int owner=(int)CharactersDao.Insert(db,new CharRecord{accountid=1,name="Gacha"});
                db.Table("gachapon_reward_pool").rows.Clear(); db.Table("gachapon_reward").rows.Clear();
                db.Table("gachapon_reward_pool").Insert(new DbRow{["id"]=1L,["gachapon_id"]=99L,["is_public"]=1L,["prob"]=10000L});
                db.Table("gachapon_reward_pool").Insert(new DbRow{["id"]=2L,["gachapon_id"]=9100100L,["end_time"]="2000-01-01"});
                db.Table("gachapon_reward_pool").Insert(new DbRow{["id"]=3L,["gachapon_id"]=9100100L,["end_time"]="2999-01-01"});
                foreach(int id in new[]{1,2,3})db.Table("gachapon_reward").Insert(new DbRow{["pool_id"]=(long)id,["item_id"]=(long)(2000000+id),["quantity"]=1L});
                var server=new NativeScriptService(db);
                var opened=server.Handle(new ScriptRequest{operation=ScriptOperation.Open,source="var G=Java.type('org.gms.server.gachapon.Gachapon');var M=Java.type('org.gms.manager.ServerManager');function test(){return G.GachaponType.getLootNames()[0]+','+G.GachaponType.getLootIds()[0]+','+M.getApplicationContext().getBean('gachaponService').getRewardsByNpcId(9100100).map(x=>x.getItemId()).join(',');}"});
                Equal("射手村,9100100,2000001,2000003",server.Handle(new ScriptRequest{operation=ScriptOperation.Call,sessionId=opened.sessionId,function="test"}).value);
                Equal("2000001,1",server.Handle(new ScriptRequest{operation=ScriptOperation.GachaDraw,characterId=owner,gachaponId=123}).value is JsArray draw ? new JsString(string.Join(",",draw.Items.ConvertAll(JsConvert.ToString))) : JsUndefined.Value);
            } finally { if(System.IO.File.Exists(path))System.IO.File.Delete(path); }
        });
        Check("ExtendType enum values and identity", () => {
            var server=new NativeScriptService();
            var opened=server.Handle(new ScriptRequest{operation=ScriptOperation.Open,source="var T=Java.type('org.gms.constants.string.ExtendType');function test(){return [T.CHARACTER_EXTEND_DAILY.getType(),T.isAccount('12'),T.isCharacter('22'),T.getExtendType('22')===T.CHARACTER_EXTEND_DAILY,T.getExtendType('missing')===T.UNSUPPORTED].join(',');}"});
            Equal("22,true,true,true,true",server.Handle(new ScriptRequest{operation=ScriptOperation.Call,sessionId=opened.sessionId,function="test"}).value);
        });
        Check("ExtendUtil exact keys, updates and durable values", () => {
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "mapledash-extend-"+Guid.NewGuid()+".json");
            try {
                var db=DbStore.Create(path);
                string script="var E=Java.type('org.gms.util.ExtendUtil');function write(){E.saveOrUpdateExtendValue('1','21','reward','old');E.saveOrUpdateExtendValue('1','21','reward','new');return E.getExtendValue('1','21','reward').getExtendValue();}function read(){return E.getExtendValue('1','21','reward').getExtendValue();}function other(){return E.getExtendValue('2','21','reward');}";
                var server=new NativeScriptService(db);
                var opened=server.Handle(new ScriptRequest{operation=ScriptOperation.Open,source=script});
                Equal("new",server.Handle(new ScriptRequest{operation=ScriptOperation.Call,sessionId=opened.sessionId,function="write"}).value);
                Equal("null",server.Handle(new ScriptRequest{operation=ScriptOperation.Call,sessionId=opened.sessionId,function="other"}).value);
                if(db.Table("extend_value").rows.Count!=1)throw new Exception("duplicate update row");
                server=new NativeScriptService(DbStore.Open(path));
                opened=server.Handle(new ScriptRequest{operation=ScriptOperation.Open,source=script});
                Equal("new",server.Handle(new ScriptRequest{operation=ScriptOperation.Call,sessionId=opened.sessionId,function="read"}).value);
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
