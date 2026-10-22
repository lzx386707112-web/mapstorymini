using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

// Play-mode lifecycle checks. Snapshot keys are restored before the editor exits.
[InitializeOnLoad]
public static class GameplayPlayChecks
{
    const string Key = "MapleDash.GameplayPlayChecks";
    static readonly string[] SaveKeys = { "mxd.profile.v1", "mxd.profile.v1.backup" };
    static double next;
    static GameplayPlayChecks() { EditorApplication.update += Tick; }

    public static void Run()
    {
        for (int i = 0; i < SaveKeys.Length; i++)
        {
            SessionState.SetBool(Key + i + "exists", PlayerPrefs.HasKey(SaveKeys[i]));
            SessionState.SetString(Key + i, PlayerPrefs.GetString(SaveKeys[i], ""));
        }
        SessionState.SetInt(Key + "phase", 0);
        SessionState.SetBool(Key + "failed", false);
        SessionState.SetFloat(Key + "start", (float)EditorApplication.timeSinceStartup);
        SessionState.SetBool(Key, true);
        EditorApplication.EnterPlaymode();
    }

    static void Require(bool condition, string label)
    {
        if (!condition) throw new Exception("[GameplayPlayChecks] " + label);
    }

    static void Tick()
    {
        if (!SessionState.GetBool(Key, false)) return;
        int phase = SessionState.GetInt(Key + "phase", 0);
        if (phase == 9 && !EditorApplication.isPlayingOrWillChangePlaymode)
        {
            for (int i = 0; i < SaveKeys.Length; i++)
            {
                if (SessionState.GetBool(Key + i + "exists", false)) PlayerPrefs.SetString(SaveKeys[i], SessionState.GetString(Key + i, ""));
                else PlayerPrefs.DeleteKey(SaveKeys[i]);
            }
            PlayerPrefs.Save();
            SessionState.SetBool(Key, false);
            EditorApplication.Exit(SessionState.GetBool(Key + "failed", false) ? 1 : 0);
            return;
        }
        if (EditorApplication.timeSinceStartup - SessionState.GetFloat(Key + "start", 0) > 180)
        {
            Fail(new Exception("[GameplayPlayChecks] timed out")); return;
        }
        if (!EditorApplication.isPlaying || EditorApplication.isCompiling || EditorApplication.timeSinceStartup < next) return;
        var game = GameLoop.I;
        if (game == null) return;
        try
        {
            switch (phase)
            {
                case 0:
                    typeof(Profile).GetField("data", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, new PlayerProfileData());
                    if (game.player != null) game.EnterLobby();
                    game.StartGame();
                    game.stage.StopSession();
                    game.player.hp = game.player.hpMax;
                    game.player.mp = 5000;
                    game.player.invulnUntil = Time.unscaledTime + 1000;
                    next = EditorApplication.timeSinceStartup + 0.5;
                    break;
                case 1:
                    var enemy = game.SpawnMob("mushroom", game.player.worldX + 3, 2);
                    enemy.TakeDamage(9999, 1);
                    long exp = Profile.experience;
                    Require(exp == 15 && game.killCount == 1, "native experience / kill settlement");
                    game.OnEnemyKilled(enemy);
                    Require(Profile.experience == exp && game.killCount == 1, "duplicate death settlement");
                    var sys = game.player.GetComponent<SkillSystem>();
                    game.player.mp = 0;
                    UiKit.RefreshMetrics();
                    // 旧的「二指触屏放技能」已随摇杆移除：现在技能只由键位触发，
                    // 这里改成按下虚拟键，验证走的是同一条施法校验（MP 不足 → 拒绝、不进冷却）。
                    var slotKey = KeyMap.Of(GameAction.Slot1);
                    InputHub.SetVirtual(slotKey, true);
                    Require(InputHub.Pressed(GameAction.Slot1), "virtual key did not raise pressed edge");
                    Require(!sys.TryCastSkill(1121006), "zero MP cast must be rejected");
                    Require(sys.Remaining(0) == 0f, "rejected cast must not start cooldown");
                    InputHub.EndFrame();
                    InputHub.SetVirtual(slotKey, false);
                    float mp = game.player.mp;
                    Require(!sys.TryCast(0) && game.player.mp == mp, "rejected cast does not spend mana");
                    game.EnterLobby();
                    Require(game.player == null && game.stage == null && EnemyUnit.All.Count == 0 && MesoDrop.All.Count == 0, "lobby clears owned battle state");
                    next = EditorApplication.timeSinceStartup + 1;
                    break;
                case 2:
                    Require(UnityEngine.Object.FindObjectsOfType<StageDirector>().Length == 0 && EnemyUnit.All.Count == 0, "no late spawns in lobby");
                    game.StartGame();
                    Require(UnityEngine.Object.FindObjectsOfType<StageDirector>().Length == 1, "one director on reentry");
                    Require(game.player.GetComponent<SkillSystem>().Remaining(0) > 0, "lobby reentry preserves character cooldown");
                    game.stage.StopSession();
                    game.player.mp = 5000;
                    Require(game.player.GetComponent<SkillSystem>().TryCast(3), "schedule native delayed illusion");
                    game.EnterLobby();
                    next = EditorApplication.timeSinceStartup + 3.5;
                    break;
                case 3:
                    Require(EnemyUnit.All.Count == 0 && Profile.experience == 15, "no stale delayed hits after lobby");
                    game.StartGame();
                    game.stage.StopSession();
                    game.player.AddAttackBonus(999, 30);
                    game.OnPlayerDead();
                    Require(!game.player.GetComponent<SkillSystem>().TryCast(0), "dead character cannot cast");
                    next = EditorApplication.timeSinceStartup + 2;
                    break;
                case 4:
                    Require(!game.gameOver && game.player.hp == game.player.hpMax && game.player.mp == game.player.mpMax, "retry resets vitality");
                    Require(game.player.EffectiveAtk == game.player.attackDamage, "retry removes temporary buff");
                    Require(UnityEngine.Object.FindObjectsOfType<StageDirector>().Length == 1, "retry keeps one director");
                    game.EnterLobby();
                    CheckShop();
                    Profile.SetClass("priest");
                    game.StartGame(); game.stage.StopSession();
                    game.player.mp = 5000; game.player.invulnUntil = Time.unscaledTime + 1000;
                    Require(game.player.GetComponent<SkillSystem>().TryCast(3), "native summon cast");
                    var summon = UnityEngine.Object.FindObjectOfType<NativeSummon>();
                    Require(summon != null && summon.transform.parent == null, "summon is independent object");
                    Require(summon.GetComponent<SpriteRenderer>().sprite != null,"summon renders native frame");
                    var target = game.SpawnMob("snail",game.player.worldX,2);
                    target.Stun(100); target.hp = target.hpMax = 100000;
                    summon.transform.position = game.player.FeetPos + Vector2.right * 50;
                    target.transform.position = summon.transform.position + Vector3.right;
                    typeof(NativeSummon).GetField("action",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(summon,"stand");
                    typeof(NativeSummon).GetField("attackAt",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(summon,0f);
                    typeof(NativeSummon).GetMethod("Update",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(summon,null);
                    Require(target.hp < target.hpMax && Vector2.Distance(summon.Position,game.player.FeetPos)>40, "summon hits from own position");
                    typeof(NativeSummon).GetField("expires",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(summon,Time.unscaledTime-1);
                    typeof(NativeSummon).GetMethod("Update",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(summon,null);
                    Require(!summon.gameObject.activeSelf,"summon lifetime removes actor");
                    var def = game.player.GetComponent<SkillSystem>().skills[3];
                    NativeSummon.Spawn(game.player.GetComponent<SkillSystem>(),game.player,def);
                    game.EnterLobby();
                    next = EditorApplication.timeSinceStartup + .5;
                    break;
                case 5:
                    Require(UnityEngine.Object.FindObjectsOfType<NativeSummon>().Length == 0,"lobby removes summons including detached actors");
                    if(SessionState.GetBool("MapleDash.CheckAllSkillVfx",false))
                    {
                        SessionState.SetBool("MapleDash.CheckAllSkillVfx",false);
                        new GameObject("SkillVfxRuntimeChecks").AddComponent<SkillVfxRuntimeChecks>();
                        break;
                    }
                    Debug.Log("[GameplayPlayChecks] PASS lifecycle / native shop / independent summon origin, lifetime and cleanup");
                    SessionState.SetInt(Key + "phase", 9);
                    EditorApplication.ExitPlaymode();
                    return;
                case 6:
                    if(!SkillVfxRuntimeChecks.Done)return;
                    if(SkillVfxRuntimeChecks.Failure!=null)throw SkillVfxRuntimeChecks.Failure;
                    Debug.Log("[GameplayPlayChecks] PASS lifecycle and all skill VFX");
                    SessionState.SetInt(Key + "phase", 9);
                    EditorApplication.ExitPlaymode();
                    return;
            }
            SessionState.SetInt(Key + "phase", phase + 1);
        }
        catch (Exception ex) { Fail(ex); }
    }

    static void CheckShop()
    {
        Profile.AddGold(1000);
        int before = Profile.gold;
        string equipped = Profile.equipWeapon;
        Require(!Profile.TryBuyWeapon(11000,0,"1382000") && Profile.gold==before,"shop identity rejects tampering");
        Require(Profile.TryBuyWeapon(11000,0,"1302000") && Profile.gold==before-50,"native SQL price charged");
        Require(Profile.equipWeapon==equipped,"shop purchase does not equip");
    }

    static void Fail(Exception ex)
    {
        Debug.LogException(ex);
        SessionState.SetBool(Key + "failed", true);
        SessionState.SetInt(Key + "phase", 9);
        if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
    }
}
