using System;
using Server.Bot;
using UnityEngine;

// 用真实IMG合成和地图实体校验；不创建/改写游戏存档。
public static class BotPresentationChecks
{
    static void Require(bool condition, string message)
    { if (!condition) throw new Exception("[BotPresentationChecks] " + message); }
    static void CheckCombatAdapter(GameObject root,BotCharacter actor)
    {
        var db=DbStore.Create(System.IO.Path.Combine(System.IO.Path.GetTempPath(),"mapledash_bot_img_combat.json"));
        int world=actor.world,channel=actor.channel;
        using var runtime=new BotRuntime(db);
        var director=root.AddComponent<FieldDirector>();
        typeof(FieldDirector).GetProperty("Map").SetValue(director,new FieldMap { id=actor.mapId });
        var adapter=root.AddComponent<BotCombatField>(); adapter.Bind(director,runtime);
        var enemyObject=new GameObject("OriginalSnailDamageFixture"); enemyObject.transform.SetParent(root.transform);
        var enemy=enemyObject.AddComponent<EnemyUnit>(); enemy.fieldMode=true; enemy.fieldMobId=100100;
        if (!EnemyUnit.All.Contains(enemy)) EnemyUnit.All.Add(enemy);
        try
        {
            enemy.BindNative(director); enemy.hpMax=enemy.NativeController.MaximumHp; enemy.hp=enemy.hpMax;
            actor.world=GameSession.World; actor.channel=GameSession.Channel;
            var snapshot=runtime.partyGrind.Monsters(actor.mapId,actor.world,actor.channel);
            var target=snapshot.Find(m=>m.id==enemy.GetInstanceID());
            Require(target!=null && target.maxHp==enemy.NativeController.MaximumHp,"adapter reads original native mob HP and live Unity identity");
            long before=enemy.NativeController.CurrentHp;
            runtime.partyGrind.Hit(actor,target,2,new BotAttack.Skill(0,0,1,BotAttack.PacketKind.CLOSE),new[] {2});
            Require(enemy.NativeController.CurrentHp==before-2 && enemy.DamageContributions[actor.id]==2 && enemy.HasBotContribution,
                "real bot adapter deducts native HP and attributes actual damage to bot");
            actor.channel++;
            runtime.partyGrind.Hit(actor,target,2,new BotAttack.Skill(0,0,1,BotAttack.PacketKind.CLOSE),new[] {2});
            Require(enemy.NativeController.CurrentHp==before-2,"stale other-channel attack cannot damage current field");
        }
        finally
        {
            actor.world=world; actor.channel=channel;
            EnemyUnit.All.Remove(enemy); UnityEngine.Object.DestroyImmediate(enemyObject);
            UnityEngine.Object.DestroyImmediate(adapter); UnityEngine.Object.DestroyImmediate(director);
        }
    }
    public static void Run()
    {
        var root = new GameObject("BotPresentationChecks");
        var actor = new BotCharacter { id = int.MinValue + 100, face = 20000, hair = 30000,
            x = 160, y = 80, world = 0, channel = 1 };
        actor.equips[-5] = 1040002;
        actor.equips[-6] = 1060002;
        actor.equips[-7] = 1072001;
        actor.equips[-11] = 1302000;
        BotRegistry.Add(actor); BotRegistry.EnterMap(actor, 100000000);
        try
        {
            var field = root.AddComponent<BotField>();
            // Edit-mode AddComponent does not run MonoBehaviour activation callbacks.
            field.SendMessage("OnEnable");
            field.Synchronize(actor.mapId, 0, 1);
            var animations = root.GetComponentsInChildren<SpriteAnim>();
            Require(animations.Length == 1, "map creates exactly one bot");
            var animation = animations[0];
            Require(animation.Renderer.sprite != null, "original stand frame composed");
            Require(animation.transform.position == new Vector3(4, -2, 0), "native feet position and PPU");
            foreach (int stance in new[] { 2, 3, 6, 7, 14, 15, 16, 17 })
            {
                actor.stance = stance; actor.moving = true;
                field.Synchronize(actor.mapId, 0, 1);
                Require(animation.Renderer.sprite != null, "original movement frame " + stance);
                string expected = stance < 4 ? "walk" : stance < 8 ? "jump" : stance < 16 ? "ladder" : "rope";
                Require(animation.Current == expected && Art.Resolve("chars",animation.key,expected,"stand") == expected, "movement must not silently fall back to stand " + stance);
                Require(animation.Renderer.flipX == ((stance & 1) == 0), "facing " + stance);
            }
            actor.moving = false; field.Synchronize(actor.mapId, 0, 1);
            Require(animation.speed == 0, "stationary rope pauses animation");
            Require(animation.Express(2), "native smile expression");
            foreach (string pose in new[] { "swingO1", "swingO2", "swingO3", "stabO1", "stabO2" })
            {
                Require(Art.GetClip("chars",animation.key,pose) != null, "original attack clip " + pose);
                BotTypeManager.Output.Send(Server.Bot.Social.BotOutput.Kind.Attack,actor,pose,4);
                Require(animation.Current == pose && animation.IsOneShot && animation.speed == 1,
                    "published native attack reaches renderer " + pose);
                field.Synchronize(actor.mapId,0,1);
                Require(animation.Current == pose && animation.IsOneShot,"base synchronization preserves swing " + pose);
            }
            field.PlaySkillEffect(actor,1001005);
            var effects=root.GetComponentsInChildren<NativeImgAnimation>();
            Require(effects.Length==1 && effects[0].Image.sprite!=null,"original Slash Blast effect reaches bot renderer");
            effects[0].Tick(Time.realtimeSinceStartupAsDouble+60);
            Require(root.GetComponentsInChildren<NativeImgAnimation>().Length==0,"one-shot skill IMG expires without orphan objects");
            foreach (int result in new[] {1,2,0})
            {
                BotTypeManager.Output.Send(Server.Bot.Social.BotOutput.Kind.Scroll,actor,value:result);
                effects=root.GetComponentsInChildren<NativeImgAnimation>();
                Require(effects.Length==1 && effects[0].Image.sprite!=null &&
                    effects[0].name.EndsWith(result==1 ? "/Success" : "/Failure",StringComparison.Ordinal),
                    "published scroll result reaches original success/failure IMG including curse " + result);
                Require(effects[0].transform.position==animation.transform.position && !effects[0].Image.flipX &&
                    effects[0].Image.sprite.pivot==new Vector2(16,-104),
                    "enchant follows movement anchor, retaining original 30x26 origin(16,130) without extra offset or facing flip");
                effects[0].Tick(Time.realtimeSinceStartupAsDouble+60);
                Require(root.GetComponentsInChildren<NativeImgAnimation>().Length==0,"enchant effect expires after original frames");
            }
            actor.equips[-111] = 1702000;
            field.Synchronize(actor.mapId, 0, 1);
            Require(animation.key.EndsWith("01702000", StringComparison.Ordinal), "cash weapon replaces visible recipe");
            field.Synchronize(actor.mapId, 0, 2);
            Require(root.GetComponentsInChildren<SpriteAnim>().Length == 0, "other channel removes entity");
            CheckCombatAdapter(root,actor);
            Debug.Log("[BotPresentationChecks] PASS native paper doll / feet / movement / facing / rope pause / expression / attack output / cash weapon / channel cleanup");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); BotRegistry.Remove(actor); }
    }
}
