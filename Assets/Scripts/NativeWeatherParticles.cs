using System;
using System.Collections.Generic;
using UnityEngine;

// CField::BlowWeather -> 0063FE57, direction=0, and weatherwind.cpp.
// The Android viewport replaces resolution.cpp's screen box; art stays at native PPU.
public sealed class NativeWeatherParticles : MonoBehaviour
{
    sealed class Particle
    {
        public SpriteRenderer Image;
        public int X,Y,Amplitude,Angle,Fade,Alpha,Drift,Duration;
    }
    readonly List<Particle> particles=new List<Particle>();
    int item=-1,sky=-1,width,height;
    uint token,random=1,windRandom=0x9E3779B9;
    double started;
    public int Count=>particles.Count;
    int Rand(){random=unchecked(random*214013+2531011);return (int)((random>>16)&32767);}
    static uint Mix(uint v){unchecked{v^=v>>16;v*=0x7FEB352D;v^=v>>15;v*=0x846CA68B;return v^(v>>16);}}
    uint WindRand(){windRandom^=windRandom<<13;windRandom^=windRandom>>17;windRandom^=windRandom<<5;return windRandom;}
    public static int Prevailing(int sky,uint token)
    {
        uint u=Mix(token^unchecked((uint)sky*0x9E3779B9));
        if(sky==8)return ((u&1)!=0?1:-1)*(1200+(int)(Mix(unchecked(u+0xA511E9B3))%601));
        if(sky==5)return ((u&1)!=0?1:-1)*(150+(int)(Mix(unchecked(u+0x2545F491))%151));
        return (int)(u%601)-300;
    }
    int Drift(int sky,uint token,int fall)
    {
        int wind=Prevailing(sky,token);
        if(sky==2||sky==5)return Mathf.Clamp(Math.Abs(wind),fall*45/100,fall*75/100)+(int)(WindRand()%181)-90;
        int range=sky==8?260:150;return wind+(int)(WindRand()%(uint)(range*2+1))-range;
    }
    public void Clear()
    {
        foreach(var particle in particles)if(particle.Image!=null)
        {if(Application.isPlaying)Destroy(particle.Image.gameObject);else DestroyImmediate(particle.Image.gameObject);}
        particles.Clear();item=-1;
    }
    void Build(int wanted,int weather,uint skyToken,int w,int h,double now)
    {
        Clear();item=wanted;sky=weather;token=skyToken;width=w;height=h;started=now;
        if(wanted==0)return;
        var info=WzAssets.Node("Item/Cash/0512.img/"+wanted.ToString("D8")+"/info");
        if(info==null)return;
        var node=WzAssets.Node(info.Text("path"));if(node==null)return;
        // These ambient items have no animation/direction. Enumerate their actual canvases.
        var variants=new List<Sprite>();
        for(int i=0;node.Get(i.ToString())!=null;i++)variants.Add(WzAssets.Sprite(node.Get(i.ToString())));
        if(variants.Count==0)return;
        int count=node.Int("count",node.Int("random")!=0?100:0);
        if(node.Int("random")!=0)count=count*(Rand()%25+30)/100;
        for(int i=0;i<count;i++)
        {
            int amplitude=15+(Rand()%4)*(Rand()%4),angle=Rand()%360,fade=Rand()%3000+3000;
            int variant=Rand()%variants.Count;
            int x=Rand()%w-w/2,y=Rand()%h-h/2;
            Rand(); // stock horizontal spread is replaced by the independent wind PRNG
            var go=new GameObject("NativeWeatherParticle");go.transform.SetParent(transform,false);
            var image=go.AddComponent<SpriteRenderer>();image.sprite=variants[variant];image.sortingOrder=710;
            particles.Add(new Particle{Image=image,X=x,Y=y,Amplitude=amplitude,Angle=angle,Fade=fade,
                Alpha=node.Get(variant.ToString()).Int("a0",255),Drift=Drift(weather,skyToken,h/2),
                Duration=(50000-info.Int("speed")*8000)/amplitude});
            if(info.Int("type")==2)Rand(); // vector angle period; it does not rotate the Canvas
        }
    }
    static int Wrap(int value,int start,int size)=>start+((value-start)%size+size)%size;
    // Shape2D 51406FFA..51407045 rounds rotated coordinates away from zero at .5.
    static int RotatedPixel(double value)=>(int)(value+(value>=0?.5:-.5));
    public void Tick(int wanted,int weather,uint skyToken,double now,Color tint)
    {
        var camera=Camera.main;if(camera==null)return;
        int h=Mathf.Max(2,(int)(camera.orthographicSize*Art.PPU*2)),w=Mathf.Max(2,(int)(h*camera.aspect));
        // weatherfx.cpp only re-blows when the native item changes. A new token of
        // the same sky must not pop every particle or alter its existing trajectory.
        if(wanted!=item||w!=width||h!=height)Build(wanted,weather,skyToken,w,h,now);
        int age=(int)Math.Max(0,(now-started)*1000);
        foreach(var particle in particles)
        {
            double cycles=age/(double)particle.Duration;
            // Ratio links subtract their captured starting position; X 100%, Y 5%.
            double initial=particle.Angle*Math.PI/180,angle=initial+(age%2000)*Math.PI/1000;
            int radius=particle.Amplitude/2;
            int dx=RotatedPixel(radius*Math.Cos(angle))-RotatedPixel(radius*Math.Cos(initial));
            int dy=(RotatedPixel(radius*Math.Sin(angle))-RotatedPixel(radius*Math.Sin(initial)))*5/100;
            int x=Wrap(particle.X+(int)(particle.Drift*cycles),-w/2-particle.Amplitude,w+particle.Amplitude)+dx;
            int y=Wrap(particle.Y+(int)((h/2)*cycles),-h/2-particle.Amplitude,h+particle.Amplitude)+dy;
            particle.Image.transform.position=new Vector3(camera.transform.position.x+x/Art.PPU,camera.transform.position.y-y/Art.PPU,0);
            var color=Color.white;color.a=Mathf.Lerp(1,particle.Alpha-2*particle.Amplitude,Mathf.Clamp01(age/(float)particle.Fade))/255;
            particle.Image.color=color;
        }
    }
    void OnDisable(){Clear();item=-1;}
}
