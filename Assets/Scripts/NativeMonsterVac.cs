using UnityEngine;
using System.Collections.Generic;

// Character.startMonsterVac / MapleMap.pullMonstersTo / MonsterVacCompat.
public sealed class NativeMonsterVac : MonoBehaviour
{
    double expires, nextPull;
    int mapId;
    Vector3 target;
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
    void OnDestroy() => StopVac();
    void Update()
    {
        if (expires == 0) return;
        if (!Active || GameLoop.I?.field?.Map?.id != mapId) { StopVac(); return; }
        if (NativeBuffs.Now < nextPull) return;
        nextPull = NativeBuffs.Now + .1;
        bool stun = GameServer.Local == null || GameConfigDao.GetBool(GameServer.Local.Db, "monster_vac_stun_enabled", true);
        foreach (var mob in EnemyUnit.All)
        {
            if (mob == null || !mob.fieldMode || mob.dead || mob.IsBoss) continue;
            var from = mob.transform.position;
            if ((from-target).sqrMagnitude > 64f/(Art.PPU*Art.PPU))
                mob.MoveVac(Vector3.MoveTowards(from,target,150f/Art.PPU));
            mob.VacuumLocked = true;
            if (!locked.ContainsKey(mob)) locked[mob] = null;
            if (stun && locked[mob] == null)
                locked[mob] = NativeImgAnimation.Spawn("Skill/111.img/skill/1111008/mob", mob.transform, mob.DamageAnchor, mob.BodySortingOrder+1, repeat:true, lifetime:2.5f);
            else if (!stun && locked[mob] != null) { locked[mob].Remove(); locked[mob] = null; }
        }
    }
}
