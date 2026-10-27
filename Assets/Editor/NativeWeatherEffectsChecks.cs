using System;
using UnityEngine;

// Real IMG canvases + runtime ground/particle code; no login, DB, or save writes.
public static class NativeWeatherEffectsChecks
{
    static void Require(bool ok,string message){if(!ok)throw new Exception(message);}
    public static void Run()
    {
        NativeReplayChecks.Run();
        var cameraObject=new GameObject("WeatherEffectsCheckCamera");
        var camera=cameraObject.AddComponent<Camera>();cameraObject.tag="MainCamera";
        camera.orthographic=true;camera.orthographicSize=10;camera.aspect=2;camera.transform.position=new Vector3(0,2,0);
        var go=new GameObject("WeatherEffectsCheck");
        GameObject playerObject=null;
        try
        {
            var map=new FieldMap();map.footholds.Add(new FieldFoothold{x1=-10000,x2=10000,y1=0,y2=0});
            var ground=go.AddComponent<NativeWeatherGround>();ground.Bind(map);
            long now=1000;
            for(int i=0;i<10;i++)ground.Tick(2,90000,0,now+=100,Color.white,Color.white);
            Require(ground.SnowLevel==1&&ground.DepositCount>0&&ground.DepositCount<=70,"aged snow seeds native deposits");
            foreach(var image in go.GetComponentsInChildren<SpriteRenderer>())
                Require(image.sprite!=null&&image.transform.position.y<=0,"snow placed on real foothold with surface offset");
            var deposit=go.GetComponentInChildren<SpriteRenderer>();
            playerObject=new GameObject("WeatherFootprintCheck");
            var player=playerObject.AddComponent<PlayerCtrl>();player.worldX=deposit.transform.position.x;
            player.terrainY=-player.DepthOffset; // Place the test's feet on the synthetic y=0 foothold.
            ground.Tick(2,90000,0,now+=100,Color.white,Color.white);
            Require(ground.FootprintCount==1,"landing on actual snow sprite leaves original footprint");
            ground.Tick(2,90000,0,now+=100,Color.white,Color.white);
            Require(ground.FootprintCount==1,"standing still does not emit footprints");
            UnityEngine.Object.DestroyImmediate(playerObject);playerObject=null;
            for(int i=0;i<260;i++)ground.Tick(0,0,0,now+=100,Color.white,Color.white);
            Require(ground.DepositCount==0&&ground.SnowLevel==0,"25 second snow melt");
            for(int i=0;i<40;i++)ground.Tick(1,75000,1,now+=100,Color.white,Color.white);
            Require(ground.PuddleCount>0&&ground.PuddleCount<=26,"aged rain seeds puddles");
            bool wide=false;
            foreach(var image in go.GetComponentsInChildren<SpriteRenderer>())if(image.gameObject.name.Contains("WeatherPuddle.img/2/"))wide=true;
            Require(wide,"oldest fitting puddles widen to tier 2");
            for(int i=0;i<410;i++)ground.Tick(0,0,0,now+=100,Color.white,Color.white);
            Require(ground.PuddleCount==0,"40 second puddle dry");
            ground.Clear();Require(go.GetComponentsInChildren<NativeImgAnimation>().Length==0,"ground teardown clears every owned layer");
            var particles=go.AddComponent<NativeWeatherParticles>();
            int[] skies={5,6,7,8},items={5120997,5120998,5120999,5120996},counts={280,100,100,1260};
            for(int i=0;i<skies.Length;i++)
            {
                particles.Tick(items[i],skies[i],123,1,Color.white);
                Require(particles.Count>=counts[i]*30/100&&particles.Count<=counts[i]*54/100,"native random count "+skies[i]);
                particles.Tick(items[i],skies[i],123,3,Color.white);
                foreach(var image in go.GetComponentsInChildren<SpriteRenderer>())Require(image.sprite!=null&&float.IsFinite(image.transform.position.x),"native particle frame/position");
            }
            particles.Tick(0,0,124,4,Color.white);Require(particles.Count==0,"clear sky removes particles");
            particles.Tick(5120996,8,125,5,Color.white);particles.Clear();
            Require(particles.Count==0,"particle teardown cleanup");
            Require(AudioManager.Has("Weather/rain")&&AudioManager.Has("Weather/wind"),"original weather loops indexed");
            foreach(int id in new[]{13121015,13121016,13121017,13121018,4221014,4221015,4221016,4221017,3121033})
                Require(WzAssets.Frames(WzAssets.Node("Skill/"+(id/10000).ToString("D3")+".img/skill/"+id+"/hit/0")).Count>0,"followup native hit frames "+id);
            for(int id=15121022;id<=15121033;id++)
                Require(WzAssets.Node("Skill/1512.img/skill/"+id+"/action").Text("0")=="alert5","spear original numeric action "+id);
            Debug.Log("WEATHER_EFFECTS_PASS snow_seed_melt snow_footprint_idle_gate puddle_seed_promote_dry particle_profiles=4 native_count frame_position cleanup audio_index followup_hits spear_actions=12");
        }
        finally{if(playerObject!=null)UnityEngine.Object.DestroyImmediate(playerObject);UnityEngine.Object.DestroyImmediate(go);UnityEngine.Object.DestroyImmediate(cameraObject);}
    }
}
