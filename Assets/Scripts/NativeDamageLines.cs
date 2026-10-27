using System;
using UnityEngine;

// AbstractDealDamageHandler.showIndexedDamageNumbers: HP settles immediately;
// only the local number packets are delayed, at index * hitIntervalMs.
public sealed class NativeDamageLines : MonoBehaviour
{
    Component target;
    DamageResult[] hits;
    int next, interval;
    double started;
    FieldDirector field;
    FieldMap map;
    public static void Show(Component target, DamageResult[] values, bool indexed, int interval, bool total = false)
    {
        if (values.Length == 0) return;
        var go = new GameObject("NativeDamageLines");
        var effect = go.AddComponent<NativeDamageLines>();
        effect.target = target; effect.interval = interval;
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
        effect.Tick();
    }
    static void Delete(GameObject value) { if(Application.isPlaying) Destroy(value); else DestroyImmediate(value); }
    void Update() => Tick();
    void Tick()
    {
        if (target == null || (field != null && !ReferenceEquals(field.Map,map))) { Delete(gameObject); return; }
        while (next < hits.Length && Time.realtimeSinceStartupAsDouble >= started + next * interval / 1000.0)
        {
            Vector3 anchor = target is EnemyUnit e ? e.DamageAnchor : ((BossUnit)target).DamageAnchor;
            // BeiDou.exe 0x6691D3: indexed line spacing 30px; +15px above anchor.
            anchor += Vector3.up * ((15 + 30 * next) / Art.PPU);
            var hit = hits[next++];
            if (hit.Amount == 0) DamageText.Miss(anchor);
            else DamageText.Show(anchor,hit.Amount,hit.Critical ? "cri" : "red",1);
        }
        if (next == hits.Length) Delete(gameObject);
    }
}
