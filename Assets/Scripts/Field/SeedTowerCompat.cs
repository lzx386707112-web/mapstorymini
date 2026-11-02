using System;
using MapleDash.Js;
using UnityEngine;

// org.gms.server.life.SeedTowerCompat 的移植层。
// 源端这个类的定位不是「玩法实现」，而是一组适配器：把脚本没有能力直接做的三件事
// （客户端上报的移动、贴图层的位移广播、反伤 buff）接回真正的执行点，规则本身仍然只在
// event/SeedTower20.js 里。移植端保持同样的分工，四个服务端钩子分别落在：
//   handleAttack      -> SkillSystem.HitAt / PlayerCtrl.HitEnemy / SwingEmpty
//   protectsTarget    -> EnemyUnit.TakeHit / TakeReplayHit
//   controls          -> EnemyUnit.ScriptControlled（本地 AI 冻结）
//   allowReactorHit   -> MapReactorService.HitPlayer
public static class SeedTowerCompat
{
    public const int TowerMapBase = 992000000;
    // 第 14 层的射击靶（黄/红蝙蝠菇）。
    public const int YellowBat = 9309116, RedBat = 9309117;
    // 第 8/13 层的火柱与落石「道具怪」，玩家伤害不结算。
    public const int HazardFirst = 9309400, HazardLast = 9309402;

    static Func<JsValue> Player => () => NpcApi.BuildPlayer(NpcSession.MapContext());

    // 源端 instance(player)：eim != null && "1".equals(eim.getProperty("seedTower"))。
    static NativeEventScriptService.Instance Instance()
    {
        var instance = NativeEventScripts.CurrentInstance();
        return instance != null && instance.Property("seedTower") == "1" ? instance : null;
    }

    static int Number(NativeEventScriptService.Instance instance, string key)
        => instance != null && int.TryParse(instance.Property(key), out int value) ? value : 0;

    // Java.type("org.gms.server.life.SeedTowerCompat")
    public static JsValue Resolve(string name)
        => name == "org.gms.server.life.SeedTowerCompat" ? Type() : null;

    static JsValue Type()
    {
        var type = new JsHostObject(null);
        // 源端还有 handleAttack/protectsTarget/controls/allowReactorHit，但那四个是
        // 服务端内部调用的静态方法，脚本面并不存在；这里只有脚本真正会调的两个。
        type.Missing = key => throw new JsRuntimeException("Unmigrated SeedTowerCompat API: " + key);
        type.Set("move", JsFunction.FromNative("move", c =>
        {
            var mob = NativeEventMap.FindByHost(c.Arg(0))
                ?? throw new JsRuntimeException("SeedTowerCompat.move requires an event monster");
            mob.ScriptMove((float)JsConvert.ToNumber(c.Arg(1)), (float)JsConvert.ToNumber(c.Arg(2)),
                JsConvert.ToBoolean(c.ArgCount > 4 ? c.Arg(4) : JsBoolean.False));
            return JsUndefined.Value;
        }));
        type.Set("spiderWeb", JsFunction.FromNative("spiderWeb", c =>
        {
            var mob = NativeEventMap.FindByHost(c.Arg(0));
            mob?.Visible?.NativeController?.ScriptReflect(4000);
            return JsUndefined.Value;
        }));
        return type;
    }

    // ————— 服务端钩子 —————

    // MapleMap.damageMonster:1508。判据全在源端：第 14 层只保护两种蝙蝠；
    // 第 8/13 层保护 9309400..9309402 的火柱/落石。
    public static bool ProtectsTarget(EnemyUnit mob)
        => mob != null && mob.fieldMode && ProtectsTarget(Profile.LastMapId, mob.fieldMobId);

    // 同上，纯规则版：编辑器回归在没有 EnemyUnit 的环境里也能验证这张表。
    public static bool ProtectsTarget(int mapId, int mobId)
    {
        if (Instance() == null) return false;
        if (mapId == TowerMapBase + 14000) return mobId == YellowBat || mobId == RedBat;
        if (mapId == TowerMapBase + 8000 || mapId == TowerMapBase + 13000)
            return mobId >= HazardFirst && mobId <= HazardLast;
        return false;
    }

    // RangedAttackHandler:1352 / MagicDamageHandler:672 / CloseRangeDamageHandler:1324。
    // 返回 true 表示「这次攻击已经被脚本接管」：源端直接 return，一行伤害都不结算。
    // 第 14 层就是靠这个把普攻变成「射击判定」，命中与否都由 seedAttack 记账。
    public static bool HandleAttack(int skillId, EnemyUnit target)
    {
        int oid = 0;
        if (skillId == 0 && target != null && !target.dead)
        {
            int id = target.fieldMobId;
            if (id == YellowBat || id == RedBat)
            {
                var mob = NativeEventMap.FindByVisible(target);
                if (mob != null) oid = mob.ObjectId;
            }
        }
        return HandleAttack(skillId, oid);
    }

    // 纯规则版：oid=0 表示空挥（没打到蝙蝠），其它为被命中的蝙蝠 oid。
    public static bool HandleAttack(int skillId, int oid)
    {
        var instance = Instance();
        if (instance == null || Number(instance, "floor") != 14) return false;
        if (Profile.LastMapId != TowerMapBase + 14000) return false;
        instance.Invoke("seedAttack", instance.Host, Player(), new JsNumber(skillId), new JsNumber(oid));
        return true;
    }

    // MoveLifeHandler:75 的 controls()：脚本登记过的怪，位移只由脚本决定。
    public static bool Controls(NativeEventMap.Mob mob) => mob != null && mob.Controlled;

    // Reactor.hitReactor:384。第 18 层靠 seedReactorHit 做 500ms 节流与重生锁；
    // 第 2 层的四块卡牌石碑则完全禁止直接打（只能按上提交卡牌）。
    public static bool AllowReactorHit(Server.Field.MapReactor reactor)
    {
        var instance = Instance();
        if (instance == null || reactor == null) return true;
        int map = Profile.LastMapId;
        if (map == TowerMapBase + 2000 && reactor.id >= 9260004 && reactor.id <= 9260007) return false;
        if (map != TowerMapBase + 18000) return true;
        try
        {
            var result = instance.Invoke("seedReactorHit", instance.Host, Player(), ReactorValue(reactor));
            return result is JsBoolean flag && flag.Value;
        }
        catch (Exception error)
        {
            Debug.LogError("[SeedTower] reactor hook: " + error.Message);
            return false;
        }
    }

    // seedReactorHit 只读 getObjectId/getId/getState/getPosition；这里给一个最小宿主即可，
    // 事件地图自己的 ReactorHost 带 Scope 依赖，脚本面走的也是那一份。
    static JsValue ReactorValue(Server.Field.MapReactor reactor)
    {
        var map = NativeEventScripts.ResolveMap(Profile.LastMapId);
        if (map != null)
        {
            var host = map.ReactorFor(reactor);
            if (host != null) return host;
        }
        var fallback = new JsHostObject(reactor);
        fallback.Set("getId", JsFunction.FromNative("getId", _ => new JsNumber(reactor.id)));
        fallback.Set("getObjectId", JsFunction.FromNative("getObjectId", _ => new JsNumber(reactor.objectId)));
        fallback.Set("getState", JsFunction.FromNative("getState", _ => new JsNumber(reactor.state)));
        fallback.Set("getPosition", JsFunction.FromNative("getPosition",
            _ => NpcApi.BuildPoint(reactor.x, reactor.y)));
        return fallback;
    }
}
