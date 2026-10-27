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
        public int delay;
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
    ImgNode root;
    long blockedUntil, busyUntil, nextDecision;
    readonly List<int> revives = new List<int>();
    readonly string killId = Guid.NewGuid().ToString("N");
    public static long Now => (long)(Time.timeAsDouble * 1000);
    public long CurrentHp => health?.Current ?? 0;
    public long MaximumHp => health?.Maximum ?? 1;
    public bool Busy => Now < busyUntil;
    public bool CanMove => root != null && root.Get("move") != null;
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
        var info = root.Get("info");
        state = new NativeMobCastState(info.Int("maxMP"));
        health = new NativeMobHealth(Convert.ToInt64(info.Get("maxHP")?.Value ?? 1));
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
                duration = data.Int("time") * 1000L, cooldown = data.Int("interval") * 1000L, range = Range(data) };
            if (c.hp <= 0) c.hp = 100;
            c.delay = AnimationMillis(root.Get("skill" + c.action));
            foreach (var child in data.Children)
                if (int.TryParse(child.Name, out _) && child.Value != null) c.summons.Add(Convert.ToInt32(child.Value));
            skills.Add(c);
        }
        encounter.OnSpawn(mob);
    }

    public static int AnimationMillis(ImgNode frames)
    {
        int result = 0;
        if (frames != null) foreach (var frame in frames.Resolve().Children) result += frame.Int("delay");
        return result;
    }
    public static Rect? Range(ImgNode node)
    {
        node = node?.Get("range") ?? node;
        var lt = node?.Get("lt"); var rb = node?.Get("rb");
        return lt == null || rb == null ? (Rect?)null : new Rect(lt.X, lt.Y, rb.X - lt.X, rb.Y - lt.Y);
    }
    public static Vector2 Pixels(Vector3 world) => new Vector2(world.x * Art.PPU, -world.y * Art.PPU);
    public bool InRange(Rect? relative, PlayerCtrl player)
    {
        if (!relative.HasValue) return true;
        return relative.Value.Contains(Pixels(player.FeetPos) - Pixels(mob.transform.position));
    }
    bool Buffed(int id) => buffs.TryGetValue(id, out var b) && Now < b.until;
    void Buff(int id, int value, long duration) => buffs[id] = new BuffValue { value = value, until = Now + duration };

    void Update()
    {
        if (mob == null || mob.dead || field.Map == null || GameLoop.I == null || GameLoop.I.gameOver ||
            GameLoop.I.state != GameLoop.GameState.Playing || PlayerCtrl.InputLocked || Time.deltaTime <= 0) return;
        if (Now < blockedUntil || Busy || Now < nextDecision) return;
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
            if (mob.hp / mob.hpMax * 100 > c.hp ||
                ((c.id >= 143 && c.id <= 145) && (Buffed(143) || Buffed(144) || Buffed(145)))) continue;
            if (!state.TryUse(c.Key, Now, c.mp, NativeBossRules.SkillCooldown(mob.fieldMobId, c.id, c.level, c.cooldown),
                Time.unscaledTime < mob.SkillsSealedUntil || Buffed(157))) continue;
            busyUntil = Now + Math.Max(1, c.delay);
            mob.PlayNativeAction("skill" + c.action);
            if (Projected(c)) return;
            encounter.Schedule(mob, c.id == 129 ? 0 : c.delay, () => Apply(c));
            return;
        }
        TryAttack(player);
    }

    void TryAttack(PlayerCtrl player)
    {
        for (int i = 0; ; i++)
        {
            var attack = root.Get("attack" + (i + 1));
            if (attack == null) return;
            var data = attack.Get("info");
            if (data == null || !AttackInRange(Range(data), player)) continue;
            int cooldown = NativeBossRules.AttackCooldown(mob.fieldMobId, i, data.Int("attackAfter"));
            if (!state.TryUse("attack:" + i, Now, data.Int("conMP"), cooldown, false)) continue;
            int delay = data.Int("attackAfter", AnimationMillis(attack));
            busyUntil = Now + Math.Max(1, AnimationMillis(attack));
            mob.PlayNativeAction("attack" + (i + 1));
            encounter.Schedule(mob, delay, () => {
                var p = GameLoop.I?.player;
                if (p == null || !AttackInRange(Range(data), p)) return;
                if (p.hp <= 0 || Time.unscaledTime < p.invulnUntil) return;
                float before = p.hp;
                int percent = data.Int("fixDamR"), fixedDamage = data.Int("fixedDamage");
                if (data.Get("deadlyAttack") != null)
                { p.TakeBossDamage(Mathf.Max(0,p.hp-1)); p.mp = Mathf.Min(p.mp,1); }
                else if (percent > 0) p.TakeBossDamage(NativeBossRules.PercentDamage((int)p.hpMax, percent));
                else if (fixedDamage > 0) p.TakeBossDamage(fixedDamage);
                else p.TakeDamage(data.Int("PADamage", NativeCombatData.Int(mob.CombatStats, "PADamage")), Direction,
                    data.Int("magic") != 0, NativeCombatData.Int(mob.CombatStats, "level", 1));
                p.mp = Mathf.Max(0,p.mp-data.Int("mpBurn"));
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

    bool AttackInRange(Rect? range, PlayerCtrl player)
    {
        if (!range.HasValue) return false; // No verified hit area: do not fabricate a map-wide attack.
        var delta = Pixels(player.FeetPos) - Pixels(mob.transform.position);
        if (Direction > 0 && root.Int("info/noFlip") == 0) delta.x = -delta.x;
        return range.Value.Contains(delta);
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
                if (c.range.HasValue) foreach (var e in new List<EnemyUnit>(EnemyUnit.All))
                {
                    if (!e.fieldMode || e.dead || !c.range.Value.Contains(Pixels(e.transform.position) - Pixels(mob.transform.position))) continue;
                    e.NativeController?.Heal(c.x / 1000 * UnityEngine.Random.Range(950, 2000));
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
            case 127: if (p != null && InRange(c.range,p)) p.DispelNativeBuffs(); return;
            case 129:
                if (p != null && InRange(c.range,p))
                {
                    int map = root.Int("info/ban/banMap/0/field", -1);
                    if (map > 0) field.WarpTo(map,root.Text("info/ban/banMap/0/portal","sp"));
                }
                return;
            case 131:
                var area = c.range;
                if (!area.HasValue) return;
                Vector2 center = Pixels(mob.transform.position);
                for (int i = 0; i < c.x * 100; i += 1000)
                    encounter.Schedule(mob, i, () => {
                        var player = GameLoop.I?.player;
                        if (player != null && area.Value.Contains(Pixels(player.FeetPos) - center))
                            NativeMobDebuffs.For(player).Apply(125,c.x,c.y,1000);
                    });
                return;
            case 120: case 121: case 122: case 123: case 124: case 125: case 126: case 128: case 132: case 133:
                if (p != null && InRange(c.range,p) && (c.id != 128 || c.count > 0))
                    NativeMobDebuffs.For(p).Apply(c.id,c.x,c.y,c.duration);
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
        if (EnemyUnit.All.Count >= 80) return;
        int limit = field.Map.id >= 925020000 && field.Map.id < 925040000 ? int.MaxValue : c.limit;
        int count = Math.Min(c.summons.Count, Math.Max(0,limit - summoned.Count));
        var ids = new List<int>(c.summons);
        for (int i = ids.Count - 1; i > 0; i--) { int j = UnityEngine.Random.Range(0,i+1); int v = ids[i]; ids[i] = ids[j]; ids[j] = v; }
        for (int i = 0; i < count; i++)
        {
            Vector3 pos = mob.transform.position;
            if (ids[i] == 8500003) pos.y = 590 / Art.PPU;
            if (ids[i] == 8500004 || ids[i] == 8510100) pos.x += UnityEngine.Random.Range(-500,500) / Art.PPU;
            if (field.Map.id == 220080001) pos.x = Mathf.Clamp(pos.x,-890 / Art.PPU,230 / Art.PPU);
            if (field.Map.id == 230040420) pos.x = Mathf.Clamp(pos.x,-239 / Art.PPU,371 / Art.PPU);
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
    }
    public void Heal(long amount) { long before=health.Current; health.Heal(amount); mob.TotalHealed+=Math.Max(0,health.Current-before); mob.hp = health.Current; }
    public void SetHpPercent(int percent) { health.SetPercent(percent); mob.hp = health.Current; }
    public void FakeDamage(int damage) { health.Damage(Math.Min(damage,Math.Max(0,health.Current-1))); mob.hp = health.Current; }
    public void OnKilled()
    {
        if (GameSession.InGame && GameLoop.I?.player != null && GameLoop.I.player.hp > 0)
            GameSession.Net.Boss(new BossRequest { characterId=GameSession.CharacterId,mobId=mob.fieldMobId,
                operation=BossOperation.RecordKill,encounter=killId });
        encounter.OnKilled(mob);
        if (revives.Count == 0) return;
        Vector3 pos = mob.transform.position;
        encounter.Schedule(mob, AnimationMillis(root.Get("die1")), () => {
            foreach (int id in revives) field.SpawnNativeMob(id,pos);
        }, true);
    }
}
