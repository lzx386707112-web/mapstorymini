using UnityEngine;

// Source UseItemHandler and dedicated equipment / chair / cash handlers use distinct routes.
public static class NativeInventoryActions
{
    public static void DoubleClick(int itemId, int inventoryPosition = 0)
    {
        string error = TryUse(itemId, inventoryPosition);
        if (!string.IsNullOrEmpty(error)) BottomBar.Toast(error);
    }

    // null means handled; unsupported routes retain the item.
    public static string TryUse(int itemId, int inventoryPosition = 0)
    {
        if (!GameSession.InGame || NpcSession.Blocking) return "当前无法使用物品";
        var player = GameLoop.I?.player;
        if (player == null || player.hp <= 0) return "当前无法使用物品";
        if (itemId / 1000000 == 1) return EquipLoadout.Equip(itemId, Profile.Attributes, inventoryPosition);
        if (Profile.ItemCount(itemId) <= 0) return "背包里没有这个物品";
        if (itemId / 1000000 == 2)
        {
            // UseItemHandler.handleCureItems consumes even without a matching disease.
            if (itemId >= 2050001 && itemId <= 2050004)
            {
                if (!Profile.RemoveItem(itemId, 1)) return "背包里没有这个物品";
                var diseases = player.GetComponent<NativeMobDebuffs>();
                if (itemId == 2050001) diseases?.Clear(121);
                else if (itemId == 2050002) { diseases?.Clear(122); diseases?.Clear(126); }
                else if (itemId == 2050003) { diseases?.Clear(120); diseases?.Clear(124); }
                else foreach (int disease in new[] { 120, 121, 122, 124, 125, 126 }) diseases?.Clear(disease);
                GameSession.SaveToDb();
                return null;
            }
            var spec = NativeItemSource.Node(itemId)?.Get("spec");
            if (!string.IsNullOrEmpty(spec?.Text("script")))
                return NpcSession.UseItem(itemId) != null ? null : "物品脚本尚未接入";
            if (NpcSession.UseRestorative(itemId)) return null;
            return "该消耗品的特殊使用功能尚未接入";
        }
        if (itemId / 10000 == 301) return "椅子使用功能尚未接入";
        if (itemId / 1000000 == 5) return "该现金道具的使用功能尚未接入";
        return "这个物品不能直接使用";
    }
}
