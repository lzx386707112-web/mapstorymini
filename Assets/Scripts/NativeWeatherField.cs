using System;
using System.Collections.Generic;
using MapleDash.Wz;
using UnityEngine;

// Original weather.cpp/weatherfx.cpp table-driven sky sheets. No synthesized art.
public sealed class NativeWeatherField : MonoBehaviour
{
    static Dictionary<string,object> data;
    static NativeWeatherService detached;
    static NativeWeatherField current;
    // 客户端天气表现默认开启，运行期由服务端 Enabled（后台「天气总开关」）决定是否真的生效：
    // 关掉时 Sync 带 Disabled 位，客户端 State 归零，只剩原图。源端 config.ini 的
    // enableWeatherSystem=false 只是原版默认不显示天气，这里改由后台控，否则后台点了没反应。
    public static bool ClientEnabled { get; set; } = true;
    public readonly NativeWeatherState State = new NativeWeatherState();
    public bool HasSky { get; private set; }
    // 该地图本没有天空贴图基准（见 NativeWeather.json 的 excluded），但后台强制了天气 /
    // 该区域被 forced：这时仍要挂雨雪雾云层，否则在城镇里点「下雨」什么都看不到。
    public bool Forced { get; private set; }
    public bool Active { get; private set; }
    public bool WaterBase { get; private set; }
    readonly Dictionary<string,float> levels = new Dictionary<string,float>();
    readonly List<Sheet> sheets = new List<Sheet>();
    readonly Dictionary<SpriteRenderer,Color> scenery = new Dictionary<SpriteRenderer,Color>();
    NativeMapView view;
    NativeWeatherService server;
    ImgNode mapNode;
    bool mounted;
    int mapId, revision=-1, syncedRevision;
    long syncAt, tickAt;
    float night, boost;
    Vector3 tint=Vector3.one*255;
    bool primed;
    NativeWeatherGround ground;
    NativeWeatherSound sound;
    NativeWeatherParticles particles;
    Color groundTint=Color.white,rainTint=Color.white;
    sealed class Sheet { public SpriteRenderer Image; public string Group; }
    public static Dictionary<string,object> Data
    {
        get
        {
            if(data==null)
            {
                data=(Dictionary<string,object>)MiniJson.Parse(Resources.Load<TextAsset>("NativeWeather").text);
                // 不再用 data["clientEnabled"] 关掉整个模块：那是源端 config.ini 的出厂值，
                // 后台「天气总开关」才是本工程的准绳（关掉走 Sync 的 Disabled 位）。
            }
            return data;
        }
    }
    static double Number(object x)=>Convert.ToDouble(x);
    static int Int(object x)=>Convert.ToInt32(x);
    static long Now=>DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    static long LocalNow=>(long)(Time.realtimeSinceStartupAsDouble*1000);
    static NativeWeatherService Service()
    {
        if(GameSession.Server?.Weather!=null){NativeWeatherHost.Shared=GameSession.Server.Weather;return GameSession.Server.Weather;}
        // 后台（AdminWeather）可能先进图之前就建好了实例（Host.Service 的兜底）。
        // 直接接管那份，别另建一个新的 —— 否则后台点过的强制天气会随旧实例一起被丢掉。
        if(NativeWeatherHost.Shared!=null)
        {
            if(GameSession.Server!=null)GameSession.Server.Weather=NativeWeatherHost.Shared;
            else detached=NativeWeatherHost.Shared;
            return NativeWeatherHost.Shared;
        }
        if(GameSession.Server==null && detached!=null){NativeWeatherHost.Shared=detached;return detached;}
        var rows=(List<object>)Data["regions"];
        var regions=new NativeWeatherRegion[rows.Count];
        for(int i=0;i<rows.Count;i++)
        {
            var row=(Dictionary<string,object>)rows[i]; var weights=(List<object>)row["weights"];
            var w=new double[weights.Count];for(int j=0;j<w.Length;j++)w[j]=Number(weights[j]);
            regions[i]=new NativeWeatherRegion {Name=(string)row["name"],Forced=Int(row["forced"]),Tint=Int(row["tint"]),Palette=i,Weights=w};
        }
        var rng=new System.Random();
        var service=new NativeWeatherService(regions,Now,rng.NextDouble);
        if(GameSession.Server!=null)GameSession.Server.Weather=service;else detached=service;
        // 后台（AdminWeather）与游戏共用同一实例：后台改天气/时段，游戏下一帧 Sync 就能看到
        NativeWeatherHost.Shared=service;
        if(NativeWeatherHost.FromGame==null)NativeWeatherHost.FromGame=()=>GameSession.Server?.Weather;
        return service;
    }
    public static bool SkyForMap(int id,ImgNode map)
    {
        foreach(var excluded in (List<object>)Data["excluded"])if(Int(excluded)==id)return false;
        var first=map.Get("back/0");if(first==null)return false;
        foreach(Dictionary<string,object> sky in (List<object>)Data["skyBases"])
            if(string.Equals((string)sky["bank"],first.Text("bS"),StringComparison.OrdinalIgnoreCase) && Int(sky["no"])==first.Int("no"))return true;
        return false;
    }
    public void Bind(NativeMapView view,int id,ImgNode map)
    {
        var config=Data;this.view=view;mapId=id;mapNode=map;
        if(!ClientEnabled)return;
        current=this; HasSky=SkyForMap(id,map);
        server=Service();
        // 「后台强制了这个区域的天气」= 运营正在演示，本图即使默认无天空基准也要表现
        Forced=IsForcedByServer();
        Active=HasSky || id/1000000==230 || Forced;
        WaterBase=string.Equals(map.Get("back/0")?.Text("bS"),"nautilusPort",StringComparison.OrdinalIgnoreCase);
        server.Roll(Now,DateTime.Now.Month);server.Sync(id,true,State,Now);
        // Client ages are monotonic; server's Unix clock is only used in sync payloads.
        RebaseSync();
        foreach(var image in view.GetComponentsInChildren<SpriteRenderer>())scenery[image]=image.color;
        if(HasSky || Forced) MountLayers();
        Tick();
    }
    bool IsForcedByServer()
    {
        if(server==null) return false;
        if(server.OverrideSky>=0) return true;
        int region=NativeWeatherService.RegionForMap(mapId);
        return region>=0 && region<server.RegionCount && server.Region(region).Forced>=0;
    }
    // 天气图层：本来每个地图进图时挂一次；后台中途强制天气也要能补挂（见 Tick）。
    void MountLayers()
    {
        if(mounted || view==null || Data==null) return;
        mounted=true;
        int index=0;
        foreach(Dictionary<string,object> row in (List<object>)Data["layers"])
        {
            // 本图没有天空基准时跳过 sky 组：那几张是整屏天空贴图，盖在原图上会毁掉原画。
            // 夜色/云/雨/雪/雾/彩虹都能直接叠，运营要的「看得见变化」靠它们。
            if(!HasSky && (string)row["group"]=="sky") { index++; continue; }
            var image=view.AddWeatherBack(row,((bool)row["front"]?700:-1100)+index++);
            if(image!=null)sheets.Add(new Sheet {Image=image,Group=(string)row["group"]});
        }
        if(HasSky) view.PromoteWeatherBacks(mapNode);
        ground=gameObject.AddComponent<NativeWeatherGround>();
        ground.Bind(NativeMapView.LoadMap(new FieldMap{id=mapId}));
        sound=gameObject.AddComponent<NativeWeatherSound>();
        particles=gameObject.AddComponent<NativeWeatherParticles>();
    }
    void RebaseSync()
    {
        int age=State.Age(Now), rainbow=State.Rainbow(Now);
        State.Set((int)State.Minute,State.MsPerMinute,State.Sky,(State.Snap?1:0)|(State.Frozen?2:0)|(State.BareSky?4:0)|(State.Disabled?8:0),age/1000,State.Tint,rainbow,State.Palette,age,State.Token,LocalNow);
        syncAt=LocalNow+60000;
        if(server!=null) syncedRevision=server.Revision;
    }
    public static bool HandlePacket(byte[] packet,int offset=0)
        => current!=null && current.State.Read(packet,offset,LocalNow);
    public void Tick()
    {
        if(!ClientEnabled || server==null)return;
        long now=LocalNow;
        bool rolled=server.Roll(Now,DateTime.Now.Month);
        // 后台（AdminWeather）改过天气/时段/区域 → Revision 变了立刻推一份，而且带 snap，
        // 画面直接切过去而不是慢慢过渡。只靠 syncAt 的 60s 周期会让人以为「点了没反应」。
        bool changed=server.Revision!=syncedRevision;
        if(now>=syncAt || rolled || changed) {server.Sync(mapId,changed,State,Now);RebaseSync();}
        // 玩家先进图、后台随后点「强制天气」时补挂图层（本图原来没有天空基准，进图时没挂）。
        Forced=IsForcedByServer();
        if(!mounted && (HasSky || Forced)) { Active=true; MountLayers(); }
        State.Tick(now);
        if(now<tickAt)return; tickAt=now+30; // source CMapLoadable logic tick
        bool snap=State.Revision!=revision && State.Snap;
        if(State.Revision!=revision){revision=State.Revision;if(snap)primed=false;}
        bool renders=HasSky || Forced;
        var row=(Dictionary<string,object>)((List<object>)Data["profiles"])[renders?State.Sky:0];
        var p=(List<object>)row["values"];
        float step=(float)Number(p[7]);
        float targetNight=NativeWeatherState.Night(State.Minute);
        night=snap?targetNight:Mathf.MoveTowards(night,targetNight,.015f);
        Vector3 palette=PaletteTint(night);
        var targetTint=new Vector3(Mathf.Min(255,palette.x*(float)Number(p[3])/0x4A),Mathf.Min(255,palette.y*(float)Number(p[4])/0x5A),Mathf.Min(255,palette.z*(float)Number(p[5])/0x8C));
        float targetBoost=(float)Number(p[6]);
        if(!primed){tint=targetTint;boost=targetBoost;primed=true;}
        else {tint=new Vector3(Mathf.MoveTowards(tint.x,targetTint.x,step*255),Mathf.MoveTowards(tint.y,targetTint.y,step*255),Mathf.MoveTowards(tint.z,targetTint.z,step*255));boost=Mathf.MoveTowards(boost,targetBoost,step);}
        SetLevel("cloud",(float)Number(p[0]),step,snap);SetLevel("rain",(float)Number(p[1]),step,snap);
        SetLevel("dust",State.Sky==8?1:0,step,snap);SetLevel("fog",State.Sky==5?1:0,step,snap);
        SetLevel("snow",State.Sky==2?1:0,step,snap);SetLevel("rainbow",State.Rainbow(now)>0?.22f*(1-night):0,step,snap);
        if(!Active)return;
        int age=State.Age(now);
        float lightning=renders && State.Sky==4 && levels["rain"]>=.70f && age>0 ? NativeWeatherLightning.Evaluate((uint)age,State.Token,State.Palette) : 0;
        float darkness=Mathf.Max(night,boost)*(1-.697f*lightning);
        Color sceneryColor=TintColor(1,darkness);
        groundTint=sceneryColor;rainTint=TintColor(levels["rain"],darkness*.55f);
        foreach(var pair in scenery)if(pair.Key!=null)pair.Key.color=pair.Value*sceneryColor;
        view.SetWeatherSkyVisibility(State.BareSky);
        foreach(var sheet in sheets)
        {
            float alpha;
            if(sheet.Group=="sky")alpha=WaterBase?0:((Camera.main!=null && Camera.main.orthographicSize*2*Art.PPU>=900)?(night>.5f?1:0):night);
            else if(sheet.Group=="night")alpha=night;
            else alpha=levels[sheet.Group]*(sheet.Group=="fog"?.60f:1);
            if(sheet.Group=="sky" || sheet.Group=="night")alpha*=1-.578f*lightning;
            sheet.Image.color=sheet.Group=="sky"||sheet.Group=="night"?new Color(1,1,1,alpha):TintColor(alpha,darkness*(sheet.Group=="rain"?.55f:1));
        }
    }
    void SetLevel(string group,float target,float step,bool snap)
    { levels.TryGetValue(group,out float value);levels[group]=snap?target:Mathf.MoveTowards(value,target,step); }
    Vector3 PaletteTint(float n)
    {
        if(!State.HasPalette)return new Vector3((State.Tint>>16)&255,(State.Tint>>8)&255,State.Tint&255);
        var colors=(List<object>)((Dictionary<string,object>)((List<object>)Data["palettes"])[State.Palette])["colors"];
        var dusk=new Vector3(Int(colors[0]),Int(colors[1]),Int(colors[2]));var dark=new Vector3(Int(colors[3]),Int(colors[4]),Int(colors[5]));
        return n<=.35f?Vector3.Lerp(Vector3.one*255,dusk,n/.35f):Vector3.Lerp(dusk,dark,(n-.35f)/.65f);
    }
    Color TintColor(float alpha,float darkness)
    {
        float dawn=Golden(State.Minute,300,420),dusk=Golden(State.Minute,1020,1140);
        float k=Mathf.Max(dawn,dusk)*.85f*Mathf.Max(0,1-boost*2);
        Vector3 effective=Vector3.Lerp(tint,dawn>dusk?new Vector3(0xF2,0x9E,0xB8):new Vector3(0xFF,0xA2,0x48),k);
        return new Color((255-(int)(darkness*(255-effective.x)))/255f,(255-(int)(darkness*(255-effective.y)))/255f,(255-(int)(darkness*(255-effective.z)))/255f,(int)(Mathf.Clamp01(alpha)*255)/255f);
    }
    static float Golden(double minute,int start,int end)=>minute<start||minute>=end?0:Mathf.Sin((float)(minute-start)/(end-start)*Mathf.PI);
    void LateUpdate()
    {
        // 没天空基准但被后台强制天气时同样要跑地面/音效/粒子（HasSky 只是「原图自带天空」）。
        if(!ClientEnabled || !Active)return;
        levels.TryGetValue("rain",out float rain);
        ground?.Tick(State.Sky,State.Age(LocalNow),rain,LocalNow,groundTint,rainTint);
        var profile=(Dictionary<string,object>)((List<object>)Data["profiles"])[State.Sky];
        sound?.Tick(Int(((List<object>)profile["values"])[8]),rain);
        particles?.Tick(Int(((List<object>)profile["values"])[2]),State.Sky,State.Token,Time.realtimeSinceStartupAsDouble,groundTint);
    }
    void OnDisable(){if(current==this)current=null;}
}
