using System;
using System.Collections.Generic;
using UnityEngine;

// Field-owned task queue: never Invoke on a dying monster (revive is delayed
// until die1 completes). Clear invalidates every task, including phase transitions.
public sealed class NativeBossEncounter : MonoBehaviour
{
    readonly NativeBossTimeline tasks = new NativeBossTimeline();
    FieldDirector field;
    NativeLucidEncounter lucid;
    public void Bind(FieldDirector owner) { field = owner; }
    public void Schedule(EnemyUnit source, long delay, Action action, bool afterDeath = false)
        => tasks.Schedule(NativeMobController.Now,delay,
            () => afterDeath || (source != null && !source.dead && source.isActiveAndEnabled), action);
    void Update()
    {
        if (field == null || field.Map == null || GameLoop.I == null ||
            GameLoop.I.state != GameLoop.GameState.Playing || PlayerCtrl.InputLocked || Time.deltaTime <= 0) return;
        tasks.Tick(NativeMobController.Now);
        lucid?.Tick();
    }
    public void Clear() { tasks.Clear(); lucid = null; NativeSkillVideo.Stop(NativeSkillVideo.Channel.BossScene); }
    public void OnSpawn(EnemyUnit mob)
    {
        if (NativeBossRules.IsLucid(mob.fieldMobId))
            lucid = new NativeLucidEncounter(this,field,mob);
        if (mob.fieldMobId >= 8810118 && mob.fieldMobId <= 8810122)
        {
            for (int id = 8810010; id <= 8810017; id++) Remove(id);
            int[] parts = {8810107,8810102,8810103,8810104,8810105,8810106,8810108,8810109};
            foreach (int id in parts) if (Count(id) == 0) field.SpawnNativeMob(id,mob.transform.position);
        }
        if (mob.fieldMobId >= 8810010 && mob.fieldMobId <= 8810017)
        {
            for (int id = 8810010; id <= 8810017; id++) if (Count(id) == 0) return;
            foreach (var e in new List<EnemyUnit>(EnemyUnit.All))
                if (e.fieldMobId == 8810018 || (e.fieldMobId >= 8810118 && e.fieldMobId <= 8810122)) e.DefeatNative();
            for (int id = 8810010; id <= 8810017; id++) Remove(id);
        }
    }
    public void OnDamaged(EnemyUnit mob, int amount)
    {
        // Horntail body is a shared HP gauge; direct body damage is restored.
        int id = mob.fieldMobId;
        if (id == 8810018 || (id >= 8810118 && id <= 8810122)) { mob.NativeController?.Heal(amount); return; }
        bool normal = id >= 8810002 && id <= 8810009, chaos = id >= 8810102 && id <= 8810109;
        if (!normal && !chaos) return;
        foreach (var e in EnemyUnit.All)
            if (e.fieldMode && !e.dead && (normal ? e.fieldMobId == 8810018 : e.fieldMobId >= 8810118 && e.fieldMobId <= 8810122))
                e.NativeController?.FakeDamage(amount);
    }
    public void OnKilled(EnemyUnit mob) { lucid?.OnKilled(mob); }
    public bool BlocksDamage(EnemyUnit mob)
    {
        int id = mob.fieldMobId;
        // MapleMap's Zakum body remains invulnerable until every arm is gone.
        if (id != 8800000 && id != 8800100) return false;
        int first = id + 3;
        foreach (var e in EnemyUnit.All)
            if (e.fieldMode && !e.dead && e.fieldMobId >= first && e.fieldMobId <= first + 7) return true;
        return false;
    }
    public void Effect(string path)
    {
        if (string.IsNullOrEmpty(path)) return;
        if (NativeBossVideoEffect.Handle(path)) return;
        Debug.Log("[BossEffect] " + path);
    }
    public void Damage(EnemyUnit source, int percent, Rect? absolute = null)
    {
        var p = GameLoop.I?.player;
        if (p == null || p.hp <= 0 || (absolute.HasValue && !absolute.Value.Contains(NativeMobController.Pixels(p.FeetPos)))) return;
        p.TakeBossDamage(NativeBossRules.PercentDamage((int)p.hpMax,percent));
    }
    public void Remove(int id)
    {
        foreach (var e in new List<EnemyUnit>(EnemyUnit.All))
            if (e.fieldMode && e.fieldMobId == id) { e.dead = true; e.gameObject.SetActive(false); Destroy(e.gameObject); }
    }
    public int Count(int id)
    {
        int count = 0;
        foreach (var e in EnemyUnit.All) if (e.fieldMode && !e.dead && e.fieldMobId == id) count++;
        return count;
    }
    public EnemyUnit Spawn(int id, Vector2 pixels, bool ground = true)
        => field.SpawnNativeMob(id,new Vector3(pixels.x / Art.PPU,-pixels.y / Art.PPU,0),ground,!ground);
}

// Ported from LucidBossCompat. Coordinates/timers are source values, not stage JSON.
public sealed partial class NativeLucidEncounter
{
    readonly NativeBossEncounter owner;
    readonly FieldDirector field;
    readonly EnemyUnit boss;
    readonly int phase;
    long butterfly, dust, summon, dragon, laser, shoot, rush, glass, hurdle, bomb, fury;
    int gauge, positionIndex, glassIndex;
    bool failed, stopped;
    long Now => NativeMobController.Now;
    static readonly int[] Support = {8880164,8880161,8880165,8880171,8880175};
    public NativeLucidEncounter(NativeBossEncounter owner, FieldDirector field, EnemyUnit boss)
    {
        this.owner = owner; this.field = field; this.boss = boss;
        phase = boss.fieldMobId == 8880140 ? 1 : boss.fieldMobId == 8880141 ? 2 : 3;
        butterfly = Now + 5000; dust = Now + 9000; summon = Now + 7000;
        dragon = Now + (phase == 1 ? 20000 : 28000); laser = Now + 12000;
        shoot = Now + 25000; rush = Now + 18000; glass = Now + 10000; hurdle = Now + 1000;
        bomb = Now + (phase == 1 ? 12000 : 15000); fury = Now + 45000;
        if (phase == 3)
        {
            foreach (int id in Support) owner.Remove(id);
            owner.Effect("customSkill/lucid/furyVideoLayer");
            BottomBar.Toast("露希德已暴怒：45秒后噩梦迷雾将降临");
        }
    }
    void Schedule(int delay, Action action) => owner.Schedule(boss,delay,() => { if (!stopped) action(); });
    void Hit(int delay, int percent, Rect? range = null) => Schedule(delay,() => owner.Damage(boss,percent,range));
    public void OnKilled(EnemyUnit e)
    {
        if (e != boss || failed) return;
        stopped = true;
        if (phase == 3) owner.Effect("customSkill/lucid/furyStopVideoLayer");
    }
    public void Tick()
    {
        if (stopped || failed || boss == null || boss.dead) return;
        if (phase == 3) { if (Now >= fury) FailFury(); return; }
        if (Now >= butterfly) Butterflies();
        if (Now >= dust)
        {
            Vector2 p = NativeMobController.Pixels(boss.transform.position);
            owner.Damage(boss,10,new Rect(p.x-600,p.y-500,1200,650));
            dust = Now + (phase == 1 ? 12000 : 10000);
        }
        if (Now >= summon)
        {
            Nightmares(); summon = Now + (phase == 1 ? 15000 : 18000);
        }
        if (Now >= dragon)
        {
            owner.Effect("customSkill/lucid/dragonP" + phase + "VideoLayer"); Hit(6300,100);
            dragon = Now + (phase == 1 ? 35000 : 38000);
        }
        if (Now >= bomb)
        {
            owner.Effect("customSkill/lucid/bombVideoLayer");
            Schedule(3000,() => {
                var player = GameLoop.I?.player;
                if (player == null || player.hp <= 0) return;
                var p = NativeMobController.Pixels(player.FeetPos);
                owner.Damage(boss,35,new Rect(p.x-250,p.y-250,500,500));
            });
            bomb = Now + 25000;
        }
        if (Now >= hurdle)
        {
            foreach (var e in EnemyUnit.All)
            {
                if (!e.fieldMode || e.dead || e.fieldMobId != (phase == 1 ? 8880161 : 8880171)) continue;
                var p = NativeMobController.Pixels(e.transform.position);
                var player = GameLoop.I?.player;
                var r = new Rect(p.x-20,p.y-500,40,510);
                if (player != null && r.Contains(NativeMobController.Pixels(player.FeetPos))) { owner.Damage(boss,20,r); break; }
            }
            hurdle = Now + 1000;
        }
        if (phase != 2) return;
        if (Now >= laser)
        {
            owner.Effect("customSkill/lucid/laserRainVideoLayer"); Hit(1260,18); Hit(3000,18);
            Schedule(3000,() => { if (owner.Count(8880171) < 15) owner.Spawn(8880171,GroundPoint()); });
            laser = Now + 30000;
        }
        if (Now >= shoot)
        {
            owner.Effect("customSkill/lucid/phantomBarrageVideoLayer");
            for (int i = 0; i < 12; i++) Hit(2400 + i * 1000,10);
            shoot = Now + 45000;
        }
        if (Now >= rush) { Rush(); rush = Now + 20000; }
        if (Now >= glass)
        {
            int index = glassIndex++ % GlassAreas.Length;
            var area = GlassAreas[index];
            owner.Effect("customSkill/lucid/stainedGlass" + (index == 0 ? "" : index.ToString()) + "VideoLayer");
            Hit(1260,20,area);
            Schedule(3000,() => { if (owner.Count(8880171) < 15) owner.Spawn(8880171,new Vector2(area.center.x,area.y)); });
            glass = Now + 10000;
        }
    }
    void Butterflies()
    {
        int hp = Mathf.Clamp(Mathf.CeilToInt(boss.hp / boss.hpMax * 100),1,100);
        int count = Math.Min(NativeBossRules.ButterflyCount(hp),40-gauge);
        gauge += count;
        int id = phase == 1 ? 8880165 : 8880175;
        var positions = phase == 1 ? ButterflyP1 : ButterflyP2;
        int visible = Math.Min(count,40-owner.Count(id));
        for (int i = 0; i < visible; i++) owner.Spawn(id,positions[positionIndex++ % positions.Length],false);
        if (gauge >= 40)
        {
            gauge = 0; owner.Remove(8880165); owner.Remove(8880175);
            owner.Effect("customSkill/lucid/butterflyBurstVideoLayer"); Hit(1800,30);
        }
        butterfly = Now + NativeBossRules.ButterflyInterval(hp);
    }
    Vector2 GroundPoint()
    {
        int[] xs = phase == 1 ? new[] {220,520,820,1120,1420,1720} : new[] {0,250,500,750,1000,1250};
        return new Vector2(xs[UnityEngine.Random.Range(0,xs.Length)],phase == 1 ? 0 : 380);
    }
    void Nightmares()
    {
        if (phase == 1 && owner.Count(8880164) < 4) owner.Spawn(8880164,GroundPoint());
        int id = phase == 1 ? 8880161 : 8880171;
        int count = Math.Min(phase == 1 ? 1 : 2,15-owner.Count(id));
        for (int i = 0; i < count; i++) owner.Spawn(id,GroundPoint());
    }
    void Rush()
    {
        owner.Effect("customSkill/lucid/rushVideoLayer");
        bool hit = false;
        for (int t = 0; t <= 3000; t += 100)
        {
            int time = t;
            Schedule(t,() => {
                if (hit) return;
                Vector2 p = RushPosition(time);
                var player = GameLoop.I?.player;
                if (player != null && new Rect(p.x-47,p.y-135,123,149).Contains(NativeMobController.Pixels(player.FeetPos)))
                { hit = true; owner.Damage(boss,20); }
            });
        }
    }
    static Vector2 RushPosition(int elapsed)
    {
        float total = 0;
        for (int i = 1; i < RushPath.Length; i++) total += Vector2.Distance(RushPath[i-1],RushPath[i]) / RushSpeeds[i];
        float target = Mathf.Clamp(elapsed,0,3000) * total / 3000, consumed = 0;
        for (int i = 1; i < RushPath.Length; i++)
        {
            float weight = Vector2.Distance(RushPath[i-1],RushPath[i]) / RushSpeeds[i];
            if (target <= consumed + weight || i == RushPath.Length-1)
                return Vector2.Lerp(RushPath[i-1],RushPath[i],weight == 0 ? 1 : (target-consumed)/weight);
            consumed += weight;
        }
        return RushPath[RushPath.Length-1];
    }
    void FailFury()
    {
        failed = true;
        Vector3 pos = boss.transform.position;
        owner.Remove(8880152);
        var fail = field.SpawnNativeMob(8880154,pos,false,true);
        fail?.NativeController?.BlockFor(10000);
        owner.Schedule(fail,3000,() => owner.Damage(fail,100));
        owner.Schedule(fail,10000,() => {
            owner.Remove(8880154);
            var p2 = owner.Spawn(8880141,new Vector2(711,-786),false);
            if (p2 != null) p2.NativeController?.SetHpPercent(16);
        });
    }
}
