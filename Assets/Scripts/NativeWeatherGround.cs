using System;
using System.Collections.Generic;
using UnityEngine;

// Direct port of weatheraccum.cpp/weatherpuddle.cpp/weathersplash.cpp placement rules.
// All positions below are original map pixels (positive Y down).
public sealed class NativeWeatherGround : MonoBehaviour
{
    sealed class Deposit { public NativeImgAnimation Art; public int X,Slot,Depth,Half; }
    sealed class Print { public NativeImgAnimation Art; public int X; public long Born; }
    sealed class Puddle { public NativeImgAnimation Art; public int X,Y,Tier,Variant,MaxTier; }
    readonly List<Deposit> deposits=new List<Deposit>();
    readonly List<Print> prints=new List<Print>();
    readonly List<Puddle> puddles=new List<Puddle>();
    readonly List<NativeImgAnimation> splashes=new List<NativeImgAnimation>();
    readonly Dictionary<string,int> variants=new Dictionary<string,int>();
    FieldMap map;
    string levelKind,seededFor;
    float snowLevel,wetLevel,snowCarry,wetCarry,splashCarry;
    int snowCatchUp,wetCatchUp,lastPrintX=int.MinValue,lastWalkX;
    bool wetSeeded,wasGrounded,wasWet;
    long lastFrame;
    uint snowRand=0x7F4A7C15,wetRand=0x2545F491,splashRand=0x1D872B41;
    Color tint;
    int groundOrder;
    public int DepositCount=>deposits.Count;
    public int PuddleCount=>puddles.Count;
    public int FootprintCount=>prints.Count;
    public float SnowLevel=>levelKind=="snow"?snowLevel:0;
    static uint Next(ref uint value){value^=value<<13;value^=value>>17;value^=value<<5;return value;}
    public void Bind(FieldMap value,int groundOrder=-399){map=value;this.groundOrder=groundOrder;}
    static Vector3 World(int x,int y)=>new Vector3(x/Art.PPU,-y/Art.PPU,0);
    bool Ground(int x,int from,int to,out int y)
    {
        float best=float.PositiveInfinity;
        if(map!=null)foreach(var f in map.footholds)
        {
            if(f.x1==f.x2 || x<Mathf.Min(f.x1,f.x2) || x>Mathf.Max(f.x1,f.x2))continue;
            float at=f.y1+(f.y2-f.y1)*(x-f.x1)/(f.x2-f.x1);
            if(at>=from && at<=to && at<best)best=at;
        }
        y=float.IsPositiveInfinity(best)?0:(int)best;return !float.IsPositiveInfinity(best);
    }
    bool Neighbours(int x,int from,int to,int y,int probe,int tolerance)
        =>Ground(x-probe,from,to,out int left)&&Ground(x+probe,from,to,out int right)&&Math.Abs(left-y)<=tolerance&&Math.Abs(right-y)<=tolerance;
    NativeImgAnimation Make(string path,int x,int y,int order,bool loop=false,float life=0)
    {
        var a=NativeImgAnimation.Spawn(path,transform,World(x,y),order,false,loop,life);
        if(a!=null)a.Tint=tint;return a;
    }
    int Variants(string kind)
    {
        if(variants.TryGetValue(kind,out int count))return count;
        while(count<32 && WzAssets.Node("Effect/WeatherAccum.img/"+kind+"/"+count+"/0")!=null)count++;
        variants[kind]=count;return count;
    }
    static void Remove(NativeImgAnimation art){if(art!=null)art.Remove();}
    void Cull(int cx,int half)
    {
        for(int i=deposits.Count-1;i>=0;i--)if(deposits[i].Art==null||Math.Abs(deposits[i].X-cx)>half*3){Remove(deposits[i].Art);deposits.RemoveAt(i);}
        for(int i=puddles.Count-1;i>=0;i--)if(puddles[i].Art==null||Math.Abs(puddles[i].X-cx)>half*3){Remove(puddles[i].Art);puddles.RemoveAt(i);}
    }
    void RemoveFarDeposit(int cx)
    {
        int far=0;for(int i=1;i<deposits.Count;i++)if(Math.Abs(deposits[i].X-cx)>Math.Abs(deposits[far].X-cx))far=i;
        Remove(deposits[far].Art);deposits.RemoveAt(far);
    }
    void RemoveFarPuddle(int cx)
    {
        int far=0;for(int i=1;i<puddles.Count;i++)if(Math.Abs(puddles[i].X-cx)>Math.Abs(puddles[far].X-cx))far=i;
        Remove(puddles[far].Art);puddles.RemoveAt(far);
    }
    bool PlaceDeposit(string kind,int cx,int cy,int hw,int hh)
    {
        int count=Variants(kind);if(count==0)return false;
        hw+=80;hh+=80;
        for(int attempt=0;attempt<6;attempt++)
        {
            int slot=(int)Math.Floor((cx-hw)/26.0)+(int)(Next(ref snowRand)%(uint)(2*hw/26+1));
            int depth=0;foreach(var d in deposits)if(d.Slot==slot)depth++;
            if(depth>=3)continue;
            int x=slot*26+(int)(Next(ref snowRand)%26);
            if(!Ground(x,cy-hh,cy+hh,out int y)||!Neighbours(x,cy-hh,cy+hh,y,14,10))continue;
            int variant=(int)(Next(ref snowRand)%(uint)count);
            var art=Make("Effect/WeatherAccum.img/"+kind+"/"+variant,x,y+5-depth*2,groundOrder);
            if(art==null)return false;
            deposits.Add(new Deposit{Art=art,X=x,Slot=slot,Depth=depth,Half=(int)(art.Image.sprite.rect.width/2)});return true;
        }
        return false;
    }
    void Footprints(long now)
    {
        for(int i=prints.Count-1;i>=0;i--)
        {
            var print=prints[i];long age=now-print.Born;
            if(print.Art==null||age>=11000){Remove(print.Art);prints.RemoveAt(i);continue;}
            var color=tint;color.a*=age>7500?(11000-age)/3500f:1;print.Art.Tint=color;
        }
        var player=FindObjectOfType<PlayerCtrl>();
        if(player==null||deposits.Count==0){wasGrounded=false;return;}
        int x=(int)(player.FeetPos.x*Art.PPU),y=(int)(-player.FeetPos.y*Art.PPU);
        bool grounded=Ground(x,y-6,y+8,out int surface)&&Math.Abs(surface-y)<=8;
        bool landed=grounded&&!wasGrounded;wasGrounded=grounded;
        if(!grounded || (!landed&&lastPrintX!=int.MinValue&&Math.Abs(x-lastPrintX)<22) || levelKind!="snow")return;
        bool covered=false;foreach(var d in deposits)covered|=Math.Abs(x-d.X)<=d.Half;
        int count=Variants("print");if(!covered||count==0)return;
        var art=Make("Effect/WeatherAccum.img/print/"+(Next(ref snowRand)%(uint)count),x,surface+5,groundOrder+1,false,11);
        if(art==null)return;
        if(prints.Count>=20){Remove(prints[0].Art);prints.RemoveAt(0);}
        prints.Add(new Print{Art=art,X=x,Born=now});lastPrintX=x;
    }
    void Accumulate(int sky,int age,int dt,long now,int cx,int cy,int hw,int hh)
    {
        string kind=sky==2||sky==5?"snow":sky==6?"leaf":sky==7?"petal":null;
        if(kind!=null&&seededFor!=kind&&snowLevel<=0)
        {seededFor=kind;snowLevel=Mathf.Min(1,(age/1000)*1000/90000f);snowCatchUp=(int)(snowLevel*70);}
        bool growing=kind!=null&&(levelKind==null||levelKind==kind);
        if(growing){levelKind=kind;snowLevel=Mathf.Min(1,snowLevel+dt/90000f);}
        else snowLevel=Mathf.Max(0,snowLevel-dt/25000f);
        foreach(var d in deposits)if(d.Art!=null)d.Art.Tint=tint;
        Footprints(now);
        if(snowLevel<=0)
        {
            foreach(var d in deposits)Remove(d.Art);deposits.Clear();levelKind=null;
            snowCarry=0;seededFor=null;snowCatchUp=0;return;
        }
        int target=(int)(snowLevel*70);
        if(deposits.Count>target){RemoveFarDeposit(cx);return;}
        if(!growing||deposits.Count>=target)return;
        snowCarry+=14*dt/1000f;int want=(int)snowCarry;snowCarry-=want;
        int burst=Math.Min(snowCatchUp,25);want+=burst;snowCatchUp-=burst;
        while(want>0&&deposits.Count<target){if(!PlaceDeposit(kind,cx,cy,hw,hh))break;want--;}
        if(burst>0&&want>0)snowCatchUp+=Math.Min(want,burst);
    }
    static int Half(int tier)=>tier==0?15:tier==1?24:35;
    bool Holds(int x,int from,int to,int y,int tier)=>Neighbours(x,from,to,y,Half(tier)+2,10);
    bool PlacePuddle(int cx,int cy,int hw,int hh)
    {
        hw+=80;hh+=80;int from=cy-hh,to=cy+hh;
        for(int attempt=0;attempt<12;attempt++)
        {
            int x=cx-hw+(int)(Next(ref wetRand)%(uint)(2*hw));
            if(!Ground(x,from,to,out int y)||!Neighbours(x,from,to,y,12,3)||!Holds(x,from,to,y,0))continue;
            int max=0;while(max+1<3&&Holds(x,from,to,y,max+1))max++;
            bool close=false;foreach(var p in puddles)if(Math.Abs(y+6-p.Y)<=24&&Math.Abs(x-p.X)<Half(max)+Half(p.MaxTier)+56){close=true;break;}
            if(close)continue;
            int variant=(int)(Next(ref wetRand)%3);
            var art=Make("Effect/WeatherPuddle.img/0/"+variant,x,y+6,groundOrder,true);
            if(art==null)return false;
            puddles.Add(new Puddle{Art=art,X=x,Y=y+6,Variant=variant,MaxTier=max});return true;
        }
        return false;
    }
    bool Promote(int tier)
    {
        foreach(var p in puddles)
        {
            if(p.Tier>=tier||p.Tier+1>=3||p.Tier+1>p.MaxTier)continue;
            var art=Make("Effect/WeatherPuddle.img/"+(p.Tier+1)+"/"+p.Variant,p.X,p.Y,groundOrder,true);
            if(art==null)return false;Remove(p.Art);p.Art=art;p.Tier++;return true;
        }
        return false;
    }
    void StepWater()
    {
        var player=FindObjectOfType<PlayerCtrl>();if(player==null){wasWet=false;return;}
        int x=(int)(player.FeetPos.x*Art.PPU),y=(int)(-player.FeetPos.y*Art.PPU);
        Puddle inside=null;foreach(var p in puddles)if(x>=p.X-Half(p.Tier)&&x<=p.X+Half(p.Tier)&&Math.Abs(y-p.Y)<=14){inside=p;break;}
        if(inside==null){wasWet=false;return;}
        if(!wasWet){wasWet=true;lastWalkX=x;Burst(x,inside.Y-6,6,11);}
        else if(Math.Abs(x-lastWalkX)>=26){lastWalkX=x;Burst(x,inside.Y-6,2,11);}
    }
    void Puddles(int sky,int age,int dt,int cx,int cy,int hw,int hh)
    {
        StepWater();foreach(var p in puddles)if(p.Art!=null)p.Art.Tint=tint;
        bool wet=sky==1||sky==4;
        if(wet&&!wetSeeded&&wetLevel<=0){wetSeeded=true;wetLevel=Mathf.Min(1,(age/1000)*1000/75000f);wetCatchUp=(int)(wetLevel*26);}
        if(!wet)wetSeeded=false;
        wetLevel=Mathf.Clamp01(wetLevel+dt/(wet?75000f:-40000f));
        if(wetLevel<=0){foreach(var p in puddles)Remove(p.Art);puddles.Clear();wetCarry=0;return;}
        int target=(int)(wetLevel*26);if(puddles.Count>target){RemoveFarPuddle(cx);return;}
        if(!wet)return;
        wetCarry+=3*dt/1000f;int want=(int)wetCarry;wetCarry-=want;
        int burst=Math.Min(wetCatchUp,10);want+=burst;wetCatchUp-=burst;
        int tier=Math.Min(2,(int)(wetLevel*3));
        while(want-->0){if(puddles.Count<target&&PlacePuddle(cx,cy,hw,hh))continue;if(!Promote(tier))break;}
    }
    void Splash(int x,int surface,Color color)
    {
        if(splashes.Count>=40)return;
        var art=Make("Effect/WeatherSplash.img/rain/"+(Next(ref splashRand)%3),x,surface+4,groundOrder+1,false,.520f);
        if(art!=null){art.Tint=color;splashes.Add(art);}
    }
    void Burst(int x,int y,int count,int spread)
    {
        for(int i=0;i<count&&splashes.Count<40;i++)Splash(x-spread+(int)(Next(ref splashRand)%(uint)(2*spread+1)),y,tint);
    }
    void RainSplashes(float intensity,int dt,int cx,int cy,int hw,int hh,Color rainTint)
    {
        splashes.RemoveAll(x=>x==null);
        if(intensity<=0||dt==0)return;
        splashCarry+=22.4f*intensity*dt/1000f;int want=(int)splashCarry;splashCarry-=want;
        hw+=64;hh+=64;
        while(want-->0)
        {
            if(splashes.Count>=40){splashCarry=0;break;}
            int x;
            if(Next(ref splashRand)%3==0&&puddles.Count>0)
            {var p=puddles[(int)(Next(ref wetRand)%(uint)puddles.Count)];x=p.X-Half(p.Tier)+(int)(Next(ref wetRand)%(uint)(2*Half(p.Tier)+1));}
            else x=cx-hw+(int)(Next(ref splashRand)%(uint)(2*hw));
            if(Ground(x,cy-hh,cy+hh,out int y))Splash(x,y,rainTint);
        }
    }
    public void Tick(int sky,int age,float rain,long now,Color scenery,Color rainTint)
    {
        int dt=lastFrame==0?0:(int)Math.Min(100,Math.Max(0,now-lastFrame));lastFrame=now;tint=scenery;
        var camera=Camera.main;if(camera==null||map==null)return;
        int cx=(int)(camera.transform.position.x*Art.PPU),cy=(int)(-camera.transform.position.y*Art.PPU);
        int hh=(int)(camera.orthographicSize*Art.PPU),hw=(int)(hh*camera.aspect);
        Cull(cx,hw);Accumulate(sky,age,dt,now,cx,cy,hw,hh);Puddles(sky,age,dt,cx,cy,hw,hh);RainSplashes(rain,dt,cx,cy,hw,hh,rainTint);
    }
    public void Clear()
    {
        foreach(var d in deposits)Remove(d.Art);deposits.Clear();
        foreach(var p in puddles)Remove(p.Art);puddles.Clear();
        foreach(var p in prints)Remove(p.Art);prints.Clear();
        foreach(var s in splashes)Remove(s);splashes.Clear();
        snowLevel=wetLevel=snowCarry=wetCarry=splashCarry=0;
        snowCatchUp=wetCatchUp=0;levelKind=seededFor=null;wetSeeded=wasGrounded=wasWet=false;
        lastPrintX=int.MinValue;lastFrame=0;
    }
    void OnDisable()=>Clear();
}
