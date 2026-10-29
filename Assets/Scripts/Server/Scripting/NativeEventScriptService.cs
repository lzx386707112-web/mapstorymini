using System;
using System.Collections.Generic;
using MapleDash.Js;

// EventManager/EventInstanceManager: original scripts share one engine per manager.
// Map creation and packet delivery belong to the caller, never a fake shared field.
public sealed class NativeEventScriptService
{
    readonly Func<string,string> source;
    readonly Func<long> clock;
    readonly Action<JsEngine> configure;
    readonly Dictionary<string,Manager> managers=new Dictionary<string,Manager>();
    readonly Dictionary<int,Instance> membership=new Dictionary<int,Instance>();
    readonly List<Task> tasks=new List<Task>();
    public Func<Instance,int,JsValue> InstanceMap;
    public Action<Instance,int?> Clock;
    public Action<Instance> ReleaseMaps;
    public Action<Exception> Error;
    // V1.7.0__create_game_config.sql: event_lobby_delay is 10 seconds.
    public long LobbyDelayMillis=10000;
    public NativeEventScriptService(Func<string,string> source,Func<long> clock,Action<JsEngine> configure=null)
    { this.source=source;this.clock=clock;this.configure=configure; }
    public Manager GetManager(string name)
    {
        if (managers.TryGetValue(name,out var found)) return found;
        string text=source("event/"+name);
        if (string.IsNullOrEmpty(text)) return null;
        var manager=new Manager(this,name,text);
        managers.Add(name,manager);
        try { manager.Initialize();return manager; }
        catch { manager.Cancel();managers.Remove(name);throw; }
    }
    public Instance GetInstance(int character) => membership.TryGetValue(character,out var instance) ? instance : null;
    public void Tick()
    {
        // A callback may enqueue another delay-0 task. Execute that on the next tick,
        // rather than looping forever inside one frame.
        long now=clock();var due=tasks.FindAll(t=>!t.Cancelled && t.At<=now);
        due.Sort((a,b)=>a.At.CompareTo(b.At));
        foreach(var task in due)
        {
            tasks.Remove(task);
            if(task.Cancelled || task.Manager.Cancelled || task.Owner?.Disposed==true) continue;
            task.Cancelled=true;
            try { task.Action(); } catch(Exception error) { if(Error==null)throw;Error(error); }
        }
        tasks.RemoveAll(t=>t.Cancelled || t.Manager.Cancelled || t.Owner?.Disposed==true);
    }
    public void Clear()
    {
        foreach(var manager in new List<Manager>(managers.Values)) manager.Cancel();
        managers.Clear();membership.Clear();tasks.Clear();
    }
    Task Schedule(Manager manager,Instance owner,long delay,Action action)
    {
        var task=new Task {Manager=manager,Owner=owner,At=clock()+Math.Max(0,delay),Action=action};
        tasks.Add(task);return task;
    }
    sealed class Task
    {
        public Manager Manager;public Instance Owner;public long At;public Action Action;public bool Cancelled;
        public JsValue Host()
        {
            var host=new JsHostObject(this);
            host.Set("cancel",JsFunction.FromNative("cancel",c=>{bool pending=!Cancelled;Cancelled=true;return JsBoolean.Of(pending);}));
            host.Set("isCancelled",JsFunction.FromNative("isCancelled",c=>JsBoolean.Of(Cancelled)));
            return host;
        }
    }
    static JsValue Void=JsUndefined.Value;
    static void Bind(JsHostObject host,string name,Func<JsCall,JsValue> call) => host.Set(name,JsFunction.FromNative(name,call));
    static string Text(JsCall c,int index=0) => JsConvert.ToString(c.Arg(index));
    static int Number(JsCall c,int index=0) => JsConvert.ToInt32(c.Arg(index));
    static long Long(JsCall c,int index=0) => (long)JsConvert.ToNumber(c.Arg(index));
    static JsHostObject Strict(object value,string kind)
    {
        var host=new JsHostObject(value);
        host.Missing=key=>throw new JsRuntimeException("Unmigrated "+kind+" API: "+key);
        return host;
    }
    static void Properties(JsHostObject host,Dictionary<string,string> properties)
    {
        Bind(host,"setProperty",c=>{properties[Text(c)]=Text(c,1);return Void;});
        Bind(host,"getProperty",c=>properties.TryGetValue(Text(c),out var value) ? (JsValue)new JsString(value) : JsNull.Value);
        Bind(host,"getIntProperty",c=>properties.TryGetValue(Text(c),out var value) && int.TryParse(value,out int number)
            ? (JsValue)new JsNumber(number) : throw new JsRuntimeException("Invalid event integer property: "+Text(c)));
    }
    static int PlayerId(JsValue player,JsEngine engine)
    {
        var method=engine.Interp.GetMember(player,"getId");
        return JsConvert.ToInt32(engine.Interp.Call(method,player,Array.Empty<JsValue>()));
    }
    static JsValue Players(IEnumerable<JsValue> players)
    {
        var array=new JsArray(players);
        array.Set("get",JsFunction.FromNative("get",c=>array.GetIndex(Number(c))));
        array.Set("size",JsFunction.FromNative("size",c=>new JsNumber(array.Length)));
        array.Set("isEmpty",JsFunction.FromNative("isEmpty",c=>JsBoolean.Of(array.Length==0)));
        return array;
    }
    public sealed class Manager
    {
        readonly NativeEventScriptService service;
        readonly JsEngine engine;
        readonly Dictionary<string,Instance> instances=new Dictionary<string,Instance>();
        readonly Dictionary<string,string> properties=new Dictionary<string,string>();
        public readonly string Name;
        public readonly JsHostObject Host;
        public bool Cancelled {get;private set;}
        public Manager(NativeEventScriptService service,string name,string text)
        {
            this.service=service;Name=name;engine=new JsEngine(text,"event/"+name);
            Host=Strict(this,"event manager");Properties(Host,properties);
            Bind(Host,"getName",c=>new JsString(Name));
            Bind(Host,"newInstance",c=>NewInstance(Text(c)).Host);
            Bind(Host,"getInstance",c=>instances.TryGetValue(Text(c),out var eim) ? (JsValue)eim.Host : JsNull.Value);
            Bind(Host,"getInstances",c=>{var values=new List<JsValue>();foreach(var eim in instances.Values)values.Add(eim.Host);return Players(values);});
            Bind(Host,"disposeInstance",c=>{if(instances.TryGetValue(Text(c),out var eim))Remove(eim);return Void;});
            Bind(Host,"schedule",c=>
            {
                var owner=c.ArgCount>2 ? (c.Arg(1) as JsHostObject)?.Host as Instance : null;
                return service.Schedule(this,owner,Long(c,c.ArgCount>2?2:1),()=>Call(Text(c),owner?.Host ?? (JsValue)JsNull.Value)).Host();
            });
            Bind(Host,"scheduleAtTimestamp",c=>service.Schedule(this,null,Long(c,1)-service.clock(),()=>Call(Text(c),JsNull.Value)).Host());
            // Expose only the single-character overload until party/expedition registration exists.
            Bind(Host,"startInstance",c=>
            {
                if(c.ArgCount!=1)throw new JsRuntimeException("Unmigrated event startInstance overload");
                return JsBoolean.Of(Start(c.Arg(0))!=null);
            });
        }
        internal void Initialize()
        {
            service.configure?.Invoke(engine);engine.Bind("em",Host);engine.Run();Call("init");
        }
        internal JsValue Call(string method,params JsValue[] args)
            => !Cancelled && engine.HasFunction(method) ? engine.Call(method,args) : JsNull.Value;
        public Instance NewInstance(string name)
        {
            if(Cancelled)throw new JsRuntimeException("Disposed event manager: "+Name);
            if(instances.ContainsKey(name))throw new JsRuntimeException("Event instance already exists: "+name);
            var instance=new Instance(this,service,name);instances.Add(name,instance);return instance;
        }
        public Instance Start(JsValue player)
        {
            if(Cancelled)return null;
            int max=engine.HasFunction("getMaxLobbies") ? JsConvert.ToInt32(Call("getMaxLobbies")) : 8;
            int lobby=0;
            while(lobby<Math.Min(8,max))
            {
                bool used=false;foreach(var instance in instances.Values)if(instance.Lobby==lobby)used=true;
                if(!used)break;lobby++;
            }
            if(lobby>=Math.Min(8,max))return null;
            var before=new HashSet<string>(instances.Keys);
            try
            {
                var result=Call("setup",new JsNumber(1),new JsNumber(lobby));
                var instance=(result as JsHostObject)?.Host as Instance;
                if(instance==null || instance.manager!=this)throw new JsRuntimeException("Event setup did not return its instance: "+Name);
                instance.Lobby=lobby;instance.Leader=PlayerId(player,engine);
                instance.Register(player);instance.Start();return instance;
            }
            catch
            {
                foreach(var entry in new List<Instance>(instances.Values))if(!before.Contains(entry.Name))entry.Dispose();
                throw;
            }
        }
        public void Cancel()
        {
            if(Cancelled)return;
            try { Call("cancelSchedule",JsNull.Value); }
            finally
            {
                foreach(var instance in new List<Instance>(instances.Values))instance.Dispose();
                Cancelled=true;properties.Clear();
            }
        }
        internal int Id(JsValue player) => PlayerId(player,engine);
        internal void Remove(Instance instance) => service.Schedule(this,null,service.LobbyDelayMillis,()=>
        {
            if(instances.TryGetValue(instance.Name,out var current) && current==instance)instances.Remove(instance.Name);
        });
    }
    public sealed class Instance
    {
        internal readonly Manager manager;
        readonly NativeEventScriptService service;
        readonly Dictionary<int,JsValue> players=new Dictionary<int,JsValue>();
        readonly Dictionary<string,string> properties=new Dictionary<string,string>();
        readonly Dictionary<int,JsValue> maps=new Dictionary<int,JsValue>();
        Task timer;
        long timerStarted,eventTime;
        bool disposing;
        public readonly string Name;
        public readonly JsHostObject Host;
        public bool Disposed {get;private set;}
        public bool Started {get;private set;}
        public int Leader {get;internal set;}
        public int Lobby {get;internal set;}=-1;
        public int PlayerCount=>players.Count;
        internal Instance(Manager manager,NativeEventScriptService service,string name)
        {
            this.manager=manager;this.service=service;Name=name;
            Host=Strict(this,"event instance");Properties(Host,properties);
            Bind(Host,"getName",c=>new JsString(Name));
            Bind(Host,"getEm",c=>manager.Host);
            Bind(Host,"getEventManager",c=>manager.Host);
            Bind(Host,"getPlayers",c=>Players(players.Values));
            Bind(Host,"getPlayerCount",c=>new JsNumber(players.Count));
            Bind(Host,"getPlayerById",c=>players.TryGetValue(Number(c),out var p)?p:JsNull.Value);
            Bind(Host,"getLeaderId",c=>new JsNumber(Leader));
            Bind(Host,"isLeader",c=>JsBoolean.Of(manager.Id(c.Arg(0))==Leader));
            Bind(Host,"registerPlayer",c=>{Register(c.Arg(0),c.ArgCount<2 || JsConvert.ToBoolean(c.Arg(1)));return Void;});
            Bind(Host,"unregisterPlayer",c=>{Unregister(c.Arg(0));return Void;});
            Bind(Host,"dispose",c=>{Dispose();return Void;});
            Bind(Host,"isDisposed",c=>JsBoolean.Of(Disposed));
            Bind(Host,"startEvent",c=>{Start();return Void;});
            Bind(Host,"schedule",c=>{if(!Disposed)service.Schedule(manager,this,Long(c,1),()=>Invoke(Text(c),Host));return Void;});
            Bind(Host,"startEventTimer",c=>{StartTimer(Long(c));return Void;});
            Bind(Host,"restartEventTimer",c=>{StopTimer();StartTimer(Long(c));return Void;});
            Bind(Host,"stopEventTimer",c=>{StopTimer();return Void;});
            Bind(Host,"getTimeLeft",c=>new JsNumber(eventTime-(service.clock()-timerStarted)));
            Bind(Host,"isTimerStarted",c=>JsBoolean.Of(eventTime>0 && timerStarted>0));
            Bind(Host,"getInstanceMap",c=>GetMap(Number(c)));
            Bind(Host,"getMapInstance",c=>GetMap(Number(c)));
        }
        JsValue GetMap(int id)
        {
            if(Disposed)throw new JsRuntimeException("Disposed event instance: "+Name);
            if(maps.TryGetValue(id,out var map))return map;
            if(service.InstanceMap==null)throw new JsRuntimeException("Unmigrated event instance map isolation: "+id);
            map=service.InstanceMap(this,id);
            if(map==null || map is JsNull)throw new JsRuntimeException("Missing event instance map: "+id);
            maps.Add(id,map);return map;
        }
        public JsValue Invoke(string method,params JsValue[] args) => Disposed ? JsNull.Value : manager.Call(method,args);
        public void Register(JsValue player,bool entry=true)
        {
            if(Disposed || player is JsNull || player is JsUndefined)return;
            int id=manager.Id(player);if(players.ContainsKey(id))return;
            players.Add(id,player);service.membership[id]=this;
            if(entry)Invoke("playerEntry",Host,player);
        }
        public void Unregister(JsValue player)
        {
            if(Disposed)return;
            int id=manager.Id(player);
            Invoke("playerUnregistered",Host,player);
            players.Remove(id);
            service.membership.Remove(id);
        }
        public void Start() { if(Disposed)return;Started=true;Invoke("afterSetup",Host); }
        public void ChangedMap(JsValue player,int map,bool after=false)
            => Invoke(after?"afterChangedMap":"changedMap",Host,player,new JsNumber(map));
        public void StartTimer(long duration)
        {
            if(Disposed)return;
            timerStarted=service.clock();eventTime=duration;service.Clock?.Invoke(this,(int)(duration/1000));
            timer=service.Schedule(manager,this,duration,()=>{DismissTimer();Invoke("scheduledTimeout",Host);});
        }
        void DismissTimer() { service.Clock?.Invoke(this,null);timer=null;timerStarted=0;eventTime=0; }
        public void StopTimer() { if(timer!=null)timer.Cancelled=true;DismissTimer(); }
        public void Dispose()
        {
            if(Disposed || disposing)return;
            disposing=true;
            try { Invoke("dispose",Host); }
            finally
            {
                Disposed=true;
                foreach(int id in players.Keys)if(service.GetInstance(id)==this)service.membership.Remove(id);
                players.Clear();if(timer!=null)timer.Cancelled=true;timer=null;
                foreach(var task in service.tasks)if(task.Owner==this)task.Cancelled=true;
                properties.Clear();maps.Clear();manager.Remove(this);service.ReleaseMaps?.Invoke(this);
                disposing=false;
            }
        }
    }
}
