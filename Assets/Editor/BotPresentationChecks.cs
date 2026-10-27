using System;
using Server.Bot;
using UnityEngine;

// 用真实IMG合成和地图实体校验；不创建/改写游戏存档。
public static class BotPresentationChecks
{
    static void Require(bool condition, string message)
    { if (!condition) throw new Exception("[BotPresentationChecks] " + message); }
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
                Require(animation.Current == expected, "movement must not silently fall back to stand " + stance);
                Require(animation.Renderer.flipX == ((stance & 1) == 0), "facing " + stance);
            }
            actor.moving = false; field.Synchronize(actor.mapId, 0, 1);
            Require(animation.speed == 0, "stationary rope pauses animation");
            Require(animation.Express(2), "native smile expression");
            actor.equips[-111] = 1702000;
            field.Synchronize(actor.mapId, 0, 1);
            Require(animation.key.EndsWith("01702000", StringComparison.Ordinal), "cash weapon replaces visible recipe");
            field.Synchronize(actor.mapId, 0, 2);
            Require(root.GetComponentsInChildren<SpriteAnim>().Length == 0, "other channel removes entity");
            Debug.Log("[BotPresentationChecks] PASS native paper doll / feet / movement / facing / rope pause / expression / cash weapon / channel cleanup");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); BotRegistry.Remove(actor); }
    }
}
