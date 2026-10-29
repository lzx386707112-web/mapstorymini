using System;
using System.IO;
using System.Reflection;
using Server.Field;
using UnityEditor;
using UnityEngine;

public static class GroundItemChecks
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    const BindingFlags StaticPrivate = BindingFlags.Static | BindingFlags.NonPublic;
    [Serializable] sealed class Oracle { public string sha256; public Case[] cases, flights; }
    [Serializable] sealed class Case { public int sx, sy, tx, ty, ms, x, y; }
    static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    static void Set(object obj, string field, object value) => obj.GetType().GetField(field, Private).SetValue(obj, value);

    public static void Run()
    {
        GameObject playerObject = null, bridgeObject = null;
        try
        {
            long itemClock=0;
            var previousClock=MapItemService.Clock;
            MapItemService.Clock=()=>itemClock;
            MapService.Register(new MapRuntime { mapId=987654321 });
            MapService.Register(new MapRuntime { mapId=987654322,everlast=true });
            var expires=MapItemService.SpawnMesoDrop(987654321,10,0,0,0,0,-1,false,2);
            var stays=MapItemService.SpawnMesoDrop(987654322,10,0,0,0,0,-1,false,2);
            itemClock=180000; MapItemService.Tick(itemClock);
            Require(MapItemService.Find(987654321,expires.objectId)!=null,"Drop expired at inclusive boundary");
            itemClock++; MapItemService.Tick(itemClock);
            Require(MapItemService.Find(987654321,expires.objectId)==null && MapItemService.Find(987654322,stays.objectId)!=null,"Everlast/expiry lifecycle");
            MapItemService.ClearMap(987654321); MapItemService.ClearMap(987654322);
            MapService.Unregister(987654321); MapService.Unregister(987654322); MapItemService.Clock=previousClock;
            var oracle = JsonUtility.FromJson<Oracle>(File.ReadAllText("tools/native_drop_oracle.json"));
            foreach (var c in oracle.cases)
            {
                NativeDropMotion.Pickup(c.sx, c.sy, c.tx, c.ty, c.ms, out int x, out int y, out _);
                Require(x == c.x && y == c.y, "x86 pickup mismatch at " + c.ms + "ms: " + x + "," + y + " != " + c.x + "," + c.y);
            }
            foreach (var c in oracle.flights)
            {
                NativeDropMotion.Flight(c.sx, c.sy, c.tx, c.ty, c.ms, out int x, out int y, out _);
                Require(x == c.x && y == c.y, "x86 flight mismatch at " + c.ms + "ms");
            }
            Require(NativeDropMotion.MesoTier(49) == 0 && NativeDropMotion.MesoTier(50) == 1 &&
                NativeDropMotion.MesoTier(99) == 1 && NativeDropMotion.MesoTier(100) == 2 &&
                NativeDropMotion.MesoTier(999) == 2 && NativeDropMotion.MesoTier(1000) == 3, "Meso thresholds");
            Require(NativeDropMotion.MesoFrame(1, 240) == 3 && NativeDropMotion.MesoFrame(1, 320) == 0 &&
                NativeDropMotion.MesoFrame(100, 600) == 3 && NativeDropMotion.MesoFrame(1000, 3999) == 0 &&
                NativeDropMotion.MesoFrame(1000, 4000) == 1 && NativeDropMotion.MesoFrame(1000, 4240) == 3 &&
                NativeDropMotion.MesoFrame(1000, 4360) == 0, "Meso frame schedule");
            Require(NativeDropMotion.InPickupRange(0, 0, -25, -50) &&
                !NativeDropMotion.InPickupRange(0, 0, 25, 0) && !NativeDropMotion.InPickupRange(0, 0, 0, 10), "Pickup rectangle edges");
            NativeDropMotion.Flight(0, 0, 50, 0, 510, out int fx, out int fy, out bool landed);
            Require(fx == 26 && fy == -100 && !landed, "Ordinary fling midpoint");
            NativeDropMotion.Flight(0, 0, 50, 60, 1140, out fx, out fy, out landed);
            Require(!landed && fy == 48, "Lower-platform constant-speed fall");
            NativeDropMotion.Flight(0, 0, 50, 60, 1170, out fx, out fy, out landed);
            Require(landed && fy == 60 && fx == 50, "Lower-platform landing");

            foreach (int amount in new[] { 1, 50, 100, 1000 })
            {
                var view = GroundItem.Spawn(0, amount, Vector3.zero, alreadyOnGround: true);
                Require(view != null, "Original meso art unavailable");
                var frames = (Sprite[])typeof(GroundItem).GetField("frames", Private).GetValue(view);
                Require(frames.Length == 4, "Missing fourth original meso frame");
                UnityEngine.Object.DestroyImmediate(view.gameObject);
            }
            var equipment = GroundItem.Spawn(1002000, 1, Vector3.zero, alreadyOnGround: true);
            Require(equipment != null && equipment.GetComponent<SpriteRenderer>().sprite != null, "Original equipment icon");
            var coin = GroundItem.Spawn(0, 1, Vector3.zero, alreadyOnGround: true);
            var coinRenderer = coin.GetComponent<SpriteRenderer>();
            Require(Mathf.Abs(coinRenderer.bounds.min.y * Art.PPU - 4) < 0.01f,
                "Small coin moved away from original origin.y=28, height=24 baseline");
            foreach (int id in new[] { 1002000, 2000000, 4000000 })
            {
                var itemView = GroundItem.Spawn(id, 1, Vector3.zero, alreadyOnGround: true);
                Require(itemView != null, "Missing baseline test icon " + id);
                foreach (int ms in new[] { 0, 240, 600, 1200 })
                {
                    typeof(GroundItem).GetMethod("RenderAt", Private).Invoke(coin, new object[] { ms });
                    typeof(GroundItem).GetMethod("RenderAt", Private).Invoke(itemView, new object[] { ms });
                    Require(Mathf.Abs(itemView.GetComponent<SpriteRenderer>().bounds.min.y - coinRenderer.bounds.min.y) * Art.PPU < 0.01f,
                        "Item/coin ground baseline mismatch: " + id + " at " + ms);
                }
                UnityEngine.Object.DestroyImmediate(itemView.gameObject);
            }
            UnityEngine.Object.DestroyImmediate(coin.gameObject);
            UnityEngine.Object.DestroyImmediate(equipment.gameObject);

            playerObject = new GameObject("PickupPlayerCheck");
            var player = playerObject.AddComponent<PlayerCtrl>();
            typeof(PlayerCtrl).GetMethod("Awake", Private).Invoke(player, null);
            PlayerCtrl.SetBounds(-20, 20);
            PlayerCtrl.SetTerrain(new System.Collections.Generic.List<Vector4> { new Vector4(-20, 0, 20, 0) });
            player.PlaceAt(0, 0);
            bridgeObject = new GameObject("PickupBridgeCheck");
            bridgeObject.AddComponent<GroundItemBridge>().Bind(null, null);
            var item = new MapItem { objectId = 987654, meso = 10, pickupCharacterId = GameSession.CharacterId };
            var drop = GroundItem.Spawn(0, 10, Vector3.zero, item, alreadyOnGround: true);
            // Edit-mode executeMethod does not dispatch ordinary MonoBehaviour.OnEnable.
            if (!GroundItem.All.Contains(drop)) typeof(GroundItem).GetMethod("OnEnable", Private).Invoke(drop, null);
            int requests = 0;
            bool allow = false;
            GroundItem.AuthoritativePickup = candidate =>
            {
                requests++;
                if (allow) MapItemService.PickItemDrop(0, candidate, 2, GameSession.CharacterId);
                return allow;
            };
            var step = typeof(PlayerCtrl).GetMethod("Step", Private);
            TouchInput.Reset();
            for (int i = 0; i < 120; i++) step.Invoke(player, new object[] { 1f / 60 });
            Require(requests == 0 && !item.pickedUp && drop.gameObject.activeSelf, "Standing/walking step auto-picked a drop");
            var pickup = typeof(GroundItem).GetMethod("PickupInRange", StaticPrivate);
            Require(!(bool)pickup.Invoke(null, new object[] { Vector2.zero }) && requests == 1 && !item.pickedUp,
                "Refused pickup changed entity");
            allow = true;
            Require((bool)pickup.Invoke(null, new object[] { Vector2.zero }) && requests == 2 && item.pickedUp,
                "Active pickup did not reach authority");
            Require(item.pickupCharacterId == GameSession.CharacterId && drop.gameObject.activeSelf && drop.enabled &&
                GroundItem.All.Contains(drop) && (bool)typeof(GroundItem).GetField("picking", Private).GetValue(drop),
                "Synchronous Removed event hid/destroyed the animated drop");
            Require(!(bool)pickup.Invoke(null, new object[] { Vector2.zero }) && requests == 2, "Duplicate pickup during animation");
            // Use a moving picker target; preserve scale and original canvas, fade only after 420ms.
            Set(drop, "pickupTarget", (Func<Vector2?>)(() => new Vector2(1, 1)));
            Set(drop, "removedAt", Time.realtimeSinceStartupAsDouble - 0.5);
            typeof(GroundItem).GetMethod("RenderAt", Private).Invoke(drop, new object[] { 1500 });
            Require(drop.transform.localScale == Vector3.one && drop.GetComponent<FxLife>() == null &&
                Mathf.Abs(drop.GetComponent<SpriteRenderer>().color.a - 201 / 255f) < 0.01f,
                "Pickup shrink/sparkle or fade timeline regressed");
            Require(!NativeDropMotion.Pickup(0, 0, 1, 1, 700, out _, out _, out _), "Pickup did not end at 700ms");
            Require(!File.ReadAllText("Assets/Scripts/PlayerCtrl.cs").Contains("TryAutoPickup"), "Automatic pickup call restored");
            Debug.Log("[GroundItemChecks] PASS " + oracle.cases.Length + " x86 pickup + " + oracle.flights.Length + " x86 flight cases; original four-tier/four-frame art and timing; rectangle/flight; no automatic pickup; refused/accepted requests; synchronous bridge retains visual; no duplicate pickup/shrink/sparkle");
            EditorApplication.Exit(0);
        }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        finally
        {
            GroundItem.AuthoritativePickup = null;
            foreach (var view in GroundItem.All.ToArray()) if (view != null) UnityEngine.Object.DestroyImmediate(view.gameObject);
            if (bridgeObject != null) UnityEngine.Object.DestroyImmediate(bridgeObject);
            if (playerObject != null) UnityEngine.Object.DestroyImmediate(playerObject);
            TouchInput.Reset();
        }
    }
}
