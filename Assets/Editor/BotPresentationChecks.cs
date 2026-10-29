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
    static void Synchronize(BotField field, int mapId, int world, int channel)
    {
        // Multiple deterministic snapshots in one editor frame must bypass the real-time poll gate.
        typeof(BotField).GetField("lastSync", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).SetValue(field, float.NegativeInfinity);
        field.Synchronize(mapId, world, channel);
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
            foreach (int permit in new[] {5030000,5030001,5030002,5030004,5030008})
            {
                var employee = NativeImgAnimation.Spawn("Item/Cash/0503.img/"+permit.ToString("D8")+
                    "/employee/stand",root.transform,new Vector3(4,-2,0),300,repeat:true);
                Require(employee != null && employee.Image.sprite != null,"original hired merchant employee "+permit);
                Require(employee.transform.position == new Vector3(4,-2,0),"merchant map position retains feet anchor "+permit);
                employee.Tick(Time.realtimeSinceStartupAsDouble+60);
                Require(employee.Image.sprite != null,"merchant animation remains visible "+permit);
                employee.Remove();
            }
            var field = root.AddComponent<BotField>();
            // Edit-mode AddComponent does not run MonoBehaviour activation callbacks.
            typeof(BotField).GetMethod("OnEnable", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(field, null);
            Synchronize(field, actor.mapId, 0, 1);
            var animations = root.GetComponentsInChildren<SpriteAnim>();
            Require(animations.Length == 1, "map creates exactly one bot");
            var animation = animations[0];
            Require(animation.Renderer.sprite != null, "original stand frame composed");
            Require(animation.transform.position == new Vector3(4, -2, 0), "native feet position and PPU");
            foreach (int stance in new[] { 2, 3, 6, 7, 14, 15, 16, 17 })
            {
                actor.stance = stance; actor.moving = true;
                Synchronize(field, actor.mapId, 0, 1);
                Require(animation.Renderer.sprite != null, "original movement frame " + stance);
                string expected = stance < 4 ? "walk" : stance < 8 ? "jump" : stance < 16 ? "ladder" : "rope";
                Require(animation.Current == expected && Art.Resolve("chars",animation.key,expected,"stand") == expected, "movement must not silently fall back to stand " + stance);
                Require(animation.Renderer.flipX == ((stance & 1) == 0), "facing " + stance);
            }
            actor.moving = false; Synchronize(field, actor.mapId, 0, 1);
            Require(animation.speed == 0, "stationary rope pauses animation");
            Require(animation.Express(2), "native smile expression");
            foreach (string pose in new[] { "swingO1", "swingO2", "swingO3", "stabO1", "stabO2" })
            {
                Require(Art.GetClip("chars",animation.key,pose) != null, "original attack clip " + pose);
                BotTypeManager.Output.Send(Server.Bot.Social.BotOutput.Kind.Attack,actor,pose,4);
                Require(animation.Current == pose && animation.IsOneShot && animation.speed == 1,
                    "published native attack reaches renderer " + pose);
                Synchronize(field, actor.mapId, 0, 1);
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
                var expectedEffect = WzAssets.Texture(WzAssets.Frames(WzAssets.Node("Effect/BasicEff.img/Enchant/" + (result == 1 ? "Success" : "Failure")))[0]);
                Require(effects.Length==1 && effects[0].Image.sprite!=null &&
                    System.Linq.Enumerable.SequenceEqual(effects[0].Image.sprite.texture.GetPixels32(), expectedEffect.GetPixels32()),
                    "published scroll result reaches original success/failure IMG including curse " + result);
                Require(effects[0].transform.position==animation.transform.position && !effects[0].Image.flipX &&
                    effects[0].Image.sprite.pivot==new Vector2(16,-104),
                    "enchant follows movement anchor, retaining original 30x26 origin(16,130) without extra offset or facing flip");
                effects[0].Tick(Time.realtimeSinceStartupAsDouble+60);
                Require(root.GetComponentsInChildren<NativeImgAnimation>().Length==0,"enchant effect expires after original frames");
            }
            actor.equips[-111] = 1702000;
            Synchronize(field, actor.mapId, 0, 1);
            Require(animation.key.EndsWith("01702000", StringComparison.Ordinal), "cash weapon replaces visible recipe");
            actor.chair = 3010000; actor.stance = 4;
            Synchronize(field, actor.mapId, 0, 1);
            effects = root.GetComponentsInChildren<NativeImgAnimation>();
            Require(animation.Current == "sit" && !animation.IsOneShot && effects.Length == 1,
                "chair interrupts prior swing and creates original chair effect");
            Require(effects[0].Image.sprite.rect.size == new Vector2(58,36) &&
                effects[0].Image.sprite.pivot == new Vector2(21,52), "chair retains original Canvas size and negative-y origin");
            var brow = WzCharacter.Attachment(animation.FrameUri,"brow");
            Require(brow.y < 0 && effects[0].transform.localPosition == new Vector3(-brow.x/Art.PPU,-brow.y/Art.PPU,0),
                "pos1 chair follows composed brow anchor rather than guessed feet offset");
            Require(effects[0].Image.flipX && effects[0].Image.sortingOrder == animation.Renderer.sortingOrder-1,
                "chair direction and rear z come from original effect");
            actor.stance = 5; Synchronize(field, actor.mapId,0,1);
            Require(!effects[0].Image.flipX && effects[0].transform.localPosition.x == brow.x/Art.PPU,
                "chair anchor and image both follow facing changes");
            actor.chair = 0; Synchronize(field, actor.mapId,0,1);
            Require(root.GetComponentsInChildren<NativeImgAnimation>().Length == 0 && animation.Current == "stand",
                "standing removes chair effect and restores native stance");
            foreach (int mount in new[] {1902000,1902001,1902002,1902005,1902008,1902009,1902011,1902012})
            {
                actor.mountActive = true; actor.mountItemId = mount;
                foreach (int stance in new[] {4,2,6,14,16})
                {
                    actor.stance = stance; Synchronize(field,actor.mapId,0,1);
                    Require(animation.key.EndsWith("_mount_"+mount,StringComparison.Ordinal) && animation.Renderer.sprite != null,
                        "original mount plus rider composed " + mount + "/" + stance);
                    var navel = WzCharacter.Attachment(animation.FrameUri,"navel");
                    Require(navel.y < 0,"rider follows native mount navel " + mount + "/" + stance);
                    if (mount == 1902000 && stance == 4) Require(navel == new Vector2Int(3,-51),"hog saddle preserves original navel (3,-51)");
                    Require(animation.Renderer.sprite.rect.width >= 40,"mount original Canvas is included " + mount);
                }
            }
            actor.mountActive = false; actor.stance = 4; Synchronize(field,actor.mapId,0,1);
            Require(!animation.key.Contains("_mount_"),"dismount restores original unmounted appearance");
            Synchronize(field, actor.mapId, 0, 2);
            Require(root.GetComponentsInChildren<SpriteAnim>().Length == 0, "other channel removes entity");
            CheckCombatAdapter(root,actor);
            CheckReactors(root);
            Debug.Log("[BotPresentationChecks] PASS native paper doll / feet / movement / facing / rope pause / expression / attack output / cash weapon / channel cleanup");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); BotRegistry.Remove(actor); }
    }
    public static void RunReactors()
    {
        var root = new GameObject("ReactorPresentationChecks");
        try { CheckReactors(root); Debug.Log("[ReactorPresentationChecks] PASS original stand / old-state hit / new-state loop / respawn"); }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }
    static void CheckReactors(GameObject root)
    {
        const int mapId = 920010000;
        var map = new Server.Field.MapRuntime {mapId = mapId};
        map.reactors.Add(new Server.Field.MapReactorSpawn {id = 2002001, x = 120, y = -40});
        Server.Field.MapService.Register(map); Server.Field.MapReactorService.Clear(mapId);
        long now = 1000; Server.Field.MapReactorService.Clock = () => now;
        var go = new GameObject("OriginalReactorFixture"); go.transform.SetParent(root.transform);
        var view = go.AddComponent<NativeReactorView>(); view.Bind(mapId);
        try
        {
            view.Tick(now);
            var renderer = go.GetComponentInChildren<SpriteRenderer>();
            Require(renderer != null && renderer.sprite != null, "reactor original stand Canvas exists");
            Require(renderer.transform.localPosition == new Vector3(3,1,0), "reactor uses raw map position and shared PPU");
            Require(renderer.sprite.rect.width == 106 && renderer.sprite.pivot == new Vector2(53,36), "cloud uses original 106x72 origin 53,36");
            var reactor = Server.Field.MapReactorService.Get(mapId)[0];
            Server.Field.MapReactorService.Hit(mapId,reactor.objectId); view.Tick(now);
            now += 121; view.Tick(now);
            Require(renderer.sprite.rect.width == 111 && renderer.sprite.pivot == new Vector2(53,37), "state transition plays old state hit second original Canvas");
            now += 10000; view.Tick(now);
            var expected = WzAssets.Sprite(WzAssets.FileRoot("Reactor/2002001.img").Get("1/0"), "reactor:2002001/1/0");
            Require(renderer.sprite == expected, "after hit animation loops new state");
            Server.Field.MapReactorService.Respawn(mapId,reactor); view.Tick(now);
            Require(renderer.sprite.rect.width == 106, "respawn resets original stand without replaying stale hit");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(go); Server.Field.MapReactorService.Clear(mapId);
            Server.Field.MapReactorService.Clock = () => 0;
        }
    }
}
