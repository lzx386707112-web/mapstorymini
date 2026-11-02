using System;
using System.Collections.Generic;
using MapleDash.Js;

// HuntTaskAutoPublisher.java. Task state is channel-server lifetime, not NPC lifetime.
public sealed class NativeHuntTasks
{
    public sealed class Task
    {
        public int type, itemId, bossId, needCount;
        public string name="", completedBy="";
        public bool completed;
        public long start, end, completedAt;
    }
    public sealed class Reward
    {
        public int itemId, count;
        public double chance;
        public string name;
    }
    readonly Func<string> config;
    readonly List<Task> items=new List<Task>();
    readonly List<Reward> rewards=new List<Reward>();
    readonly Random random=new Random();
    public Func<long> Clock=()=>DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    public Func<int,int> Choose;
    public Func<double> Roll;
    public Action<string> Notice;
    public Func<int,string> ItemName, MobName;
    public int Gold {get;private set;}=100000;
    public int Exp {get;private set;}=50000;
    public int Cash {get;private set;}=100;
    public int Interval {get;private set;}=3600;
    public IReadOnlyList<Reward> Rewards=>rewards;
    Task current=new Task();
    bool running;
    long next;
    public NativeHuntTasks(Func<string> config) { this.config=config;Reload(); }
    static int Int(Dictionary<string,object> row,string key,int fallback=0)
        =>row!=null && row.TryGetValue(key,out var value) && value!=null?Convert.ToInt32(value):fallback;
    static string Text(Dictionary<string,object> row,string key)
        =>row.TryGetValue(key,out var value)?value as string:null;
    public void Reload()
    {
        running=false;items.Clear();rewards.Clear();Interval=3600;
        string json=config();
        if(!string.IsNullOrEmpty(json))
        {
            var root=MiniJsonDb.Parse(json) as Dictionary<string,object> ?? throw new JsRuntimeException("Invalid hunt task configuration");
            if(root.TryGetValue("HUNT_CONFIG",out var timing))Interval=Int(timing as Dictionary<string,object>,"TASK_INTERVAL",3600);
            if(Interval<=0)throw new JsRuntimeException("Invalid hunt task interval");
            if(root.TryGetValue("HUNT_ITEMS",out var tasks) && tasks is List<object> taskList)
                foreach(var value in taskList)
                {
                    var row=(Dictionary<string,object>)value;
                    items.Add(new Task {type=Int(row,"type"),itemId=Int(row,"itemId"),bossId=Int(row,"bossId"),
                        needCount=Int(row,"needCount"),name=Text(row,"name")});
                }
            if(root.TryGetValue("REWARD_INFO",out var info) && info is Dictionary<string,object> rewardInfo)
            {
                Gold=Int(rewardInfo,"GOLD_REWARD",100000);Exp=Int(rewardInfo,"EXP_REWARD",50000);Cash=Int(rewardInfo,"CASH_REWARD",100);
                if(rewardInfo.TryGetValue("REWARD_ITEMS",out var rewardItems) && rewardItems is List<object> rewardList)
                    foreach(var value in rewardList)
                    {
                        var row=(Dictionary<string,object>)value;
                        rewards.Add(new Reward {itemId=Int(row,"itemId"),count=Int(row,"count",1),name=Text(row,"name"),
                            chance=row.TryGetValue("chance",out var probability)?Convert.ToDouble(probability):0});
                    }
            }
        }
        Start();
    }
    public void Start()
    {
        if(items.Count==0)return;
        Publish();running=true;next=Clock()+Interval*1000L;
    }
    public void Stop()=>running=false;
    public void Tick()
    {
        if(!running || Clock()<next)return;
        Publish();next+=Interval*1000L;
    }
    void Publish()
    {
        var item=items[Choose?.Invoke(items.Count) ?? random.Next(items.Count)];
        string name=item.name;
        if(string.IsNullOrEmpty(name))name=item.type==2?MobName?.Invoke(item.bossId) ?? "BOSS":ItemName?.Invoke(item.itemId) ?? "未知道具";
        long now=Clock();
        current=new Task {type=item.type,itemId=item.itemId,bossId=item.bossId,needCount=item.needCount,name=name,start=now,end=now+Interval*1000L};
        Notice?.Invoke("[狩猎任务] 新任务发布啦！\r\n"+(item.type==2?"狩猎BOSS："+name+"，\r\n第一个击杀该BOSS的玩家获得奖励！\r\n":
            "需要道具："+name+" x"+item.needCount+"，\r\n全服只有一个玩家能够完成任务，先到先得！\r\n")+"限时："+(Interval/3600)+"小时");
    }
    public Task Current()
    {
        if(!current.completed && Clock()>current.end)current=new Task();
        return current;
    }
    public int CanComplete(int count)
    {
        if(current.type==2 || current.completed || current.itemId==0)return 1;
        if(Clock()>current.end)return 2;
        return count<current.needCount?3:0;
    }
    public void MarkCompleted(string name)
    {
        current.completed=true;current.completedBy=name;current.completedAt=Clock();
        CompletionNotice(name,null);
    }
    // Returns false for every non-target, expired, or already completed kill.
    // The caller performs the source player methods synchronously, then reports rewards.
    public bool MonsterKilled(int id,string player,Action award)
    {
        if(current.type!=2 || current.completed || Clock()>current.end || current.bossId!=id)return false;
        current.completed=true;current.completedBy=player;current.completedAt=Clock();award();return true;
    }
    public bool RewardWon(Reward reward)=>(Roll?.Invoke() ?? random.NextDouble())<reward.chance;
    public void CompletionNotice(string player,IList<string> success)
    {
        Notice?.Invoke("[狩猎任务] 恭喜玩家 "+player+" 完成了狩猎任务！\r\n"+
            (current.type==2?"狩猎BOSS："+current.name+"\r\n":"完成道具：#v"+current.itemId+"# #z"+current.itemId+"# × "+current.needCount+"\r\n")+
            "获得奖励："+(success!=null && success.Count>0?string.Join("、",success):"金币10万、经验5万、点券100"));
    }
    static void Bind(JsHostObject host,string method,Func<JsCall,JsValue> fn)=>host.Set(method,JsFunction.FromNative(method,fn));
    static JsHostObject Strict(object value)
    {
        var host=new JsHostObject(value);host.Missing=key=>throw new JsRuntimeException("Unmigrated HuntTaskAutoPublisher API: "+key);return host;
    }
    JsValue TaskHost(Task task)
    {
        var host=Strict(task);
        Bind(host,"getType",c=>new JsNumber(task.type));Bind(host,"getItemId",c=>new JsNumber(task.itemId));
        Bind(host,"getBossId",c=>new JsNumber(task.bossId));Bind(host,"getNeedCount",c=>new JsNumber(task.needCount));
        Bind(host,"getItemName",c=>new JsString(task.name));Bind(host,"isBossTask",c=>JsBoolean.Of(task.type==2));
        Bind(host,"isCompleted",c=>JsBoolean.Of(task.completed));Bind(host,"getCompletedByName",c=>new JsString(task.completedBy));
        Bind(host,"getCompletedTime",c=>new JsNumber(task.completedAt));Bind(host,"getStartTime",c=>new JsNumber(task.start));Bind(host,"getEndTime",c=>new JsNumber(task.end));
        return host;
    }
    public JsValue Type()
    {
        var type=Strict(this);var host=Strict(this);Bind(type,"getInstance",c=>host);
        Bind(host,"getCurrentTask",c=>TaskHost(Current()));Bind(host,"getTaskInterval",c=>new JsNumber(Interval));
        Bind(host,"getHuntItemsSize",c=>new JsNumber(items.Count));Bind(host,"getGoldReward",c=>new JsNumber(Gold));
        Bind(host,"getExpReward",c=>new JsNumber(Exp));Bind(host,"getCashReward",c=>new JsNumber(Cash));
        Bind(host,"getRewardItems",c=>
        {
            var array=new JsArray();foreach(var reward in rewards)
            {
                var entry=Strict(reward);Bind(entry,"getItemId",_=>new JsNumber(reward.itemId));Bind(entry,"getCount",_=>new JsNumber(reward.count));
                Bind(entry,"getChance",_=>new JsNumber(reward.chance));Bind(entry,"getName",_=>reward.name==null?(JsValue)JsNull.Value:new JsString(reward.name));array.Items.Add(entry);
            }
            return array;
        });
        Bind(host,"canCompleteTask",c=>
        {
            var player=c.Arg(0);var count=c.Interp.Call(c.Interp.GetMember(player,"getItemQuantity"),player,new JsValue[]{new JsNumber(current.itemId),JsBoolean.False});
            return new JsNumber(CanComplete(JsConvert.ToInt32(count)));
        });
        Bind(host,"markTaskCompleted",c=>{MarkCompleted(JsConvert.ToString(c.Arg(0)));return JsUndefined.Value;});
        Bind(host,"reload",c=>{Reload();return JsUndefined.Value;});Bind(host,"start",c=>{Start();return JsUndefined.Value;});Bind(host,"stop",c=>{Stop();return JsUndefined.Value;});
        return type;
    }
}
