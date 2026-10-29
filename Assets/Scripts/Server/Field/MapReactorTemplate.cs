using System;
using System.Collections.Generic;

namespace Server.Field
{
    // ReactorFactory.getReactor: retain every event, and the first type-100 area.
    public sealed class MapReactorTemplate
    {
        public sealed class Event
        {
            public int type, next, item, quantity;
        }
        public readonly Dictionary<int,List<Event>> states=new Dictionary<int,List<Event>>();
        public int left,top,right,bottom;
        public List<Event> Events(int state) => states.TryGetValue(state,out var events) ? events : null;
        public int Next(int state,int index)
        { var events=Events(state);return events!=null && index<events.Count ? events[index].next : -1; }
        public static Dictionary<int,MapReactorTemplate> Parse(Dictionary<string,object> root)
        {
            var result=new Dictionary<int,MapReactorTemplate>();
            foreach(var pair in root)
            {
                var data=(Dictionary<string,object>)pair.Value;
                var template=new MapReactorTemplate();
                if(data["area"] is List<object> area)
                { template.left=Convert.ToInt32(area[0]);template.top=Convert.ToInt32(area[1]);
                  template.right=Convert.ToInt32(area[2]);template.bottom=Convert.ToInt32(area[3]); }
                foreach(var state in (Dictionary<string,object>)data["states"])
                {
                    var events=new List<Event>();
                    foreach(Dictionary<string,object> row in (List<object>)state.Value)
                        events.Add(new Event { type=Convert.ToInt32(row["type"]),next=Convert.ToInt32(row["next"]),
                            item=Convert.ToInt32(row["item"]),quantity=Convert.ToInt32(row["quantity"]) });
                    template.states.Add(int.Parse(state.Key),events);
                }
                result.Add(int.Parse(pair.Key),template);
            }
            return result;
        }
    }
}
