namespace Server.Bot
{
    // 源端Character在假人与真人交互中共用的运行时身份/位置视图。
    // 真人存档仍由现有FullCharacter/CharService管理，本对象不另建存档。
    public class BotActor
    {
        public int id;
        public string name = "";
        public int level = 1, job, gender, face, hair, skinColor, fame;
        public int mapId, world, channel = 1;
        public float x, y;
        internal long mapEntryOrder;
        static long nextMapEntryOrder;
        internal void MarkMapEntry() => mapEntryOrder = ++nextMapEntryOrder;
        public bool online = true;
        public FullCharacter character;
        public Trade.IBotTradeSession trade;
        public int mesosTraded;
        public virtual bool IsBot() => false;
    }
}
