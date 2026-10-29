using System;
using System.Collections.Generic;

// Weather_HandleWorldState / SetWorldState. Units are real milliseconds.
public sealed class NativeWeatherState
{
    public double Minute { get; private set; } = 720;
    public int MsPerMinute { get; private set; } = 10000;
    public int Sky { get; private set; }
    public bool Frozen { get; private set; }
    public bool BareSky { get; private set; }
    public bool Disabled { get; private set; }
    public int Tint { get; private set; } = 0x4A5A8C;
    public int Palette { get; private set; } = 26;
    public bool HasPalette { get; private set; }
    public uint Token { get; private set; }
    public int Revision { get; private set; }
    public bool Snap { get; private set; }
    long stamped, age, rainbow, last;
    bool hasState;
    public void LeaveField() { Sky = 0; Revision++; }
    static int Int(byte[] b,ref int p) { int n=b[p]|b[p+1]<<8|b[p+2]<<16|b[p+3]<<24; p+=4; return n; }
    public bool Read(byte[] packet,int offset,long now)
    {
        if(packet==null || offset<0 || offset>packet.Length-10) return false;
        int p=offset+2; int minute=packet[p]|packet[p+1]<<8; p+=2;
        int ms=Int(packet,ref p),sky=packet[p++],flags=packet[p++];
        int sec=packet.Length-p>=4 ? Math.Max(0,Int(packet,ref p)) : 0;
        int tint=0;
        if(packet.Length-p>=3) { tint=packet[p]<<16|packet[p+1]<<8|packet[p+2]; p+=3; }
        int rainbow=0;
        if(packet.Length-p>=2) { rainbow=Math.Max(0,(int)(short)(packet[p]|packet[p+1]<<8)); p+=2; }
        int palette=packet.Length-p>=1 ? packet[p++] : -1;
        int age=packet.Length-p>=4 ? Int(packet,ref p) : -1;
        uint token=packet.Length-p>=4 ? unchecked((uint)Int(packet,ref p)) : 0;
        Set(minute,ms,sky,flags,sec,tint,rainbow,palette,age,token,now); return true;
    }
    public void Set(int minute,int ms,int sky,int flags,int seconds,int tint,int rainbowSeconds,int palette,int elapsedMs,uint token,long now)
    {
        Disabled=(flags&8)!=0;
        if(Disabled) { minute=720; sky=0; flags|=2; rainbowSeconds=0; }
        Minute=minute>=0 && minute<1440 ? minute : 0;
        MsPerMinute=Math.Max(100,ms); Sky=sky>=0 && sky<9 ? sky : 0;
        Frozen=(flags&2)!=0; BareSky=(flags&4)!=0; Snap=(flags&1)!=0;
        age=elapsedMs>=0 ? elapsedMs : Math.Min(int.MaxValue,(long)Math.Max(0,seconds)*1000);
        if(elapsedMs>=0) Token=token;
        if(tint!=0) Tint=tint;
        if(palette>=0 && palette<27) { Palette=palette; HasPalette=true; }
        rainbow=Math.Max(0,rainbowSeconds); stamped=now; hasState=true; Revision++;
    }
    public void Tick(long now)
    {
        long dt=now-last;
        if(last!=0 && dt>=0 && dt<60000 && !Frozen) Minute=(Minute+(double)dt/MsPerMinute)%1440;
        last=now;
    }
    public int Age(long now) => !hasState ? 0 : (int)Math.Min(int.MaxValue,age+Math.Max(0,now-stamped));
    public int Rainbow(long now) => !hasState ? 0 : (int)Math.Max(0,rainbow-Math.Max(0,now-stamped)/1000);
    public static float Night(double minute)
        => minute<300 || minute>=1140 ? 1 : minute<420 ? 1-(float)(minute-300)/120 : minute<1020 ? 0 : (float)(minute-1020)/120;
}

public sealed class NativeWeatherRegion
{
    public string Name;
    public int Forced, Tint, Palette;
    public double[] Weights;
}

// WeatherRuntime.java: one shared clock and independent regional occurrences.
// Random input and time are injected for reproducible checks, never Unity frame time.
public sealed class NativeWeatherService
{
    public bool Enabled=true, InjectSky=true, SeasonDrift=true;
    public long DayLength=14400000, ChangeInterval=900000, OverrideHold=3600000;
    public int RainbowDuration=180;
    readonly NativeWeatherRegion[] regions;
    readonly int[] skies;
    readonly long[] since, rainbowUntil;
    readonly uint[] tokens;
    readonly Func<double> random;
    long nextRoll, overrideUntil;
    int overrideSky=-1, overrideMinute=-1;
    int revision;
    /// <summary>
    /// 后台（AdminWeather）改动版本号：强制天气/时段、区域强制、重掷风向、自然轮换都会自增。
    /// 客户端每帧比对，变了就立刻 Sync —— 否则后台点完要等下一次 60s 周期同步才看得到。
    /// </summary>
    public int Revision => revision;
    /// <summary>配置项（enabled/昼长等）被后台改过：让客户端下一帧重新 Sync。</summary>
    public void Touch() => revision++;
    public NativeWeatherService(NativeWeatherRegion[] regions,long now,Func<double> random)
    {
        this.regions=regions; this.random=random;
        skies=new int[regions.Length]; since=new long[regions.Length]; rainbowUntil=new long[regions.Length]; tokens=new uint[regions.Length];
        for(int i=0;i<regions.Length;i++) { skies[i]=Math.Max(0,regions[i].Forced); since[i]=now; tokens[i]=NewToken(); }
    }
    uint NewToken() => unchecked((uint)(random()*4294967296.0));
    int Automatic(int r) => regions[r].Forced>=0 ? regions[r].Forced : skies[r];
    int Effective(int r) => overrideSky>=0 ? overrideSky : Automatic(r);
    void Transition(int r,int previous,int next,long now)
    {
        if(previous==next)return;
        since[r]=now; tokens[r]=NewToken(); revision++;
        rainbowUntil[r]=(previous==1||previous==4) && next!=1 && next!=4 ? now+RainbowDuration*1000L : 0;
    }
    public void ClearOverride(long now)
    {
        if(overrideSky>=0) for(int r=0;r<regions.Length;r++) Transition(r,overrideSky,Automatic(r),now);
        overrideSky=overrideMinute=-1; overrideUntil=0; nextRoll=0; revision++;
    }

    // —————— 后台（AdminWeather）读写口 —————— ——————
    public long NextRollAt => nextRoll;
    public long OverrideUntil => overrideUntil;
    public int OverrideSky => overrideSky;
    public int OverrideMinute => overrideMinute;
    public int RegionCount => regions.Length;
    public NativeWeatherRegion Region(int i) => regions[i];
    public int SkyOf(int r) => skies[r];
    /// <summary>当前游戏时刻（分钟 0-1439）：时间被冻结时返回覆盖值。</summary>
    public int MinuteOfDay(long now)
        => overrideMinute >= 0 ? overrideMinute
         : (int)(((((now % DayLength) + DayLength) % DayLength) * 1440) / Math.Max(1, DayLength));
    /// <summary>昼夜系数（与客户端 NativeWeatherState.Night 同公式）。</summary>
    public float NightLevel(long now) => NativeWeatherState.Night(MinuteOfDay(now));
    /// <summary>源端 rerollWind：只重掷阵风方向（token），不改天气类型。</summary>
    public void RerollWind() { for (int r = 0; r < regions.Length; r++) tokens[r] = NewToken(); revision++; }
    /// <summary>改 forced（强制天气）后调用，让该区域立即落到新值。</summary>
    public void ApplyForced(int r, int previousEffective, long now) { Transition(r, previousEffective, Effective(r), now); revision++; }
    void Expire(long now) { if(overrideUntil>0 && overrideUntil<=now) ClearOverride(now); }
    public void Override(int? sky,int? minute,long duration,long now)
    {
        if(sky==null && minute==null) throw new ArgumentException("Weather or time required");
        Expire(now);
        for(int r=0;r<regions.Length;r++) Transition(r,Effective(r),sky??Automatic(r),now);
        overrideSky=sky??-1; overrideMinute=minute??-1; overrideUntil=now+duration; revision++;
    }
    public bool Roll(long now,int month)
    {
        Expire(now);
        if(!Enabled || overrideSky>=0 || now<nextRoll)return false;
        nextRoll=now+ChangeInterval;
        for(int r=0;r<regions.Length;r++)
        {
            int next=regions[r].Forced;
            if(next<0)
            {
                var w=(double[])regions[r].Weights.Clone();
                if(SeasonDrift) ApplySeason(w,month);
                double total=0; foreach(double v in w)total+=Math.Max(0,v);
                double cursor=random()*total; next=0;
                for(int i=0;i<w.Length;i++) { cursor-=Math.Max(0,w[i]); if(cursor<0){next=i;break;} }
            }
            Transition(r,skies[r],next,now); skies[r]=next;
        }
        return true;
    }
    public void Sync(int map,bool snap,NativeWeatherState client,long now)
    {
        Expire(now); int r=RegionForMap(map); var region=regions[r];
        int minute=overrideMinute>=0 ? overrideMinute : (int)((((now%DayLength)+DayLength)%DayLength)*1440/DayLength);
        int flags=(snap?1:0)|(overrideMinute>=0||!Enabled?2:0)|(InjectSky?4:0)|(!Enabled?8:0);
        int elapsed=(int)Math.Min(int.MaxValue,Math.Max(0,now-since[r]));
        client.Set(Enabled?minute:720,(int)Math.Max(1,DayLength/1440),Enabled?Effective(r):0,flags,elapsed/1000,region.Tint,
            (int)Math.Min(short.MaxValue,Math.Max(0,rainbowUntil[r]-now)/1000),r,elapsed,tokens[r],now);
    }
    public static int RegionForMap(int map)
    {
        if(map<0)return 26;
        switch(map/1000000) {
            case 100:return 19; case 101:return 3; case 102:return 4; case 103:return 5; case 104:return 16; case 105:return 10;
            case 110:return 14; case 120:return 18; case 130:return 20; case 140:return 1; case 200:return 7; case 211:return 0;
            case 220:case 221:case 222:return 13; case 230:return 11; case 240:return 12; case 250:case 251:return 8;
            case 260:return 9; case 261:return 17; case 270:return 21; case 300:return 22; case 600:case 610:return 23;
            case 680:return 15; case 740:case 741:case 742:return 24; case 800:return map==800000000?2:25; case 801:return 6; default:return 26;
        }
    }
    public static void ApplySeason(double[] w,int month)
    {
        if(month==12||month<=2) {w[2]*=1.8;w[5]*=1.5;w[1]*=.7;w[6]*=.4;w[7]*=.3;}
        else if(month<=5) {w[7]*=1.8;w[1]*=1.2;w[2]*=.5;w[5]*=.4;}
        else if(month<=8) {w[1]*=1.4;w[4]*=1.3;w[2]*=.3;w[5]*=.2;w[6]*=.6;}
        else {w[6]*=1.8;w[3]*=1.2;w[7]*=.4;w[2]*=.7;}
    }
}

// 天气服务的唯一入口：游戏（NativeWeatherField）与后台（AdminWeather）共用一个实例，
// 否则后台改了天气、游戏里看不到（两个 NativeWeatherService 各滚各的天空）。
// 配置来自 Resources/NativeWeather.json（regions 的 name/forced/weights/tint 与源端
// WeatherRegion 枚举同序）。纯 C#，可被 tools/admin-check 直接编译。
public static class NativeWeatherHost
{
    public static NativeWeatherService Shared;

    /// <summary>Unity 侧注入：返回已挂在 GameServer 上的服务（没有就 null）。</summary>
    public static Func<NativeWeatherService> FromGame;

    public static long NowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    public static NativeWeatherService Service
    {
        get
        {
            var live = FromGame != null ? FromGame() : null;
            if (live != null) { Shared = live; return live; }
            if (Shared == null) Shared = Create(PlatformRes.LoadText("NativeWeather"), NowMs());
            return Shared;
        }
    }

    public static NativeWeatherService Create(string json, long now)
    {
        var root = MiniJsonDb.Parse(json) as Dictionary<string, object>;
        if (root == null || !root.TryGetValue("regions", out var raw) || !(raw is List<object> rows))
            throw new InvalidOperationException("NativeWeather.json 缺少 regions");
        var regions = new NativeWeatherRegion[rows.Count];
        for (int i = 0; i < rows.Count; i++)
        {
            var row = (Dictionary<string, object>)rows[i];
            var weights = (List<object>)row["weights"];
            var w = new double[weights.Count];
            for (int j = 0; j < w.Length; j++) w[j] = Convert.ToDouble(weights[j]);
            regions[i] = new NativeWeatherRegion
            {
                Name = (string)row["name"],
                Forced = Convert.ToInt32(row["forced"]),
                Tint = Convert.ToInt32(row["tint"]),
                Palette = i,
                Weights = w,
            };
        }
        var rng = new Random();
        return new NativeWeatherService(regions, now, rng.NextDouble);
    }
}
