using System;
using System.Collections.Generic;

namespace Server.Field
{
    // ReactorActionManager.generateDropList/assembleReactorDropEntries/dropItems.
    // The original JS supplies meso options; SQL supplies every item/probability.
    public static class NativeReactorDrops
    {
        public sealed class Entry { public int item, chance, quest; }
        sealed class Spray
        {
            public MapReactor reactor;
            public int owner, x, minMeso, maxMeso, index;
            public double mesoRate;
            public long at;
            public List<Entry> items;
        }
        public static readonly Dictionary<int,List<Entry>> Tables = new Dictionary<int,List<Entry>>();
        public static Random Random = new Random();
        public static Func<int,(double drop,double meso)> Rates;
        public static Func<int,int,int,bool> NeedsQuestItem;
        static readonly List<Spray> sprays = new List<Spray>();

        public static void Load(Dictionary<string,object> root)
        {
            Tables.Clear();
            foreach (var pair in root)
            {
                var rows = new List<Entry>();
                foreach (Dictionary<string,object> row in (List<object>)pair.Value)
                    rows.Add(new Entry { item=Convert.ToInt32(row["item"]), chance=Convert.ToInt32(row["chance"]), quest=Convert.ToInt32(row["quest"]) });
                Tables.Add(int.Parse(pair.Key), rows);
            }
        }

        public static void Drop(MapReactor reactor,int owner,bool meso,int mesoChance,int minMeso,int maxMeso,int minItems)
        {
            if (!Tables.TryGetValue(reactor.id,out var table))
                throw new InvalidOperationException("Reactor drop table is not migrated: " + reactor.id);
            var rates = Rates?.Invoke(owner) ?? (drop:1.0,meso:1.0);
            var items = new List<Entry>();
            if (meso && Random.NextDouble() < 1.0 / mesoChance) items.Add(new Entry { quest=-1 });
            foreach (var row in table)
                if (Random.NextDouble() < rates.drop / row.chance) items.Add(row);
            while (items.Count < minItems) items.Add(new Entry { quest=-1 });

            var normal = new List<Entry>(); var visible = new List<Entry>(); var other = new List<Entry>();
            foreach (var row in items)
                if (row.quest <= 0) normal.Add(row);
                else if (NeedsQuestItem?.Invoke(owner,row.quest,row.item) ?? true) visible.Add(row);
                else other.Add(row);
            Shuffle(normal); Shuffle(visible); Shuffle(other);
            items.Clear(); items.AddRange(normal); items.AddRange(visible); items.AddRange(other);
            var left = new List<Entry>(); var right = new List<Entry>();
            for (int i=0;i<items.Count;i++) if (i%2==0) left.Add(items[i]); else right.Add(items[i]);
            left.Reverse(); left.AddRange(right);
            sprays.Add(new Spray { reactor=reactor,owner=owner,items=left,
                x=reactor.x-(items.Count%2==0 ? 12 : 0)-12*items.Count,
                minMeso=minMeso,maxMeso=maxMeso,mesoRate=rates.meso,at=MapReactorService.Now() });
        }

        static void Shuffle(List<Entry> rows)
        {
            for (int i=rows.Count-1;i>0;i--) { int j=Random.Next(i+1);var row=rows[i];rows[i]=rows[j];rows[j]=row; }
        }

        public static void Tick(long now)
        {
            foreach (var spray in new List<Spray>(sprays))
            {
                // register(action,200) has zero initial delay, then a 200ms period.
                while (spray.index < spray.items.Count && now >= spray.at)
                {
                    var r=spray.reactor;var row=spray.items[spray.index++];
                    int x=spray.x; spray.x+=25; spray.at+=200;
                    if (row.item==0)
                    {
                        int amount=(int)Math.Min(int.MaxValue,(Random.NextDouble()*(spray.maxMeso-spray.minMeso)+spray.minMeso)*spray.mesoRate);
                        MapItemService.CalcDropPos(r.mapId,x,r.y,r.x,r.y,out float dx,out float dy);
                        MapItemService.SpawnMesoDrop(r.mapId,amount,dx,dy,r.objectId,spray.owner,
                            MapItemService.PartyOf?.Invoke(spray.owner) ?? -1,false,2);
                    }
                    else
                        MapItemService.DropFromReactor(r.mapId,r.objectId,spray.owner,
                            Server.Bot.BotLogic.GenerateCleanItem(row.item),x,r.y,r.x,r.y,row.quest);
                }
                if (spray.index==spray.items.Count) sprays.Remove(spray);
            }
        }
        public static void Clear(int? mapId=null)
        { if (mapId.HasValue) sprays.RemoveAll(s=>s.reactor.mapId==mapId.Value); else sprays.Clear(); }
    }
}
