// 权威地面掉落（Server.Field.MapItemService）↔ 原IMG地面物品显示（GroundItem）之间的桥。
// 后端负责实体/归属/过期/背包结算，这里只做两件事：
//   1. 把 MapItemService.Spawned/Removed 事件转成 GroundItem 的生成与销毁（图标沿用原 IMG，不新增自绘）；
//   2. 把 GroundItem 的拾取请求转成 MapItemPickup.PickupItem（源端 Character.pickupItem 语义）。
// 坐标约定与 FieldDirector 同步真人一致：mapX = unityX * Art.PPU，mapY = -unityY * Art.PPU。
using System.Collections.Generic;
using Server.Bot;
using Server.Field;
using UnityEngine;

public sealed class GroundItemBridge : MonoBehaviour
{
    FieldDirector owner;
    BotRuntime runtime;
    BotActorLooter looter;
    int looterId = -1;
    System.Func<MapItem, bool> pickupHandler;
    bool subscribed;

    public void Bind(FieldDirector field, BotRuntime bots)
    {
        owner = field; runtime = bots;
        MapItemService.ExpireMilliseconds=GameServer.Local==null ? 180000 : GameConfigDao.GetInt(GameServer.Local.Db,"item_expire_time",180000);
        if (subscribed) return;
        pickupHandler = TryPickup;
        MapItemService.Spawned += OnSpawned;
        MapItemService.Removed += OnRemoved;
        GroundItem.AuthoritativePickup = pickupHandler;
        subscribed = true;
    }

    public void Unbind()
    {
        if (!subscribed) return;
        subscribed = false;
        MapItemService.Spawned -= OnSpawned;
        MapItemService.Removed -= OnRemoved;
        if (ReferenceEquals(GroundItem.AuthoritativePickup, pickupHandler)) GroundItem.AuthoritativePickup = null;
        pickupHandler = null;
        looter = null; looterId = -1;
    }

    void OnDisable() => Unbind();
    void OnDestroy() => Unbind();

    BotActorLooter Looter()
    {
        if (looter != null && looterId == GameSession.CharacterId) return looter;
        looter = null; looterId = -1;
        if (runtime == null) return null;
        var actor = runtime.Player(GameSession.CharacterId);
        if (actor == null) return null;
        looter = new BotActorLooter(actor, runtime.trades, runtime.parties,
            new BotLootHooks { NeedQuestItem=QuestLogic.NeedQuestItem });
        looterId = actor.id;
        return looter;
    }

    int CurrentMapId => owner != null && owner.Map != null ? owner.Map.id : -1;

    static Vector3 ToUnity(MapItem item) => new Vector3(item.x / Art.PPU, -item.y / Art.PPU, 0f);

    void OnSpawned(MapItem item)
    {
        if (item == null || item.mapId != CurrentMapId) return;
        if (!item.IsMeso && !QuestLogic.NeedQuestItem(item.questId,item.ItemId)) return;
        foreach (var view in GroundItem.All)
            if (view != null && view.bound != null && view.bound.objectId == item.objectId) return;   // 已有显示
        // 金币：GroundItem 用 itemId==0 表示，amount 为金额
        var actor = BotRegistry.Get(item.dropperId);
        var reactor = MapReactorService.Find(item.mapId,item.dropperId);
        Vector3 from = actor != null && actor.mapId == item.mapId
            ? new Vector3(actor.x / Art.PPU, -actor.y / Art.PPU, 0)
            : reactor!=null ? new Vector3(reactor.x/Art.PPU,-reactor.y/Art.PPU,0) : ToUnity(item);
        GroundItem.Spawn(item.IsMeso ? 0 : item.ItemId, item.IsMeso ? item.meso : item.Quantity,
            ToUnity(item), item, from);
    }

    void OnRemoved(MapItem item, int reason)
    {
        if (item == null) return;
        // 不维护额外索引：GroundItem.All 是唯一显示来源，地图切换 GroundItem.Clear() 一并清干净。
        foreach (var view in GroundItem.All.ToArray())
            if (view != null && view.bound != null && view.bound.objectId == item.objectId)
            {
                System.Func<Vector2?> target = null;
                if (item.pickupCharacterId == GameSession.CharacterId && item.pickupPetIndex < 0)
                    target = GroundItem.LocalPickupTarget;
                else if (item.pickupCharacterId != 0 && item.pickupPetIndex < 0)
                {
                    int picker = item.pickupCharacterId;
                    target = () => owner != null ? owner.GetComponent<BotField>()?.PickupTarget(picker) : null;
                }
                view.Remove(reason, target, item.pickupCharacterId == GameSession.CharacterId);
            }
    }

    bool TryPickup(MapItem item)
    {
        var actor = Looter();
        if (item == null || actor == null) return false;
        if (item.mapId != CurrentMapId) return false;
        // 源端 Character.pickupItem：400ms 延迟、canBePickedBy、背包空间、addFromDrop/gainMeso
        var player=runtime.Player(GameSession.CharacterId);
        int before=player.character.record.meso;
        bool picked=MapItemPickup.PickupItem(item, actor, (long)(Time.realtimeSinceStartup * 1000));
        if (picked) GameSession.ApplyGroundPickup(player,player.character.record.meso-before);
        return picked;
    }
}
