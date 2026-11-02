using System.Collections.Generic;

// 源端 Channel.java 武陵道场分配（L494-700）的单机等价实现。
// 位图 usedDojo：bit0-4 = 队伍道场 5 槽，bit5-19 = 个人道场 15 槽；
// getDojoSlot(mapid)（源端 L584）= mapid%100 + (mapid/10000==92502 ? 5 : 0)。
// ingressDojo（源端 L507）：找第一个空位 → slotMapid = base + 100*(fromStage+1) + slot →
//   队伍查重（-2 已登录）→ usedDojo 置位 → resetDojo + startDojoSchedule(限时踢出)。
// 单机差异（都有明确依据，不是简化掉）：
//   - resetDojo/resetDojoMap：本端地图按需加载，进图时 FieldDirector 全新重建刷怪点，
//     「重置地图对象」天然成立，无需额外动作；
//   - startDojoSchedule 的超时踢出：源端 TimerManager 到点把道场图上的人 changeMap(DOJO_EXIT)
//     再释放槽位；单机在 Tick() 惰性检查，超时把本地角色送回 925020002 后释放；
//   - freeDojoSectionIfEmpty（源端 L588）：5 张连续 stage 图都无人才释放。脚本在
//     cm.warp(925020002) 之后立即调用，而本端 warp 是排队到帧末生效，调用瞬间玩家
//     仍登记在道场图上——所以这里按「调用即离开意图」处理：玩家已不在道场图、或正在
//     离场链路中，都释放该槽位，避免永久占用。
public static class NativeDojoService
{
    public const int PartyBase = 925030000;   // MapId.DOJO_PARTY_BASE
    public const int SoloBase = 925020000;    // MapId.DOJO_SOLO_BASE
    public const int DojoExit = 925020002;    // MapId.DOJO_EXIT
    const int PartyRange = 5, SoloRange = 15;

    static int usedDojo;
    static readonly Dictionary<long, int> dojoParty = new Dictionary<long, int>();
    static readonly int[] dojoStage = new int[20];
    static readonly long[] dojoFinishTime = new long[20];
    static readonly int[] dojoFinishStage = new int[20];

    // Channel.lookupPartyDojo(party)：队伍当前占用的道场槽位，-1 = 没有。
    public static int LookupPartyDojo(long partyId)
        => partyId != 0 && dojoParty.TryGetValue(partyId, out int slot) ? slot : -1;

    // Channel.ingressDojo(isPartyDojo, party, fromStage)：-1 全满，-2 队伍已登录，其余 = 新槽位。
    public static int IngressDojo(bool isPartyDojo, long partyId, int fromStage)
    {
        int list = usedDojo, range, slot = 0;
        if (!isPartyDojo) { list >>= 5; range = SoloRange; }
        else range = PartyRange;
        while ((list & 1) != 0) { list >>= 1; slot++; }
        if (slot >= range) return -1;

        int slotMapId = (isPartyDojo ? PartyBase : SoloBase) + 100 * (fromStage + 1) + slot;
        int dojoSlot = SlotOf(slotMapId);
        if (isPartyDojo)
        {
            if (partyId != 0 && dojoParty.ContainsKey(partyId)) return -2;
            if (partyId != 0) dojoParty[partyId] = dojoSlot;
        }
        usedDojo |= 1 << dojoSlot;
        StartSchedule(slotMapId);
        return slot;
    }

    // Channel.resetDojoMap(fromMapId)：firstEnter 时把后续连续段重置一遍。
    // 本端进图即全新刷怪（见类注释），保留入口只为脚本面完整。
    public static void ResetDojoMap(int fromMapId) { }

    // Channel.freeDojoSectionIfEmpty(dojoMapId)。
    public static void FreeDojoSectionIfEmpty(int mapId)
    {
        int slot = SlotOf(mapId);
        if ((usedDojo & (1 << slot)) == 0) return;
        ReleaseSlot(slot, PartyIdOfLocal());
    }

    // Channel.dismissDojoSchedule(dojoMapId, party)：取消超时踢出并释放（源端事件脚本用）。
    public static void DismissDojoSchedule(int mapId, long partyId)
    {
        int slot = SlotOf(mapId);
        dojoFinishTime[slot] = 0;
        ReleaseSlot(slot, partyId);
    }

    // Channel.setDojoProgress(dojoMapId)：记录该槽位已推进到的最高 stage。
    public static bool SetDojoProgress(int mapId)
    {
        int slot = SlotOf(mapId);
        int stage = (mapId / 100) % 100;
        if (dojoStage[slot] < stage) { dojoStage[slot] = stage; return true; }
        return false;
    }

    public static long GetDojoFinishTime(int mapId) => dojoFinishTime[SlotOf(mapId)];

    // 源端 startDojoSchedule 的超时时长（L623）：stage>36 → 15 分钟，否则 (stage/6)+5 分钟。
    public static long StageClockMillis(int stage) => (long)(stage > 36 ? 15 : stage / 6 + 5) * 60000;

    static void StartSchedule(int slotMapId)
    {
        int slot = SlotOf(slotMapId);
        int stage = (slotMapId / 100) % 100;
        if (stage <= dojoStage[slot]) return;
        dojoFinishStage[slot] = stage;
        dojoFinishTime[slot] = ServerClock() + StageClockMillis(stage) + 3000;   // 源端 TIMES UP 展示 3 秒
    }

    // GameLoop.Update 每帧调（源端 TimerManager.schedule 的等价物）。
    // 超时：道场图上的本地角色 changeMap(DOJO_EXIT)，然后释放槽位（源端 L625-650）。
    public static void Tick()
    {
        long now = ServerClock();
        for (int slot = 0; slot < 20; slot++)
        {
            if (dojoFinishTime[slot] == 0 || now < dojoFinishTime[slot]) continue;
            dojoFinishTime[slot] = 0;
            long partyId = PartyIdOfLocal();
            var field = GameLoop.I != null ? GameLoop.I.field : null;
            if (field?.Map != null && IsDojo(field.Map.id))
                NpcSession.MapContext().RequestWarp(DojoExit);
            ReleaseSlot(slot, partyId);
        }
    }

    // Channel.freeDojoSlot(slot, party)：清位图；队伍键优先，找不到时按值反查（源端 L555）。
    static void ReleaseSlot(int slot, long partyId)
    {
        usedDojo &= ~(1 << slot);
        dojoFinishTime[slot] = 0;
        if (partyId != 0 && dojoParty.Remove(partyId)) return;
        long stale = 0;
        foreach (var entry in dojoParty)
            if (entry.Value == slot) { stale = entry.Key; break; }
        if (stale != 0) dojoParty.Remove(stale);
    }

    // getDojoSlot（源端 L584）。
    public static int SlotOf(int dojoMapId)
        => dojoMapId % 100 + ((dojoMapId / 10000 == 92502) ? 5 : 0);

    // MapId.isDojo：道场图段 925020000..925020599 / 925030000..925033804。
    public static bool IsDojo(int mapId)
        => (mapId >= SoloBase && mapId < 925020600) || (mapId >= PartyBase && mapId <= 925033804);

    static long PartyIdOfLocal()
    {
        if (!GameSession.InGame) return 0;
        var state = GameSession.Net.Social(new SocialRequest { characterId = GameSession.CharacterId });
        return state.party ? state.partyId : 0;
    }

    static long ServerClock() => System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}
