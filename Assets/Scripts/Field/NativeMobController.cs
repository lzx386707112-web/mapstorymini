using System;
using System.Collections.Generic;
using MapleDash.Wz;
using UnityEngine;

// WZ-backed Monster/MobSkill runtime. All scheduled effects belong to the field;
// destroying a caster or leaving the field cancels its outstanding attacks.
public sealed class NativeMobController : MonoBehaviour
{
    sealed class Cast
    {
        public int id, level, action, mp, hp, x, y, count, limit, probability;
        public long duration, cooldown;
        // LifeFactory:601-604 sums Mob/<id>.img/skill<action> frame delays with a 0
        // default and no abs(). That single number is both the server's apply delay
        // (MoveLifeHandler:135-141) and its animation lock (Monster:349-351), so one
        // field drives both; AnimationMillis() stays for playback only.
        public int serverDelay;
        // Parsed for completeness only: MobSkill/applyEffect never reads effectAfter,
        // so there is nothing source-backed to do with it yet.
        public int effectAfter;
        // True only for rows loaded from the mob's own info/skill, i.e. something the
        // mob really cast. Attack-borne diseases reuse this struct with level taken
        // from attack<N>/info, and must not pull a MobSkill canvas by accident.
        public bool cast;
        public Rect? range;
        public readonly List<int> summons = new List<int>();
        public string Key => id + ":" + level;
    }
    sealed class BuffValue { public int value; public long until; }
    readonly List<Cast> skills = new List<Cast>();
    readonly Dictionary<int, BuffValue> buffs = new Dictionary<int, BuffValue>();
    readonly List<EnemyUnit> summoned = new List<EnemyUnit>();
    EnemyUnit mob;
    FieldDirector field;
    NativeBossEncounter encounter;
    NativeMobCastState state;
    NativeMobHealth health;
    ImgNode root, artRoot;
    NativeMovementPhysics motionPhysics;
    readonly NativeMobPursuit pursuit=new NativeMobPursuit();
    long pursuitTick;
    bool flightWaypoint;
    Vector2 waypoint;
    int flightPoints;
    double flightVx,flightVy;
    public bool Flying => artRoot?.Get("fly")!=null;
    public bool Pursuing => pursuit.Active;
    public void OnPlayerDamage()
    {
        if (pursuit.Active) return;
        pursuit.Acquire(UnityEngine.Random.Range(0,60000));
        flightWaypoint=false;flightPoints=0;
    }
    public bool UpdatePursuit(PlayerCtrl player)
    {
        long now=Now;
        pursuit.Tick(Math.Max(0,now-pursuitTick),player!=null && player.hp>0);
        pursuitTick=now;
        if (!pursuit.Active && player!=null && player.hp>0 && root.Int("info/firstAttack")!=0 && root.Int("info/notAttack")==0)
        {
            Vector2 delta=Pixels(player.FeetPos)-Pixels(mob.transform.position);
            if (NativeMobMotion.AcquisitionRange((int)delta.x,(int)delta.y)) OnPlayerDamage();
        }
        return pursuit.Active;
    }
    // Pursued-target flight branch 0x9BD799..0x9BDA9A. Idle region selection is
    // deliberately separate: it requires CPhysicalSpace2D's region topology.
    public bool MoveFlight(PlayerCtrl player,float dt,bool blocked,out int direction)
    {
        direction=Direction;
        if (!pursuit.Active || blocked) return false;
        var physics=motionPhysics ?? (motionPhysics=NativeMovementPhysics.Load());
        double fly=NativeMobMotion.Multiplier(root.Int("info/flySpeed"));
        Vector2 position=Pixels(mob.transform.position),target=Pixels(player.FeetPos);
        if (!flightWaypoint)
        {
            if (flightPoints==0) flightPoints=UnityEngine.Random.Range(60,120);
            waypoint=new Vector2((int)target.x+UnityEngine.Random.Range(-120,120),(int)target.y+UnityEngine.Random.Range(-40,40));
            flightWaypoint=true;
            if (--flightPoints<=0) pursuit.Clear();
        }
        float dx=waypoint.x-position.x,dy=waypoint.y-position.y;
        int horizontal=Math.Abs((int)dx)<10 ? 0 : dx<0 ? -1 : 1;
        int vertical=Math.Abs((int)dy)<10 ? -1 : 0;
        // 9BD836 keeps vertical input zero below and calls jump/flap above.
        if (Math.Abs((int)dy)>=10 && dy<0) flightVy=physics.Flap(fly);
        double oldVx=flightVx,oldVy=flightVy;
        physics.Flight(ref flightVx,ref flightVy,horizontal,vertical,fly,dt);
        position.x+=(float)((oldVx+flightVx)*.5*dt);
        position.y+=(float)((oldVy+flightVy)*.5*dt);
        mob.transform.position=new Vector3(position.x/Art.PPU,-position.y/Art.PPU,mob.transform.position.z);
        // Win32 PtInRect excludes the right and bottom edges and truncates coordinates.
        bool Inside(Vector2 p,int radius) => (int)p.x>=waypoint.x-radius && (int)p.x<waypoint.x+radius &&
            (int)p.y>=waypoint.y-radius && (int)p.y<waypoint.y+radius;
        if (Inside(position,11) || !Inside(target,150)) flightWaypoint=false;
        if (horizontal!=0) direction=horizontal;
        return flightVx!=0 || flightVy!=0;
    }
    long blockedUntil, busyUntil, nextDecision;
    // specialAttack (0x667600) rewrites the mob-wide attack interval; this is the
    // local equivalent of [mob+0xA0] + [mob+0x9C] being in the future.
    long attackGapUntil;
    // 0x6671D3 loads the constant double at 0xAF46E0 (= 30.0) and hands it to the
    // screen-shake call 0x43DA05. The unit is not documented; it is applied here as
    // client pixels, the same 1:1 mapping Art.PPU uses everywhere else.
    static readonly float TrembleAmplitude = 30f / Art.PPU;
    long removeAt=long.MaxValue,nextMpRecovery;
    int destructionHp=-1,deathAction=1,destructionAction=1;
    public int DeathAction => deathAction;
    // LifeFactory records own-file delays; Monster.killBy/listener always request die1.
    public int ServerDeathMillis => ServerMillis(root?.Get("die1"));
    public void Expire(int action)
    {
        deathAction=action;
        mob.ExpireNative(action);
    }
    public void ReleaseExpired() => field?.OnEnemyExpired(mob);
    readonly List<int> revives = new List<int>();
    readonly string killId = Guid.NewGuid().ToString("N");
    public static long Now => (long)(Time.timeAsDouble * 1000);
    public long CurrentHp => health?.Current ?? 0;
    public long MaximumHp => health?.Maximum ?? 1;
    public bool Busy => Now < busyUntil;
    public bool CanMove => artRoot != null && artRoot.Get("move") != null;
    public bool HasChase => artRoot?.Get("chase")!=null;
    public float GroundMoveSpeed(bool chasing)
    {
        int value=root.Int("info/"+(chasing && HasChase ? "chaseSpeed" : "speed"));
        // 9B2187: field Physics.walkSpeed * CLifeMoveParam+24 (ground limit).
        return (float)((motionPhysics ?? (motionPhysics=NativeMovementPhysics.Load())).WalkSpeed*NativeMobMotion.Multiplier(value)/Art.PPU);
    }
    public int Direction { get; private set; } = 1;

    public static NativeMobController Attach(EnemyUnit mob, FieldDirector field)
    {
        var controller = mob.gameObject.AddComponent<NativeMobController>();
        controller.mob = mob; controller.field = field;
        controller.encounter = field.GetComponent<NativeBossEncounter>() ?? field.gameObject.AddComponent<NativeBossEncounter>();
        controller.encounter.Bind(field);
        controller.Load();
        return controller;
    }

    void Load()
    {
        root = WzAssets.FileRoot("Mob/" + mob.fieldMobId.ToString("D7") + ".img");
        artRoot = WzAssets.MobArtRoot(mob.fieldMobId);
        var info = root.Get("info");
        pursuitTick=Now;
        mob.nativeFloating=mob.nativeFloating || Flying;
        state = new NativeMobCastState(info.Int("maxMP"));
        health = new NativeMobHealth(Convert.ToInt64(info.Get("maxHP")?.Value ?? 1));
        var destruction=info.Get("selfDestruction");
        destructionHp=destruction?.Int("hp",-1) ?? -1;
        if (destruction!=null) destructionAction=unchecked((sbyte)destruction.Int("action",1));
        int removeSeconds=destruction!=null ? destruction.Int("removeAfter",-1) : info.Int("removeAfter",-1);
        if (info.Int("removeAfter")>0 || destruction!=null && destructionHp<0) removeAt=Now+removeSeconds*1000L;
        nextMpRecovery=Now+RecoveryInterval();
        blockedUntil = Now + NativeBossRules.RegenMillis(mob.fieldMobId);
        busyUntil = blockedUntil;
        var revive = info.Get("revive");
        if (revive != null) foreach (var entry in revive.Children) if (entry.Value != null) revives.Add(Convert.ToInt32(entry.Value));
        var list = info.Get("skill");
        if (list != null) foreach (var entry in list.Children)
        {
            int id = entry.Int("skill"), level = entry.Int("level");
            var data = WzAssets.FileRoot("Skill/MobSkill.img").Get(id + "/level/" + level);
            if (data == null) { Debug.LogWarning("[MobSkill] Missing " + id + "/" + level); continue; }
            var c = new Cast { id = id, level = level, action = entry.Int("action", skills.Count + 1),
                mp = data.Int("mpCon"), hp = data.Int("hp", 100), x = data.Int("x", 1), y = data.Int("y", 1),
                count = data.Int("count", 1), limit = data.Int("limit"), probability = data.Int("prop", 100),
                duration = data.Int("time") * 1000L, cooldown = data.Int("interval") * 1000L, range = Range(data),
                effectAfter = entry.Int("effectAfter"), cast = true };
            if (c.hp <= 0) c.hp = 100;
            // LifeFactory:599 reads skill<action> from the mob's own file (monsterData,
            // link is not followed here), so a link-only mob really has animationTime 0.
            c.serverDelay = ServerMillis(root.Get("skill" + c.action));
            foreach (var child in data.Children)
                if (int.TryParse(child.Name, out _) && child.Value != null) c.summons.Add(Convert.ToInt32(child.Value));
            skills.Add(c);
        }
        encounter.OnSpawn(mob);
    }

    public static int AnimationMillis(ImgNode frames)
    {
        int result = 0;
        foreach (var frame in WzAssets.Frames(frames))
            result += Math.Max(1, Math.Abs(frame.Resolve().Int("delay", 100)));
        return result;
    }
    // LifeFactory:601-604 / 618-621: animationTime is the plain sum of every child's
    // "delay" with a 0 default and no abs(). A negative sum (or a missing container)
    // makes the source apply the effect immediately. Do not reuse AnimationMillis here.
    public static int ServerMillis(ImgNode frames)
    {
        int result = 0;
        if (frames == null) return 0;
        foreach (var child in frames.Children)
        {
            var node = child.TryResolve();
            if (node == null) continue;
            result += node.Int("delay");
        }
        return result;
    }
    public static Rect? Range(ImgNode node)
    {
        node = node?.Get("range") ?? node;
        var lt = node?.Get("lt"); var rb = node?.Get("rb");
        return lt == null || rb == null ? (Rect?)null : new Rect(lt.X, lt.Y, rb.X - lt.X, rb.Y - lt.Y);
    }
    // Attack areas can also be a point+radius (range/sp + range/r, 399 rows). See
    // MobAttackArea for the evidence; AttackInArea handles both shapes.
    public static MobAttackArea? Area(ImgNode node) =>
        MobAttackArea.TryParse(node, out var area) ? area : (MobAttackArea?)null;
    public static Vector2 Pixels(Vector3 world) => new Vector2(world.x * Art.PPU, -world.y * Art.PPU);
    public bool InRange(Rect? relative, PlayerCtrl player)
    {
        if (!relative.HasValue) return true;
        return relative.Value.Contains(Pixels(player.FeetPos) - Pixels(mob.transform.position));
    }
    bool Buffed(int id) => buffs.TryGetValue(id, out var b) && Now < b.until;
    void Buff(int id, int value, long duration) => buffs[id] = new BuffValue { value = value, until = Now + duration };

    int showdown;
    long showdownUntil;
    public int Showdown => Now < showdownUntil ? showdown : 0;
    // StatEffect:806-810 / Monster.applyStatus:1173-1342. Defense status
    // interpretation is tracked separately; this stores the authoritative reward status.
    public bool ApplyShowdown(int skill, int value, long duration, bool boss)
    {
        if ((skill!=4121003 && skill!=4221003) || boss) return false;
        var node=WzAssets.FileRoot("Skill/"+(skill/10000).ToString("D3")+".img").Get("skill/"+skill.ToString("D7"));
        int animation=ServerMillis(node?.Get("effect"));
        showdown=value;
        showdownUntil=Now+duration+animation-100;
        return true;
    }

    // Monster.debuffMob: cancel all source variants of the same monster status.
    public void Dispel(int skill)
    {
        void Remove(params int[] ids) { foreach (int id in ids) buffs.Remove(id); }
        if (skill==4111004) { Remove(102,112,152);Remove(103,113,153);return; }
        if (skill==2311001) { Remove(100,110,150);Remove(101,111,151);Remove(102,112,152);Remove(103,113,153);return; }
        if (skill!=1111007 && skill!=1211009 && skill!=1311007) return;
        if (skill==1111007) Remove(102,112,152);
        else if (skill==1211009) Remove(101,111,151);
        else Remove(100,110,150);
        bool anti=GameServer.Local!=null && GameConfigDao.GetBool(GameServer.Local.Db,"use_anti_immunity_crash",false);
        if (!anti) return;
        if (skill!=1211009 && !Buffed(143) && !Buffed(145)) Remove(140);
        if (skill!=1311007 && !Buffed(144) && !Buffed(145)) Remove(141);
    }

    void Update()
    {
        if (mob == null || mob.dead || field.Map == null || GameLoop.I == null || GameLoop.I.gameOver ||
            GameLoop.I.state != GameLoop.GameState.Playing || PlayerCtrl.InputLocked || Time.deltaTime <= 0) return;
        if (Now>=removeAt) { Expire(destructionAction); return; }
        if (Now>=nextMpRecovery)
        {
            nextMpRecovery=Now+RecoveryInterval();
            // MapleMap.mobMpRecovery uses mob level, not the unused WZ mpRecovery value.
            state.HealMp(NativeCombatData.Int(mob.CombatStats,"level",1),NativeCombatData.Int(mob.CombatStats,"maxMP"));
        }
        if (mob.VacuumLocked || Now < blockedUntil || Busy || Now < nextDecision) return;
        var player = GameLoop.I.player;
        if (player == null || player.hp <= 0) return;
        nextDecision = Now + 500; // Local AI polling; the source client, not server, chooses cast requests.
        Direction = player.worldX >= mob.transform.position.x ? 1 : -1;
        if (Mathf.Abs(player.worldX - mob.transform.position.x) > mob.aggroRange && !mob.IsBoss) return;
        var choices = new List<Cast>(skills);
        for (int i = choices.Count - 1; i > 0; i--) { int j = UnityEngine.Random.Range(0, i + 1); var tmp = choices[i]; choices[i] = choices[j]; choices[j] = tmp; }
        choices.Sort((a, b) => NativeBossRules.Priority(mob.fieldMobId, b.id).CompareTo(NativeBossRules.Priority(mob.fieldMobId, a.id)));
        foreach (var c in choices)
        {
            // MoveLifeHandler:172-177 truncates HP% to int before comparing with MobSkill.hp.
            if (!NativeMobCastState.HpAllows(health.Current,health.Maximum,c.hp) ||
                ((c.id >= 143 && c.id <= 145) && (Buffed(143) || Buffed(144) || Buffed(145)))) continue;
            if (!state.TryUse(c.Key, Now, c.mp, NativeBossRules.SkillCooldown(mob.fieldMobId, c.id, c.level, c.cooldown),
                Time.unscaledTime < mob.SkillsSealedUntil || Buffed(157))) continue;
            busyUntil = Now + Math.Max(1, Math.Max(0, c.serverDelay));
            mob.PlayNativeAction("skill" + c.action);
            if (Projected(c)) return;
            // MoveLifeHandler:136-141 — BANISH always applies immediately; everything
            // else waits for the whole skill<action> animation (0 => immediate).
            encounter.Schedule(mob, c.id == 129 ? 0 : Math.Max(0, c.serverDelay), () => Apply(c));
            return;
        }
        TryAttack(player);
    }

    void TryAttack(PlayerCtrl player)
    {
        if (Now < attackGapUntil) return;
        for (int i = 0; ; i++)
        {
            var attack = root.Get("attack" + (i + 1)) ?? artRoot.Get("attack" + (i + 1));
            if (attack == null) return;
            var data = attack.Get("info");
            if (data == null || !AttackInArea(Area(data), player)) continue;
            int cooldown = NativeBossRules.AttackCooldown(mob.fieldMobId, i, data.Int("attackAfter"));
            if (!state.TryUse("attack:" + i, Now, data.Int("conMP"), cooldown, false)) continue;
            // 0x667600: specialAttack turns this row's attackAfter into the mob-wide
            // attack interval ([mob+0x9C], read by 0x670A3F as
            // now >= [mob+0xA0] + [mob+0x9C]). Only 9400633/attack4 opens it.
            int attackAfter = Math.Max(0, data.Int("attackAfter"));
            if (data.Int("specialAttack") != 0) attackGapUntil = Now + attackAfter;
            // attackAfter is the real impact delay: the attack-area object created by
            // 0x66DE88 stores "A+0x8C(attackAfter) + base time" in its +0 trigger slot,
            // and the executor fires it from there. The server never sees this value
            // except as the Karing/Lucid cooldown fallback (LifeFactory:624-626).
            // busyUntil is the source animation lock: LifeFactory:618-621 sums every
            // child's delay of attack<N>.
            int delay = attackAfter;
            busyUntil = Now + Math.Max(1, Math.Max(0, ServerMillis(attack)));
            mob.PlayNativeAction("attack" + (i + 1));
            encounter.Schedule(mob, delay, () => {
                // 0x6671D3: `if (A->[+0x70]) shake(30.0)`. Fired when the attack
                // executes, before any target is collected, so it does not depend on
                // actually hitting something.
                if (data.Int("tremble") != 0) HitFeel.Shake(TrembleAmplitude, 0.25f);
                // Mob/<id>.img/attack<N>/info/effect + hit are live canvases (331 and
                // 2211 authored rows; the loader paths are documented in §6.1).
                // `effect` belongs to the attacker, `hit` to the struck body.
                PlayCanvas(data, "effect", mob.transform, mob.transform.position, mob.BodySortingOrder + 1);
                var p = GameLoop.I?.player;
                if (p == null || !AttackInArea(Area(data), p)) return;
                // 0x670C99..0x670CB2: one resolution per target per 0x7D0 (2000) ms.
                // The stamp is written before the damage is computed, so an attack
                // that lands during the victim's i-frames still starts the next
                // 2000 ms window -- that is the source order, not a shortcut.
                long stamp = Now;
                if (!NativeBossRules.MobAttackAllowed(stamp, p.lastMobAttackMs)) return;
                p.lastMobAttackMs = stamp;
                if (p.hp <= 0 || Time.unscaledTime < p.invulnUntil) return;
                PlayCanvas(data, "hit", null, p.FeetPos, p.SortingOrder + 1);
                float before = p.hp;
                int percent = data.Int("fixDamR");
                if (data.Get("deadlyAttack") != null)
                { p.TakeBossDamage(Mathf.Max(0,p.hp-1)); p.mp = Mathf.Min(p.mp,1); }
                else if (percent > 0) p.TakeBossDamage(NativeBossRules.PercentDamage((int)p.hpMax, percent));
                // MobAttackInfoFactory reads only mpBurn/disease/level/conMP/fixDamR/
                // deadlyAttack. There is no absolute `fixedDamage` on attack info, and
                // none of the 2219 attack rows author one, so base damage falls back to
                // PADamage exactly like the client-computed packet would.
                else p.TakeDamage(data.Int("PADamage", NativeCombatData.Int(mob.CombatStats, "PADamage")), Direction,
                    data.Int("magic") != 0, NativeCombatData.Int(mob.CombatStats, "level", 1),mob,i,data.Int("mpBurn"));
                if (data.Get("deadlyAttack")!=null || percent>0) p.mp = Mathf.Max(0,p.mp-data.Int("mpBurn"));
                int disease = data.Int("disease"), level = data.Int("level");
                if (p.hp < before && disease > 0)
                {
                    var effect = WzAssets.FileRoot("Skill/MobSkill.img").Get(disease+"/level/"+level);
                    if (effect != null) Apply(new Cast { id=disease,level=level,x=effect.Int("x",1),y=effect.Int("y",1),
                        probability=effect.Int("prop",100),count=effect.Int("count",1),duration=effect.Int("time")*1000L });
                }
            });
            return;
        }
    }

    // Player hit box used for the attack test. The source tests the target's whole
    // bounding rect against the attack area (rect: 0x95B734 + 0x7A38D6 + [0xBF04A8];
    // circle: 0x66DB68 + 0x66DBDC..0x66DC21), and the exact numbers come from the
    // character's body frames, which are not traced, so this is a deliberate
    // approximation centred on the feet point.
    const float PlayerHitHalfWidth = 14f;
    const float PlayerHitHeight = 48f;
    bool AttackInArea(MobAttackArea? area, PlayerCtrl player)
    {
        if (!area.HasValue) return false; // No verified hit area: do not fabricate a map-wide attack.
        var delta = Pixels(player.FeetPos) - Pixels(mob.transform.position);
        if (Direction > 0 && root.Int("info/noFlip") == 0) delta.x = -delta.x;
        // Both shapes are body tests, so both go through Overlaps: a rectangle is an
        // AABB intersection, a circle is the distance from its centre to the nearest
        // point of the body compared with r+10. Do not route circles through the
        // point probe -- that under-reaches by the body height.
        return area.Value.Overlaps(delta.x - PlayerHitHalfWidth, delta.y - PlayerHitHeight,
            delta.x + PlayerHitHalfWidth, delta.y);
    }

    // Mob/<id>.img/<action>/info/{effect,hit,...}: the client builds these paths with
    // the same %07d/%s pattern as the attack parser (monster-skills.md §6.1), and
    // most attack rows really author them (hit 2211, effect 331). No-op when the
    // container is missing, which is the normal case for the rest.
    static void PlayCanvas(ImgNode info, string node, Transform parent, Vector3 at, int order)
    {
        var frames = info?.Get(node);
        if (frames == null) return;
        NativeImgAnimation.SpawnAt(frames, parent, at, order);
    }
    // Skill/MobSkill.img/<type>/level/<n>/{effect,tile}: `effect` and `tile` are in
    // the client's read whitelist (§3.5). Only genuine casts have a MobSkill row,
    // so attack-borne diseases stay silent here.
    void PlaySkillCanvas(Cast c, string node, bool follow, bool repeat, float lifetime)
    {
        if (!c.cast || c.level <= 0) return;
        var frames = WzAssets.FileRoot("Skill/MobSkill.img").Get(c.id + "/level/" + c.level + "/" + node);
        if (frames == null) return;
        NativeImgAnimation.SpawnAt(frames, follow ? mob.transform : null, mob.transform.position,
            mob.BodySortingOrder + 1, false, repeat, lifetime);
    }

    bool Projected(Cast c)
    {
        int id = mob.fieldMobId;
        if (id == 8880830 && c.id == 128 && c.level == 1)
        { Fixed(1560, 100, new Rect(-600,-1000,1201,1001), "customSkill/karing/goongiScreenVideoLayer"); return true; }
        if (id == 8880830 && c.id == 126 && c.level == 7)
        { Fixed(780, 30, new Rect(-215,-325,554,333), null); return true; }
        if (id == 8880837 && c.id == 128 && c.level == 2)
        { Fixed(2370, 25, new Rect(-800,-800,1601,1601), "customSkill/karing/darkPulseVideoLayer"); return true; }
        return id == 8880831 && ((c.id == 123 && c.level == 3) || (c.id == 120 && c.level == 5));
    }
    void Fixed(int delay, int percent, Rect? range, string effect)
    {
        encounter.Effect(effect);
        encounter.Schedule(mob, delay, () => {
            var p = GameLoop.I?.player;
            if (p != null && InRange(range, p)) p.TakeBossDamage(NativeBossRules.PercentDamage((int)p.hpMax, percent));
        });
    }

    void Apply(Cast c)
    {
        if (UnityEngine.Random.value * 100 >= c.probability) return;
        PlaySkillCanvas(c, "effect", true, false, 0);
        var p = GameLoop.I?.player;
        switch (c.id)
        {
            case 100: case 101: case 102: case 103: case 110: case 111: case 112: case 113:
            case 150: case 151: case 152: case 153: case 154: case 155: case 156: case 157:
                ApplyBuff(c); return;
            case 140: if (!Buffed(141)) ApplyBuff(c); return;
            case 141: if (!Buffed(140)) ApplyBuff(c); return;
            case 143: case 144: case 145: ApplyBuff(c); return;
            case 114:
                // MobSkill.applyHealEffect: hps = (x/1000) * (int)(950 + 1050*rand).
                if (c.range.HasValue) foreach (var e in new List<EnemyUnit>(EnemyUnit.All))
                {
                    if (!e.fieldMode || e.dead || !c.range.Value.Contains(Pixels(e.transform.position) - Pixels(mob.transform.position))) continue;
                    e.NativeController?.Heal(c.x / 1000 * (int)(950 + 1050 * UnityEngine.Random.value));
                    e.NativeController?.state.HealMp(c.y, NativeCombatData.Int(e.CombatStats,"maxMP"));
                }
                else { Heal(c.x); state.HealMp(c.y, NativeCombatData.Int(mob.CombatStats,"maxMP")); }
                return;
            case 170: case 186: case 188: case 189: case 190: case 191: case 200: case 201: case 202: case 203:
                Summon(c); return;
            case 183: case 184: case 185: case 187:
                string path = c.id == 183 ? "customBossWill/webBurst" : c.id == 184 ? "customBossMagnus/meteorStorm" :
                    c.id == 185 ? "customBossLucid/dreamBurst" : "customBossSeren/sacredBurst";
                Fixed(1400, Math.Max(1, Math.Min(100, c.id == 184 ? 35 : c.x)), null, path); return;
            case 176:
                if (mob.fieldMobId == 9421583) { Fixed(1400, 35, null, null); return; }
                encounter.Effect("customBoss/akayrum/screenCrack");
                encounter.Schedule(mob, 1100, () => {
                    var player = GameLoop.I?.player;
                    if (player == null) return;
                    var pos = Pixels(player.FeetPos);
                    int[,] vortices = {{-610,-181,115},{-305,-181,110},{0,-181,150},{315,-181,110},{610,-181,115}};
                    for (int i = 0; i < 5; i++)
                    {
                        int radius = vortices[i,2] - (InputHub.Held(GameAction.Down) ? 55 : 0);
                        if ((pos - new Vector2(vortices[i,0],vortices[i,1])).sqrMagnitude <= radius * radius)
                        { player.TakeBossDamage(Math.Max(1,c.x)); break; }
                    }
                }); return;
            case 174: case 177: case 214: case 215: return; // Visual-only in the source server too.
            // MobSkill.applyEffect has no branch for these, even though the enum lists
            // them: the source really does nothing. 105/169 are not even in MobSkillType,
            // which makes the source throw while loading the mob's stats.
            case 105: case 115: case 134: case 135: case 136: case 142: case 169: return;
            // CYGNUS_UNSUPPORTED_138/146/171/172 are in the enum but also have no
            // applyEffect branch, so the source really does nothing for them.
            case 138: case 146: case 171: case 172: return;
            case 127: if (p != null && InRange(c.range,p)) p.DispelNativeBuffs(); return;
            case 129:
                if (p != null && InRange(c.range,p))
                {
                    // LifeFactory:633 defaults banMap/0/field to -1, so -1 is the
                    // "no destination" sentinel and map 0 is a real destination.
                    int map = root.Int("info/ban/banMap/0/field", -1);
                    // LifeFactory:633 also reads banMsg; Character.changeMapBanish:1563
                    // drops it as message type 5 before the warp ("As the Pentacle
                    // swayed in the shadows, you were brought to ...").
                    string msg = root.Text("info/ban/banMsg");
                    if (map >= 0 && !string.IsNullOrEmpty(msg)) NativeSystemMessage.Drop(5, msg);
                    if (map >= 0) { NativeItemUseEffects.RememberBanish(); field.WarpTo(map,root.Text("info/ban/banMap/0/portal","sp")); }
                }
                return;
            case 131:
                // MobSkill.spawnMonsterMist -> MapleMap.spawnMist(mist, x*100, false, false, false).
                // The third argument is `poison`, and it is false: the source server only
                // creates a mob-type mist object that lives x*100 ms. No damage, no disease.
                // `131/level/<n>/tile` is the ground canvas the client draws for it (§6.3);
                // lifetime follows the same x*100 ms, so the two cannot drift apart.
                PlaySkillCanvas(c, "tile", false, true, Math.Max(0, c.x) * 0.1f);
                return;
            case 120: case 121: case 122: case 123: case 124: case 125: case 126: case 128: case 132: case 133:
                // MobSkill.applyDisease: the range branch caps SEDUCE by `count` and
                // additionally blocks every disease behind HOLY_SHIELD, SEDUCE and
                // STUN included (:525). The single-target branch goes straight to
                // Character.giveDebuff, which does let SEDUCE/STUN through (:2823).
                // Both branches only exist for a real cast (`lt/rb && skill`).
                if (p == null || !InRange(c.range,p)) return;
                bool region = c.cast && c.range.HasValue;
                if (region && c.id == 128 && c.count <= 0) return;
                NativeMobDebuffs.For(p).Apply(c.id,c.x,c.y,c.duration,region);
                return;
            default: Debug.LogWarning("[MobSkill] Source-unsupported skill " + c.Key); return;
        }
    }

    void ApplyBuff(Cast c)
    {
        if (c.range.HasValue) foreach (var e in new List<EnemyUnit>(EnemyUnit.All))
        {
            if (e.fieldMode && !e.dead && c.range.Value.Contains(Pixels(e.transform.position) - Pixels(mob.transform.position)))
                e.NativeController?.Buff(c.id,c.x,c.duration);
        }
        else Buff(c.id,c.x,c.duration);
    }
    void Summon(Cast c)
    {
        summoned.RemoveAll(e => e == null || e.dead);
        if (EnemyUnit.All.Count >= 80) return; // MobSkill.summonMonsters: map mobs < 80
        // MapId.isDojo: DOJO_SOLO_BASE..925033804 -> unlimited summons.
        int limit = field.Map.id >= 925020000 && field.Map.id <= 925033804 ? int.MaxValue : c.limit;
        int count = Math.Min(c.summons.Count, Math.Max(0,limit - summoned.Count));
        var ids = new List<int>(c.summons);
        for (int i = ids.Count - 1; i > 0; i--) { int j = UnityEngine.Random.Range(0,i+1); int v = ids[i]; ids[i] = ids[j]; ids[j] = v; }
        for (int i = 0; i < count; i++)
        {
            Vector3 pos = mob.transform.position;
            if (ids[i] == 8500003) pos.y = 590 / Art.PPU;                       // HIGH_DARKSTAR: ypos = -590
            if (ids[i] == 8500004) pos.x += UnityEngine.Random.Range(-500,500) / Art.PPU;  // LOW_DARKSTAR
            if (ids[i] == 8510100)                                              // BLOODY_BOOM
            {
                // Source sets an absolute map-pixel xpos here, so it is not an offset.
                if (UnityEngine.Random.Range(0,5) == 0)
                    pos = new Vector3((UnityEngine.Random.Range(0,5) + (UnityEngine.Random.Range(0,2) == 1 ? 180 : 0)) / Art.PPU, -78 / Art.PPU, pos.z);
                else pos.x += UnityEngine.Random.Range(-500,500) / Art.PPU;
            }
            // MobSkill.summonMonsters re-rolls toward the map centre instead of clamping.
            if (field.Map.id == 220080001)
            {
                if (pos.x < -890 / Art.PPU) pos.x = (Mathf.Ceil(UnityEngine.Random.value * 150) - 890) / Art.PPU;
                else if (pos.x > 230 / Art.PPU) pos.x = (230 - Mathf.Ceil(UnityEngine.Random.value * 150)) / Art.PPU;
            }
            if (field.Map.id == 230040420)
            {
                if (pos.x < -239 / Art.PPU) pos.x = (Mathf.Ceil(UnityEngine.Random.value * 150) - 239) / Art.PPU;
                else if (pos.x > 371 / Art.PPU) pos.x = (371 - Mathf.Ceil(UnityEngine.Random.value * 150)) / Art.PPU;
            }
            var e = field.SpawnNativeMob(ids[i],pos);
            if (e != null) summoned.Add(e);
        }
    }
    public DamageResult FilterHit(DamageResult hit, bool magic)
    {
        if (mob.dead || hit.Amount <= 0) return hit;
        if (Now < blockedUntil || root.Int("info/invincible") != 0 || encounter.BlocksDamage(mob)) return new DamageResult(0,false);
        int reflect = magic ? (Buffed(144) ? 144 : 145) : (Buffed(143) ? 143 : 145);
        if (Buffed(reflect))
            foreach (var c in skills)
                if (c.id == 145) GameLoop.I?.player?.TakeBossDamage(Math.Max(0,magic ? c.y : c.x));
        if (Buffed(reflect) || Buffed(magic ? 141 : 140)) return new DamageResult(1,false);
        if (mob.IsBoss && GameSession.InGame && hit.Amount != int.MaxValue)
        {
            double multiplier = GameSession.Net.Boss(new BossRequest { characterId=GameSession.CharacterId }).multiplier;
            return new DamageResult((int)Math.Min(int.MaxValue,Math.Floor(hit.Amount*multiplier+.5)),hit.Critical);
        }
        return hit;
    }
    public void BlockFor(int millis) => blockedUntil = Math.Max(blockedUntil, Now + millis);
    public Dictionary<string,object> EffectiveStats(Dictionary<string,object> original)
    {
        if (original == null || buffs.Count == 0) return original;
        var result = new Dictionary<string,object>(original);
        // The source sends these percentages to the client; use them in the local
        // combat policy, without mutating the shared WZ snapshot.
        Scale(result,"PADamage",100,110,150);
        Scale(result,"MADamage",101,111,151);
        Scale(result,"PDDamage",102,112,152);
        Scale(result,"MDDamage",103,113,153);
        if (Buffed(154)) result["acc"] = buffs[154].value;
        if (Buffed(155)) result["eva"] = buffs[155].value;
        return result;
    }
    void Scale(Dictionary<string,object> result, string key, params int[] ids)
    {
        foreach (int id in ids) if (Buffed(id))
        { result[key] = (int)Math.Min(int.MaxValue,NativeCombatData.Int(result,key) * (long)buffs[id].value / 100); break; }
    }
    public void ApplyDamage(int damage)
    {
        long applied = health.Damage(damage); mob.hp = health.Current;
        encounter.OnDamaged(mob,(int)applied);
        if (destructionHp>=0 && health.Current<=destructionHp) { deathAction=destructionAction; health.Damage(health.Current); mob.hp=0; }
    }
    static int RecoveryInterval() => GameServer.Local==null ? 10000 : Math.Max(1,GameConfigDao.GetInt(GameServer.Local.Db,"respawn_interval",10000));
    public void Heal(long amount) { long before=health.Current; health.Heal(amount); mob.TotalHealed+=Math.Max(0,health.Current-before); mob.hp = health.Current; }
    public int AbsorbMp(int percent)
    {
        if (mob.dead || mob.IsBoss) return 0;
        int amount=Math.Min(state.Mp,(int)(NativeCombatData.Int(mob.CombatStats,"maxMP")*(percent/100.0)));
        if (amount>0) state.TryUse("mp-eater",Now,amount,0,false);
        return amount;
    }
    public void SetHpPercent(int percent) { health.SetPercent(percent); mob.hp = health.Current; }
    public void FakeDamage(int damage) { health.Damage(Math.Min(damage,Math.Max(0,health.Current-1))); mob.hp = health.Current; }
    public void OnKilled()
    {
        if (GameSession.InGame && GameLoop.I?.player != null && GameLoop.I.player.hp > 0)
            GameSession.Net.Boss(new BossRequest { characterId=GameSession.CharacterId,mobId=mob.fieldMobId,
                operation=BossOperation.RecordKill,encounter=killId });
        encounter.OnKilled(mob);
        if (revives.Count == 0) return;
        // Monster.killBy broadcasts before the delayed revive; timeMob hours are
        // not read by MapFactory and must not become an invented spawn restriction.
        if (field.Map!=null && revives.Contains(field.Map.timeMobId))
            NativeSystemMessage.DropMap(6,field.Map.timeMobMessage);
        Vector3 pos = mob.transform.position;
        encounter.Schedule(mob, Math.Max(0,ServerDeathMillis), () => {
            foreach (int id in revives) field.SpawnNativeMob(id,pos);
        }, true);
    }
}
