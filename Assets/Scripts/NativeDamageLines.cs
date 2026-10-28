using System;
using UnityEngine;

// AbstractDealDamageHandler.showIndexedDamageNumbers: HP settles immediately;
// only the local number packets are delayed, at index * hitIntervalMs.
public sealed class NativeDamageLines : MonoBehaviour
{
    Component target;
    DamageResult[] hits;
    bool[] displayed;
    int next, interval;
    int ordinarySkill = -1;
    double started;
    FieldDirector field;
    FieldMap map;
    public static void Show(Component target, DamageResult[] values, bool indexed, int interval, bool total = false, int ordinarySkill = -1)
    {
        if (values.Length == 0) return;
        var go = new GameObject("NativeDamageLines");
        var effect = go.AddComponent<NativeDamageLines>();
        effect.target = target; effect.interval = interval;
        effect.ordinarySkill = ordinarySkill;
        effect.field = FindObjectOfType<FieldDirector>();
        effect.map = effect.field != null ? effect.field.Map : null;
        effect.started = Time.realtimeSinceStartupAsDouble;
        if (!total) effect.hits = (DamageResult[])values.Clone();
        else
        {
            long totalAmount = 0;
            foreach (var hit in values) totalAmount += hit.Amount;
            effect.hits = new[] { new DamageResult((int)Math.Min(int.MaxValue,totalAmount),false) };
        }
        effect.displayed = new bool[effect.hits.Length];
        effect.Tick();
    }
    static void Delete(GameObject value) { if(Application.isPlaying) Destroy(value); else DestroyImmediate(value); }
    void Update() => Tick();
    // CMob hit queue 0x66B0A2..0x66B13C: delay relative to the attack's hit time.
    public static int OrdinaryLineDelay(int skill, int line)
    {
        switch (skill)
        {
            case 5101004: case 15101003: case 5121007: case 15111004: return 0;
            case 22171002: return line == 0 ? 0 : line == 1 ? 90 : line == 2 ? 270 : line == 3 ? 630 : 0;
            case 3111006: case 3211006: case 13111001: return 60 * line;
            default: return 120 * line;
        }
    }
    void Tick()
    {
        if (target == null || (field != null && !ReferenceEquals(field.Map,map))) { Delete(gameObject); return; }
        for (int line = 0; line < hits.Length; line++)
        {
            if (displayed[line] || Time.realtimeSinceStartupAsDouble < started +
                (ordinarySkill >= 0 ? OrdinaryLineDelay(ordinarySkill, line) : line * interval) / 1000.0) continue;
            displayed[line] = true; next++;
            Vector3 anchor = target is EnemyUnit e ? e.DamageAnchor : ((BossUnit)target).DamageAnchor;
            // BeiDou.exe 0x6691D3: indexed line spacing 30px; +15px above anchor.
            anchor += Vector3.up * ((15 + 30 * line) / Art.PPU);
            var hit = hits[line];
            if (hit.Amount == 0) DamageText.Miss(anchor);
            else DamageText.Show(anchor,hit.Amount,hit.Critical ? "cri" : "red",1);
        }
        if (next == hits.Length) Delete(gameObject);
    }
}
