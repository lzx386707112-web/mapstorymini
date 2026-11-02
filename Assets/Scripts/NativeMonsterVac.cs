using UnityEngine;
using System.Collections.Generic;

// Character.startMonsterVac / MapleMap.pullMonstersTo / MonsterVacCompat.
public sealed class NativeMonsterVac : MonoBehaviour
{
    double expires, nextPull;
    int mapId;
    Vector3 target;
    readonly List<EnemyUnit> released = new List<EnemyUnit>();
    readonly Dictionary<EnemyUnit,NativeImgAnimation> locked = new Dictionary<EnemyUnit,NativeImgAnimation>();
    public bool Active => expires > NativeBuffs.Now;
    public static NativeMonsterVac For(PlayerCtrl player) => player.GetComponent<NativeMonsterVac>() ?? player.gameObject.AddComponent<NativeMonsterVac>();
    public bool StartVac()
    {
        if (Active) { BottomBar.Toast("怪物吸星大法仍在生效中，无法叠加使用。"); return false; }
        var field = GameLoop.I?.field;
        var player = GetComponent<PlayerCtrl>();
        if (field?.Map == null || player == null) return false;
        target = player.FeetPos;
        target.y = PlayerCtrl.GroundAt(target.x, target.y) + 1f / Art.PPU;
        mapId = field.Map.id;
        int minutes = GameServer.Local == null ? 10 : GameConfigDao.GetInt(GameServer.Local.Db, "monster_vac_duration_minutes", 10);
        if (minutes <= 0) minutes = 10;
        int duration = minutes * 60000;
        expires = NativeBuffs.Now + duration / 1000.0;
        nextPull = NativeBuffs.Now + .1;
        NativeBuffs.For(player).ApplyItem(2431158, new Dictionary<string,int> { { "WDEF", NativeItemSource.Node(2431158).Get("spec").Int("pdd") } }, duration, NativeBuffs.Now);
        BottomBar.Toast("怪物吸星大法生效，持续" + minutes + "分钟。吸怪点已固定，不会跟随移动。");
        return true;
    }
    public void StopVac()
    {
        expires = 0;
        foreach (var entry in locked)
        {
            if (entry.Key != null) entry.Key.VacuumLocked = false;
            if (entry.Value != null) entry.Value.Remove();
        }
        locked.Clear();
        GetComponent<NativeBuffs>()?.Cancel(-2431158);
    }
    // MonsterVacCompat.stepToward uses integer WZ pixels and Java Math.round.
    internal static Vector3 StepToward(Vector3 from, Vector3 to)
    {
        double dx = (to.x-from.x)*Art.PPU, dy = -(to.y-from.y)*Art.PPU;
        double distance = System.Math.Sqrt(dx*dx+dy*dy);
        if (distance <= 150) return to;
        return from + new Vector3((float)System.Math.Floor(dx*150/distance+.5)/Art.PPU,
            -(float)System.Math.Floor(dy*150/distance+.5)/Art.PPU,0);
    }
    void OnDestroy() => StopVac();
    void Update()
    {
        if (expires == 0) return;
        if (!Active || GameLoop.I?.field?.Map?.id != mapId) { StopVac(); return; }
        if (NativeBuffs.Now < nextPull) return;
        nextPull = NativeBuffs.Now + .1;
        bool stun = GameServer.Local == null || GameConfigDao.GetBool(GameServer.Local.Db, "monster_vac_stun_enabled", true);
        released.Clear();
        foreach (var entry in locked)
            if (entry.Key == null || entry.Key.dead) released.Add(entry.Key);
        foreach (var mob in released)
        {
            if (locked[mob] != null) locked[mob].Remove();
            if (mob != null) mob.VacuumLocked = false;
            locked.Remove(mob);
        }
        foreach (var mob in EnemyUnit.All)
        {
            if (mob == null || !mob.fieldMode || mob.dead || mob.IsBoss) continue;
            var from = mob.VacuumPosition;
            if ((from-target).sqrMagnitude > 64f/(Art.PPU*Art.PPU))
                mob.MoveVac(StepToward(from,target));
            mob.VacuumLocked = true;
            if (!locked.ContainsKey(mob)) locked[mob] = null;
            if (stun && locked[mob] == null)
                locked[mob] = NativeImgAnimation.Spawn("Skill/111.img/skill/1111008/mob", mob.transform, mob.DamageAnchor, mob.BodySortingOrder+1, repeat:true, lifetime:2.5f);
            else if (!stun && locked[mob] != null) { locked[mob].Remove(); locked[mob] = null; }
        }
    }
}
