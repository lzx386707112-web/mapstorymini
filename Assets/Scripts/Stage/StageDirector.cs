using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// 关卡推进器：读 chapter1.json，把 segments 变成真实战斗流程。
//
// 数据流：JSON（唯一源）→ StageChapter → StageDirector → 场景
// 段类型只有四种：walk（空跑，教手感）/ combat（按批刷怪，清完推进）
//                rest（喘息）/ boss（单屏封闭 Boss 战）
//
// 「空跑」不能省：没有它，玩家从第一秒就在打架，8 分钟就疲劳了。
public class StageDirector : MonoBehaviour
{
    public StageChapter chapter;
    public int  levelIndex;
    public int  segIndex;
    public bool allClear;
    public bool levelClear;

    public StageLevel CurLevel =>
        chapter != null && levelIndex >= 0 && levelIndex < chapter.levels.Count
            ? chapter.levels[levelIndex] : null;

    public StageSegment CurSegment
    {
        get
        {
            var lv = CurLevel;
            return lv != null && segIndex >= 0 && segIndex < lv.segments.Count
                ? lv.segments[segIndex] : null;
        }
    }

    public BossUnit boss;
    public string hint = "";
    public float  hintT;

    Coroutine run;

    public void Begin(StageChapter ch, int startLevel = 0)
    {
        chapter = ch;
        levelIndex = startLevel;
        allClear = false;
        if (run != null) StopCoroutine(run);
        run = StartCoroutine(RunAll());
    }

    IEnumerator RunAll()
    {
        while (levelIndex < chapter.levels.Count)
        {
            levelClear = false;
            yield return RunLevel(chapter.levels[levelIndex]);
            if (GameLoop.I != null && GameLoop.I.gameOver) yield break;
            levelClear = true;
            Say("「" + chapter.levels[levelIndex].name + "」通过", 2.0f);
            yield return new WaitForSecondsRealtime(1.4f);
            levelIndex++;
        }
        allClear = true;
        Say("第一章完成", 999f);
    }

    IEnumerator RunLevel(StageLevel lv)
    {
        ApplyTheme(lv.theme);
        // 关卡 BGM 按主题映射（chapter1 主题本身不带 bgm 字段，沿用同地貌的原版曲）：
        // 南港→GoPicnic、树林/草原→RestNPeace、悬崖/雪原→SleepyWood
        string bgm = "Bgm00/RestNPeace";
        if (lv.theme == "harbor") bgm = "Bgm00/GoPicnic";
        else if (lv.theme == "cliff" || lv.theme == "snow") bgm = "Bgm00/SleepyWood";
        AudioManager.PlayBgm(bgm);
        if (!string.IsNullOrEmpty(lv.teaches)) Say(lv.teaches, 3.2f);

        for (segIndex = 0; segIndex < lv.segments.Count; segIndex++)
        {
            var seg = lv.segments[segIndex];
            if (!string.IsNullOrEmpty(seg.hint)) Say(seg.hint, 2.6f);

            switch (seg.kind)
            {
                case "walk":   yield return RunWalk(seg);   break;
                case "combat": yield return RunCombat(seg); break;
                case "rest":   yield return RunRest(seg);   break;
                case "boss":   yield return RunBoss(seg);   break;
                default:
                    yield return new WaitForSecondsRealtime(1f);
                    break;
            }

            if (GameLoop.I != null && GameLoop.I.gameOver) yield break;
        }
    }

    IEnumerator RunWalk(StageSegment seg)
    {
        float t = seg.duration > 0f ? seg.duration : 6f;
        while (t > 0f)
        {
            if (GameLoop.I != null && GameLoop.I.gameOver) yield break;
            t -= Time.unscaledDeltaTime;
            yield return null;
        }
    }

    IEnumerator RunRest(StageSegment seg)
    {
        float t = seg.duration > 0f ? seg.duration : 3f;
        while (t > 0f)
        {
            if (GameLoop.I != null && GameLoop.I.gameOver) yield break;
            t -= Time.unscaledDeltaTime;
            yield return null;
        }
    }

    IEnumerator RunCombat(StageSegment seg)
    {
        foreach (var sp in seg.spawns)
        {
            for (int i = 0; i < sp.count; i++)
            {
                if (GameLoop.I == null || GameLoop.I.gameOver) yield break;
                Spawn(sp);
                yield return new WaitForSecondsRealtime(Mathf.Max(0.05f, sp.gap));
            }
        }
        // 清场才推进：这是「房间制」的核心，也是玩家理解节奏的锚点
        while (EnemyUnit.All.Count > 0)
        {
            if (GameLoop.I == null || GameLoop.I.gameOver) yield break;
            yield return null;
        }
        yield return new WaitForSecondsRealtime(0.25f);
    }

    IEnumerator RunBoss(StageSegment seg)
    {
        StageBossDef def = null;
        if (!string.IsNullOrEmpty(seg.boss)) chapter.bosses.TryGetValue(seg.boss, out def);
        if (def == null && chapter.bosses.Count > 0)
            foreach (var kv in chapter.bosses) { def = kv.Value; break; }

        if (def == null)
        {
            Debug.LogWarning("[Stage] 找不到 Boss 定义：" + seg.boss);
            yield break;
        }

        var go = new GameObject("Boss_" + def.id);
        var b = go.AddComponent<BossUnit>();
        var p = GameLoop.I != null ? GameLoop.I.player : null;
        // Boss 站位也要落在关卡切片内，否则会出现在画面外的「空气」上
        float bx = (p != null ? p.worldX : 0f) + 7.5f;
        if (GameLoop.I != null) bx = GameLoop.I.ClampSpawnX(bx);
        b.Init(def, bx);
        boss = b;

        while (b != null && !b.dead)
        {
            if (GameLoop.I != null && GameLoop.I.gameOver) yield break;
            yield return null;
        }
        boss = null;
        yield return new WaitForSecondsRealtime(1.1f);
    }

    // ——————————— 刷怪 ———————————

    void Spawn(StageSpawn sp)
    {
        var p = GameLoop.I != null ? GameLoop.I.player : null;
        float px = p != null ? p.worldX : 0f;

        int lane;
        if (sp.z == "random") lane = Random.Range(0, PlayerCtrl.Lanes);
        else if (int.TryParse(sp.z, out var z)) lane = Mathf.Clamp(z, 0, PlayerCtrl.Lanes - 1);
        else lane = Random.Range(0, PlayerCtrl.Lanes);

        // side=both 是「两边夹」——这是密度关的主力手法：
        // 玩家不能只朝一个方向打，必须来回转身，压力瞬间不同
        if (sp.side == "both")
        {
            GameLoop.I.SpawnMob(sp.type, px + Random.Range(9f, 12f), lane);
            GameLoop.I.SpawnMob(sp.type, px - Random.Range(9f, 12f),
                                Random.Range(0, PlayerCtrl.Lanes));
            return;
        }

        bool right = sp.side != "left";
        float x = px + (right ? 1f : -1f) * Random.Range(9f, 12f);
        GameLoop.I.SpawnMob(sp.type, x, lane);
    }

    public void OnBossSpawned(BossUnit b) => boss = b;
    public void OnBossKilled(BossUnit b) { if (boss == b) boss = null; }
    public void OnBossTelegraph(float seconds) => HitFeel.Punch(0.5f);

    // ——————————— 主题 / 字幕 ———————————

    void ApplyTheme(string key)
    {
        if (chapter == null || GameLoop.I == null) return;
        if (string.IsNullOrEmpty(key)) return;
        if (chapter.themes.TryGetValue(key, out var t)) GameLoop.I.ApplyTheme(t, key);
    }

    public void Say(string text, float seconds)
    {
        if (string.IsNullOrEmpty(text)) return;
        hint = text;
        hintT = seconds;
    }

    void Update()
    {
        if (hintT > 0f) hintT -= Time.unscaledDeltaTime;
    }

    // 死亡重来：只重跑当前这一关，不从头开始
    public void StopSession()
    {
        StopAllCoroutines(); run = null; boss = null;
    }

    void OnDisable() => StopSession();

    public void RestartLevel()
    {
        StopSession();
        levelIndex = Mathf.Clamp(levelIndex, 0, chapter.levels.Count - 1);
        allClear = false; levelClear = false;
        boss = null;
        hint = "";
        hintT = 0f;
        run = StartCoroutine(RunAll());
    }

    public int SegmentProgress
    {
        get
        {
            var lv = CurLevel;
            return lv == null ? 0 : segIndex;
        }
    }
}
