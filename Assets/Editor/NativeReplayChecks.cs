using System;
using UnityEngine;
public static class NativeReplayChecks
{
    public static void Run()
    {
        int casts = 0, stages = 0, ticks = 0;
        var root = (System.Collections.Generic.Dictionary<string,object>)MiniJson.Parse(Resources.Load<TextAsset>("NativeSkillReplays").text);
        foreach (string key in root.Keys)
        {
            int id = int.Parse(key); casts++;
            Require(SkillBook.Native(id,1) != null,"cast WZ " + id);
            foreach (var row in NativeSkillReplay.Get(id))
            {
                Require(SkillBook.Native(row.Skill,1) != null,"replay WZ " + row.Skill);
                int last = -1;
                foreach (int at in row.Times) { Require(at >= last,"nonmonotonic timeline"); last = at; ticks++; }
                stages++;
            }
        }
        var a = NativeSkillReplay.Adapt(new[] {new DamageResult(3,true),new DamageResult(8,false)},3,2,1);
        Require(a.Length==3 && a[0].Amount==2 && a[1].Amount==4 && a[2].Critical,"round/cycle/critical");
        var cap = NativeSkillReplay.Adapt(new[] {new DamageResult(19999999,true)},1,1,100);
        Require(cap[0].Amount==19999999 && cap[0].Critical,"cap");
        var miss = WzAssets.Node("Effect/BasicEff.img/NoRed0/Miss");
        Require(miss != null && WzAssets.Canvas(miss).Width>0,"original Miss Canvas");
        Require(WzAssets.Node("Map/Effect.img/customSkill/dawnWarrior/galaxyStarBurstVideoLayer/0")!=null,"skillback marker");
        var enemyObject=new GameObject("ReplayHpCheck");
        try
        {
            var enemy=enemyObject.AddComponent<EnemyUnit>();enemy.hp=100;
            enemy.TakeReplayHit(new[]{new DamageResult(2,false),new DamageResult(3,false),new DamageResult(4,false)},1,false,true,120);
            Require(enemy.hp==91,"all replay lines settle HP immediately");
            Require(UnityEngine.Object.FindObjectsOfType<NativeDamageLines>().Length==1,"later indexed lines pending");
            enemy.TakeReplayHit(new[]{new DamageResult(2,true),new DamageResult(3,false)},1,false,false,120);
            Require(enemy.hp==86 && UnityEngine.Object.FindObjectsOfType<NativeDamageLines>().Length==2,"native attack numbers retain individual lines");
            enemy.TakeReplayHit(new[]{new DamageResult(2,true),new DamageResult(3,false)},1,false,false,120,true);
            Require(enemy.hp==81 && UnityEngine.Object.FindObjectsOfType<NativeDamageLines>().Length==2,"explicit TOTAL emits one immediate number");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(enemyObject);
            foreach(var line in UnityEngine.Object.FindObjectsOfType<NativeDamageLines>())UnityEngine.Object.DestroyImmediate(line.gameObject);
            foreach(var line in UnityEngine.Object.FindObjectsOfType<NativeDamageNumber>())UnityEngine.Object.DestroyImmediate(line.gameObject);
        }
        int weatherMaps=0;
        var data=NativeWeatherField.Data;bool previous=NativeWeatherField.ClientEnabled;
        NativeWeatherField.ClientEnabled=true;
        try
        {
            foreach(int id in new[]{100000000,101000000,230000000})
            {
                var go=new GameObject("WeatherCheck");
                try
                {
                    var view=go.AddComponent<NativeMapView>();view.Show(id);
                    var weather=go.GetComponent<NativeWeatherField>();
                    Require(weather.Active,"weather outdoor/underwater " + id);
                    Require(weather.HasSky==(id!=230000000),"underwater no falling sky");
                    weather.State.Set(720,10000,2,1,0,0,0,26,0,1,0);weather.Tick();
                    weatherMaps++;
                }
                finally {UnityEngine.Object.DestroyImmediate(go);}
            }
        }
        finally{NativeWeatherField.ClientEnabled=previous;}
        Debug.Log("WEATHER_IMG_PASS maps="+weatherMaps+" original_sheets=21 sky_classification");
        Debug.Log("NATIVE_REPLAY_PASS casts="+casts+" stages="+stages+" ticks="+ticks+" WZ Miss marker template");
    }
    static void Require(bool ok,string message) { if(!ok) throw new Exception(message); }
}
