using System;
using System.Reflection;
using UnityEngine;
using Server.Field;

public static class FieldFeedbackChecks
{
    static void Require(bool condition,string message) {if(!condition)throw new Exception(message);}
    public static void Run()
    {
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        var go=new GameObject("FieldFeedbackChecks");
        try
        {
            var field=go.AddComponent<FieldDirector>();
            var map=FieldCatalog.GetMap(100000005);
            Require(map.spawns.Exists(s=>s.mob==6130101),"original wild boss spawn exists");
            typeof(FieldDirector).GetProperty("Map").SetValue(field,map);
            void Call(string name,params object[] args)=>typeof(FieldDirector).GetMethod(name,flags).Invoke(field,args);
            void DetachViews()
            {
                foreach(var mob in UnityEngine.Object.FindObjectsOfType<EnemyUnit>())
                    UnityEngine.Object.DestroyImmediate(mob.gameObject);
            }
            Call("BuildPoints",map);
            EnemyUnit Boss()
            {
                foreach(var mob in UnityEngine.Object.FindObjectsOfType<EnemyUnit>())
                    if(mob.fieldMobId==6130101 && !mob.dead)return mob;
                return null;
            }
            var boss=Boss();Require(boss!=null,"original wild boss actually spawned");
            long remaining=boss.NativeController.MaximumHp-100;
            boss.NativeController.RestoreFieldHp(remaining);
            Call("PreserveSpawnState"); DetachViews(); Call("BuildPoints",map);
            boss=Boss();Require(boss!=null && boss.NativeController.CurrentHp==remaining,"map reentry preserves live boss HP");
            var points=(System.Collections.IList)typeof(FieldDirector).GetField("points",flags).GetValue(field);
            object point=points[0];
            var spawn=(SpawnPoint)point.GetType().GetField("sp").GetValue(point);
            Require(spawn.Spawned==1,"view reattachment cannot duplicate spawn occupancy");
            spawn.OnKilled(100,(long)(Time.realtimeSinceStartupAsDouble*1000)); boss.dead=true;
            Call("PreserveSpawnState"); DetachViews(); Call("BuildPoints",map);
            Require(Boss()==null && !spawn.ShouldSpawn((long)(Time.realtimeSinceStartupAsDouble*1000)),"boss cooldown survives teleport away and back");
            field.AddScriptMonsterSpawn(6130101,-649,204,7200,-1);
            Require(Boss()!=null,"script addMonsterSpawn creates boss through shared scheduler");
            Call("PreserveSpawnState");DetachViews();Call("BuildPoints",map);
            Require(Boss()!=null,"script spawn survives cached script registration on reentry");
            DetachViews();
            Debug.Log("FIELD_FEEDBACK_PASS wild_boss_initial_spawn live_hp_reentry cooldown_reentry script_spawn_reentry");
        }
        finally {UnityEngine.Object.DestroyImmediate(go);}
    }
}
