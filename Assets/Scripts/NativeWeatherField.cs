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
    public static bool ClientEnabled { get; set; }
    public readonly NativeWeatherState State = new NativeWeatherState();
    public bool HasSky { get; private set; }
    public bool Active { get; private set; }
    public bool WaterBase { get; private set; }
    readonly Dictionary<string,float> levels = new Dictionary<string,float>();
    readonly List<Sheet> sheets = new List<Sheet>();
    readonly Dictionary<SpriteRenderer,Color> scenery = new Dictionary<SpriteRenderer,Color>();
    NativeMapView view;
    NativeWeatherService server;
    int mapId, revision=-1;
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
                ClientEnabled=(bool)data["clientEnabled"];
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
        var config=Data;this.view=view;mapId=id;
        if(!ClientEnabled)return;
        current=this; HasSky=SkyForMap(id,map); Active=HasSky || id/1000000==230;
        WaterBase=string.Equals(map.Get("back/0")?.Text("bS"),"nautilusPort",StringComparison.OrdinalIgnoreCase);
        server=Service();server.Roll(Now,DateTime.Now.Month);server.Sync(id,true,State,Now);
        // Client ages are monotonic; server's Unix clock is only used in sync payloads.
        RebaseSync();
        foreach(var image in view.GetComponentsInChildren<SpriteRenderer>())scenery[image]=image.color;
        if(HasSky)
        {
            int index=0;
            foreach(Dictionary<string,object> row in (List<object>)Data["layers"])
            {
                var image=view.AddWeatherBack(row,((bool)row["front"]?700:-1100)+index++);
                if(image!=null)sheets.Add(new Sheet {Image=image,Group=(string)row["group"]});
            }
            view.PromoteWeatherBacks(map);
            ground=gameObject.AddComponent<NativeWeatherGround>();
            ground.Bind(NativeMapView.LoadMap(new FieldMap{id=id}));
            sound=gameObject.AddComponent<NativeWeatherSound>();
            particles=gameObject.AddComponent<NativeWeatherParticles>();
        }
        Tick();
    }
    void RebaseSync()
    {
        int age=State.Age(Now), rainbow=State.Rainbow(Now);
        State.Set((int)State.Minute,State.MsPerMinute,State.Sky,(State.Snap?1:0)|(State.Frozen?2:0)|(State.BareSky?4:0)|(State.Disabled?8:0),age/1000,State.Tint,rainbow,State.Palette,age,State.Token,LocalNow);
        syncAt=LocalNow+60000;
    }
    public static bool HandlePacket(byte[] packet,int offset=0)
        => current!=null && current.State.Read(packet,offset,LocalNow);
    public void Tick()
    {
        if(!ClientEnabled || server==null)return;
        long now=LocalNow;
        bool rolled=server.Roll(Now,DateTime.Now.Month);
        if(now>=syncAt || rolled) {server.Sync(mapId,false,State,Now);RebaseSync();}
        State.Tick(now);
        if(now<tickAt)return; tickAt=now+30; // source CMapLoadable logic tick
        bool snap=State.Revision!=revision && State.Snap;
        if(State.Revision!=revision){revision=State.Revision;if(snap)primed=false;}
        var row=(Dictionary<string,object>)((List<object>)Data["profiles"])[HasSky?State.Sky:0];
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
        float lightning=HasSky && State.Sky==4 && levels["rain"]>=.70f && age>0 ? NativeWeatherLightning.Evaluate((uint)age,State.Token,State.Palette) : 0;
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
        if(!ClientEnabled || !HasSky || !Active)return;
        levels.TryGetValue("rain",out float rain);
        ground?.Tick(State.Sky,State.Age(LocalNow),rain,LocalNow,groundTint,rainTint);
        var profile=(Dictionary<string,object>)((List<object>)Data["profiles"])[State.Sky];
        sound?.Tick(Int(((List<object>)profile["values"])[8]),rain);
        particles?.Tick(Int(((List<object>)profile["values"])[2]),State.Sky,State.Token,Time.realtimeSinceStartupAsDouble,groundTint);
    }
    void OnDisable(){if(current==this)current=null;}
}
