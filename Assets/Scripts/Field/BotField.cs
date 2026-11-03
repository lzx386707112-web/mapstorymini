using System.Collections;
using System.Collections.Generic;
using Server.Bot;
using UnityEngine;

// 地图假人纸娃娃：复用原IMG轴心、部件z层和SpriteAnim原帧delay。
// 位置按封包节奏插值（源端客户端对远程角色是平滑播放 movement 包，不是瞬移），
// 纸娃娃合成按帧分摊（进图瞬间几十个假人一次性合成会卡主线程），
// 说话用原版 UI/ChatBalloon.img 的九宫格画在头顶。
public sealed class BotField : MonoBehaviour
{
    // 同图内新增（假人从别的图走进来）每帧最多建几个；仓促抬高会在正常游戏时掉帧。
    const int MaxCreatePerFrame = 1;
    // 换图那一批最多花多少毫秒建视图。换图有黑幕遮挡，可以比常规 4ms 预算宽，
    // 但必须按「毫秒」封顶而不是按「人头数」：一个人头 0.1~0.3ms（预合成就绪时）到
    // 3~10ms（缓存没命中、现场合成）之间浮动，人头上限在快机上太松、在慢机上太紧。
    // 秒数才是帧率的直接约束，人头数不是。
    const float MapEnterBudgetMs = 6f;
    // 进图补建窗口：这段时间内继续按上面的毫秒预算续建，窗口过后退回每帧一个。
    // 没有这个窗口，超过预算的假人就是「每帧冒一个」——39 只要 0.65 秒才站齐，
    // 玩家看到的是排队出场，和联机「进图时周围的人已经在场」完全不像。
    const float CatchUpSeconds = 1f;
    // 视图增删（进图/假人换图）与姿势同步不需要每帧重算：100ms 一次足够
    // （假人位移包本身是 160ms 一步），省掉每帧一次 BotRegistry.OnMap 的列表分配与全量遍历。
    const float SyncSeconds = 0.1f;
    // 原端聊天气泡显示 5000ms（0x48A935 的 push 0x1388），最后 500ms 淡出。
    const float BalloonSeconds = 5f;
    const float BalloonFadeSeconds = 0.5f;
    // MovementCommands.walkAlongX：duration=160ms、step=10px（＝62.5px/s）——那是「录制动作」包的节奏，
    // 假人移动走的是 BotMovementService 的 50ms tick（每 tick 约 8px ≈ 160px/s）。
    // 旧代码把 160ms 当成通用插值窗口：视图只能跑到 50px/s，永远追不上服务端，
    // 稳态滞后几百像素，而且只能用「当前位置 → 最新目标」的直线去追，于是拐角、台阶、绳梯
    // 全被抹平成空中直线（2026-11-02 用户报「假人在空中乱走、还没到绳子就开始爬」）。
    // 现在位置/速度由 Update → TrackPosition 每帧按服务端真实坐标推进，不再有固定窗口。
    // 服务端一次性换坐标（进传送门/落地纠偏）的距离阈值：超过就直接落位，
    // 不能拿限速去滑行——那就是用户看到的「空中直线飞行」。
    const float TeleportPx = 200f;

    sealed class View
    {
        public GameObject root;
        public SpriteAnim animation;
        public string appearance;
        public NativeBotChair chair;
        public BotActor actor;          // 每帧读服务端坐标用（见 Update）；不靠 100ms 的姿态同步
        public float previousY;
        public bool groundedPose;
        public string groundPose;
        public Vector3 target;
        public float speed = 0f;        // 世界单位/秒；由封包位移/时长算出来，别给默认值
        public float measured;          // 实测移动速度（px/s），用来对齐 walk 动画速率
    }

    sealed class Balloon
    {
        public string text;
        public float until;
        // 排版（断行 + 尺寸）只算一次：每帧对每个字 CalcSize 太浪费
        public string wrapped;
        public float innerW, innerH;
        public int measuredRing=-1;
        public string[] ringLines;
        public float ringWidth;
    }

    readonly Dictionary<int, View> views = new Dictionary<int, View>();
    readonly Dictionary<int, Balloon> balloons = new Dictionary<int, Balloon>();
    readonly HashSet<int> present = new HashSet<int>();
    readonly List<int> removed = new List<int>();
    readonly Queue<int> pending = new Queue<int>();
    Server.Bot.Social.BotOutput output;
    int currentMap, currentWorld, currentChannel;
    float lastSync = -10f;
    // 进图补建窗口的截止时间（unscaledTime）；-1 表示不在窗口内。
    float catchUpUntil = -1f;
    bool synchronized;
    NativeMarketField market;

    void OnEnable()
    {
        output = BotTypeManager.Output;
        output.Published += OnOutput;
        AudioManager.Preload("Game/EnchantSuccess", "Game/EnchantFailure");
    }
    void OnOutput(Server.Bot.Social.BotOutput.Message message)
    {
        var actor = BotRegistry.Get(message.actorId);
        if (!synchronized || actor == null || message.mapId != currentMap ||
            actor.world != currentWorld || actor.channel != currentChannel) return;
        if (message.kind == Server.Bot.Social.BotOutput.Kind.Chat && !string.IsNullOrEmpty(message.text))
        {
            BottomBar.AddChat(actor.name + " : " + message.text, BottomBar.ChatWhite);
            ShowBalloon(message.actorId, actor.name + ": " + message.text);
        }
        // SocialCommands.BotChatbubble 只冒泡不进聊天记录。
        if (message.kind == Server.Bot.Social.BotOutput.Kind.Bubble && !string.IsNullOrEmpty(message.text))
            ShowBalloon(message.actorId, actor.name + ": " + message.text);
        if (message.kind == Server.Bot.Social.BotOutput.Kind.Emote) Express(message.actorId, message.value);
        if (message.kind == Server.Bot.Social.BotOutput.Kind.Scroll && views.TryGetValue(message.actorId,out var scrolling))
        {
            // SHOW_SCROLL_EFFECT: success is its first bool; CURSE uses the same Failure IMG.
            string result = message.value == 1 ? "Success" : "Failure";
            NativeImgAnimation.Spawn("Effect/BasicEff.img/Enchant/" + result,
                scrolling.root.transform, scrolling.root.transform.position, 360);
            AudioManager.Play("Game/Enchant" + result, pitchVary:false);
        }
        if (message.kind == Server.Bot.Social.BotOutput.Kind.Attack && views.TryGetValue(message.actorId,out var view))
            view.animation.PlayNativeAttack(message.text,message.value);
    }

    public void Synchronize(int mapId, int world, int channel)
    {
        using var perf = Server.Bot.BotPerf.Timer("BotField.Sync");
        if (market == null) market = GetComponent<NativeMarketField>() ?? gameObject.AddComponent<NativeMarketField>();
        market.Synchronize(mapId,world,channel);
        bool sameMap = synchronized && mapId == currentMap;
        bool sameContext = sameMap && world == currentWorld && channel == currentChannel;
        if (sameContext && Time.unscaledTime - lastSync < SyncSeconds) return;
        // 「还没同步过」也算换图：首次进游戏、或者 Clear() 之后重新进图，
        // 都该把假人一次性摆好（Clear 会把 synchronized 置 false）。
        bool mapChanged = !sameMap;
        lastSync = Time.unscaledTime;
        currentMap = mapId; currentWorld = world; currentChannel = channel; synchronized = true;
        present.Clear();
        foreach (var actor in BotRegistry.OnMap(mapId))
        {
            if (!actor.online || actor.world != world || actor.channel != channel) continue;
            present.Add(actor.id);
            if (!views.ContainsKey(actor.id))
            {
                if (!pending.Contains(actor.id)) pending.Enqueue(actor.id);
                continue;
            }
            Apply(actor, views[actor.id]);
        }
        removed.Clear();
        foreach (var entry in views) if (!present.Contains(entry.Key)) removed.Add(entry.Key);
        foreach (int id in removed) { RemoveView(views[id]); views.Remove(id); balloons.Remove(id); }
        // 换图：假人一次性站好（对齐联机 —— 进图那一刻周边玩家就已经在场上，不是一个个冒出来）。
        // 前提是纸娃娃已经在启动期间预合成完（BotAppearancePrebake.Finished）：
        // 那时建视图只是查表 + new GameObject，一个人 0.1~0.3ms。
        // 预算按毫秒给（MapEnterBudgetMs），不按人头给：快机上 39 个也只要 6ms，
        // 慢机上自动少建几个人，剩下的在 CatchUpSeconds 窗口内续建，不会把一帧撑爆。
        // 预合成没赶完就退回增量排队——宁可晚出现，也不能在进图那一帧连做十几次 CPU 合成。
        if (mapChanged)
        {
            catchUpUntil = Time.unscaledTime + CatchUpSeconds;
            if (BotAppearancePrebake.Finished) CreateQueued(int.MaxValue, MapEnterBudgetMs);
            else CreateQueued(MaxCreatePerFrame);
            if (pending.Count > 0)
                Debug.Log("[Bot] 进图建视图：本图 " + present.Count + " 个，"
                          + MapEnterBudgetMs + "ms 预算内没建完，剩 " + pending.Count + " 个分帧续建");
        }
        else CreateQueued(MaxCreatePerFrame);
    }

    // 每帧按预算补建假人视图：单个纸娃娃要混合几十张 IMG 部件，一件就可能吃掉几毫秒，
    // 所以本帧预算用完就先让位给渲染，剩下的下一帧继续。
    // limit = 本批最多建几个；budgetMs > 0 表示这一批最多花多少毫秒（进图批次用），
    // budgetMs <= 0 表示只管人头数（常规增量，仍受 BotWorkPump 的共享 4ms 预算约束）。
    void CreateQueued(int limit, float budgetMs = -1f)
    {
        using var perf = Server.Bot.BotPerf.Timer("BotField.CreateQueued");
        int created = 0;
        long begin = budgetMs > 0f ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
        while (created < limit && pending.Count > 0)
        {
            if (budgetMs > 0f)
            {
                // 至少建一个：一个都建不出来就是「假人永远不出现」，宁可多花一帧。
                if (created > 0 && (System.Diagnostics.Stopwatch.GetTimestamp() - begin) *
                    1000.0 / System.Diagnostics.Stopwatch.Frequency >= budgetMs) break;
            }
            else if (created > 0 && !Server.Bot.BotWorkPump.Has(1f)) break;
            int id = pending.Dequeue();
            if (!present.Contains(id) || views.ContainsKey(id)) continue;
            var actor = BotRegistry.Get(id);
            if (actor == null) continue;
            Create(actor);
            created++;
        }
    }

    void Create(BotActor actor)
    {
        var bot = actor as BotCharacter;
        if (bot == null) return;
        using var perf = Server.Bot.BotPerf.Timer("BotField.Create");
        var root = new GameObject("SoloMapling_" + actor.id);
        root.transform.SetParent(transform, false);
        var renderer = root.AddComponent<SpriteRenderer>();
        renderer.sortingOrder = 300;
        var view = new View { root = root, animation = root.AddComponent<SpriteAnim>(), previousY = actor.y };
        view.chair = root.AddComponent<NativeBotChair>();
        var ids = BotAppearance.CharacterIds(bot);
        string key = "bot_" + string.Join("_", ids);
        WzCharacter.Appearance(key, ids);
        view.animation.Init("chars", key, "stand", renderer);
        // 假人换帧要现合成纸娃娃：由帧预算决定本帧是否合成，别让几十个假人同时换帧拖垮帧率。
        view.animation.throttleWhenBusy = true;
        view.animation.Play("stand", false, true);
        view.appearance = key;
        // 首次直接落位，之后交给插值。
        view.target = new Vector3(actor.x / Art.PPU, -actor.y / Art.PPU, 0f);
        root.transform.position = view.target;
        views.Add(actor.id, view);
        Apply(actor, view);
    }

    void Apply(BotActor actor, View view)
    {
        // 位置/速度交给 Update 每帧做（服务端 50ms 一步，100ms 同步一次会把拐角抹平）。
        view.actor = actor;
        var bot = actor as BotCharacter;
        if (bot == null) return;
        var ids = BotAppearance.CharacterIds(bot);
        string appearance = "bot_" + string.Join("_", ids);
        if (bot.mountActive && bot.mountItemId > 0 && bot.chair <= 0)
        {
            WzCharacter.Appearance(appearance, ids);
            appearance = WzCharacter.MountedAppearance(appearance, bot.mountItemId);
        }
        if (view.appearance != appearance)
        {
            if (!bot.mountActive || bot.chair > 0) WzCharacter.Appearance(appearance, ids);
            view.appearance = appearance;
            view.animation.key = appearance;
            view.animation.Play(view.animation.Current, view.animation.IsOneShot, true);
        }
        string pose = bot.chair > 0 ? "sit" : Pose(bot.stance);
        view.groundedPose = bot.chair <= 0 && (pose == "walk" || pose == "stand");
        view.groundPose = pose;
        view.animation.SetBase(pose);
        if (bot.chair > 0 && view.animation.Current != "sit") view.animation.Play("sit", false, true);
        view.animation.SetFacing((bot.stance & 1) == 0 ? 1 : -1);
        view.chair.Set(bot.chair, view.animation);
        view.chair.Refresh();
        // walk 的播放速率按「实际走多快」算（见 Update）：walkAlongX 的 62.5px/s 和录制动作的
        // 真实速度不一样，写死一个系数总有一边滑步。站/跳/爬仍按原 delay 播。
        if (pose == "rope" || pose == "ladder")
            view.animation.speed = !bot.moving && bot.y == view.previousY ? 0f : 1f;
        else if (pose != "walk")
            view.animation.speed = 1f;
        view.previousY = bot.y;
    }

    void Update()
    {
        using var framePerf = Server.Bot.BotPerf.Timer("BotField.Update");
        // 进图补建窗口内继续用进图预算（否则超过预算的假人会「每帧冒一个」）；窗口过后退回增量。
        if (Time.unscaledTime < catchUpUntil) CreateQueued(int.MaxValue, MapEnterBudgetMs);
        else CreateQueued(MaxCreatePerFrame);
        float step = Time.deltaTime;
        foreach (var view in views.Values)
        {
            TrackPosition(view, step);
            var before = view.root.transform.position;
            var after = Vector3.MoveTowards(before, view.target, view.speed * step);
            if (view.groundedPose)
            {
                bool supported = false;
                var map = Server.Field.MapService.Get(currentMap);
                if (map != null)
                {
                    var ground = Server.Bot.Environment.MapGeometryCache.Get(map).ground;
                    int px = Mathf.RoundToInt(after.x * Art.PPU), py = Mathf.RoundToInt(-after.y * Art.PPU);
                    var footing = ground.Ground(px, py);
                    if (footing != null)
                    {
                        int fy = Server.Bot.Environment.BotGroundIndex.YAt(footing, px);
                        if (Mathf.Abs(fy - py) <= 26)
                        { after.y = -(fy - 1) / Art.PPU; supported = true; }
                    }
                }
                // A landing packet can arrive while the displayed feet are still in flight.
                view.animation.SetBase(supported ? view.groundPose : "jump");
            }
            view.root.transform.position = after;
            if (step <= 0f) continue;
            // 实测速度（px/s）→ walk 动画速率：主角 125px/s 时按 1.0 播，
            // 假人 62.5px/s 就按 0.5 播——否则动画和位移对不上，看着像滑步。
            float instant = Vector3.Distance(before, after) * Art.PPU / step;
            view.measured = Mathf.Lerp(view.measured, instant, 1f - Mathf.Exp(-10f * step));
            if (view.animation.Current == "walk")
                view.animation.speed = Mathf.Clamp(view.measured / PlayerCtrl.BaseWalkPx, 0.25f, 2f);
        }
        if (balloons.Count == 0) return;
        float now = Time.unscaledTime;
        removed.Clear();
        foreach (var entry in balloons) if (now >= entry.Value.until) removed.Add(entry.Key);
        foreach (int id in removed) balloons.Remove(id);
    }

    // 每帧直接读服务端坐标。假人位移由 BotMovementService 的 50ms tick 推进（约 8px/步），
    // 若只在 100ms 的姿态同步里换目标、并按 160ms 折算速度，视图就只能以 1/3 速度
    // 沿着「当前位置→最新目标」的直线追赶 —— 拐角、台阶、平台边缘全被抹平，
    // 人还在半空中就开始摆爬绳/跳跃姿势。这里按「两帧追平」给速度：
    // 既不会瞬移，也不会落后到能看出抄近路。
    void TrackPosition(View view, float step)
    {
        var actor = view.actor;
        if (actor == null || view.root == null) return;
        var next = new Vector3(actor.x / Art.PPU, -actor.y / Art.PPU, 0f);
        float pixels = Vector3.Distance(view.target, next) * Art.PPU;
        if (pixels < 0.01f) return;
        view.target = next;
        if (pixels >= TeleportPx)
        {
            // 传送门/换图是一次性换坐标：直接落位。用 800px/s 滑过去就是「空中直线飞行」。
            view.root.transform.position = next; view.speed = 0f; return;
        }
        float window = Mathf.Max(step * 2f, 0.02f);
        // 上限要盖住跳跃弧线（起跳/下落每 50ms 约 28px ≈ 560px/s），否则跳起来那一秒
        // 视图永远落后服务端，落点才对上，看着就是「在空中直线飘」。
        // 更远的位移已经由上面的 TeleportPx 直接落位，不会再走这条限速。
        view.speed = Mathf.Clamp(pixels / Art.PPU / window, 2f / Art.PPU, 900f / Art.PPU);
    }

    // CharacterStance.java；其余姿势不能凭编号猜测。
    static string Pose(int stance)
    {
        switch (stance & ~1)
        {
            case 2: return "walk";
            case 6: return "jump";
            case 14: return "ladder";
            case 16: return "rope";
            default: return "stand";
        }
    }

    // ——————————————— 假人技能表现 ———————————————
    // 源端 grindHit 的链路是 showSkillVisual(skillId) + 对应攻击包，技能视觉全部挂在
    // Skill/<职业>.img/skill/<id>/level/<等级>/ 下（effect/ball/hit/action 都有可能只在
    // 等级节点里，根节点常常一个都没有）。旧实现只拼根节点 /effect：根上没有 effect 的
    // 技能既没有动作、也没有释放和命中特效——这就是「有些技能缺动作缺特效」的根因。
    // 现在复用主角那条已经按等级核对过的 NativeSkillVfx 解析，动作走原 IMG 的 action 节点。
    const float ProjectileSpeed=(1000f/1.5f)/Art.PPU;   // 95445F..954511：1.5ms/px
    int hitVariant;

    public void PlaySkillCast(BotCharacter actor,int skillId,int level,Component target=null)
    {
        if (!views.TryGetValue(actor.id,out var view) || view.root==null) return;
        int rank=Mathf.Max(1,level);
        string key=NativeSkillVfx.Key(skillId,rank);
        // action 可能只在 level/N/action（如 1311001 的 burster1/burster2），由 WzCharacter.Pose 解析；
        // 原端没有 action 的技能沿用武器普攻姿势，和主角 PlaySkill 的回退口径一致。
        string action="skill_"+skillId+"@"+rank;
        if (Art.GetClip("chars",view.animation.key,action)==null) action="atk";
        CastBranches(view,key,action,actor.facing,target);
    }

    // prepare/effect 在施法者身上（z<0 是施法者后层），ball 飞向目标，hit 落在目标身上。
    // 伤害由服务端即时结算，这里只负责把同一套原 IMG 表现补齐。
    void CastBranches(View view,string key,string action,int facing,Component target)
    {
        var foot=view.root!=null ? view.root.transform : null;
        if (foot==null) return;
        float preparation=0f;
        foreach (var clip in NativeSkillVfx.Parts(key,"prepare"))
        {
            SpawnPart(key,clip,foot.position,facing,foot);
            preparation=Mathf.Max(preparation,clip.Duration);
        }
        // 有 prepare 的技能先播前导帧、再出招放特效；和主角 CastVisuals 的次序一致，
        // 否则前导动画会和挥砍动作/特效叠在同一帧上。
        if (preparation>0f) StartCoroutine(CastAfterPrepare(view,key,action,facing,target,preparation));
        else CastNow(view,key,action,facing,target);
    }

    IEnumerator CastAfterPrepare(View view,string key,string action,int facing,Component target,float delay)
    {
        yield return new WaitForSeconds(delay);
        if (view==null || view.root==null) yield break;
        CastNow(view,key,action,facing,target);
    }

    void CastNow(View view,string key,string action,int facing,Component target)
    {
        var foot=view.root!=null ? view.root.transform : null;
        if (foot==null) return;
        view.animation.PlayOnce(action);
        foreach (string branch in new[]{"effect","effect0"})
            foreach (var clip in NativeSkillVfx.Parts(key,branch))
            {
                bool behind=NativeSkillVfx.Number(key,clip.path,"z")<0;
                SpawnPart(key,clip,foot.position,facing,foot,behind ? 299 : 360);
            }
        foreach (var clip in NativeSkillVfx.Parts(key,"affected"))
            SpawnPart(key,clip,foot.position,facing,foot);
        if (NativeSkillVfx.Has(key,"special")) SpecialBurst(key,foot.position,facing);
        if (target==null) return;
        if (NativeSkillVfx.Has(key,"ball")) StartCoroutine(FlyBall(foot,key,target,facing));
        else PlaySkillHit(key,target,facing);
    }

    // 原端 special（2111002 爆炸柱）：start 之后按 interval 连续 count 段，x 方向按宽度铺开，
    // y>0 时从该高度落到地面。主角 CastVisuals 走同一套参数，假人缺这段就只剩一团闪光。
    void SpecialBurst(string key,Vector3 origin,int facing)
    {
        var parts=NativeSkillVfx.Parts(key,"special");
        if (parts.Count==0) return;
        int count=Mathf.Max(1,(int)NativeSkillVfx.Number(key,"special","count",parts.Count));
        float start=NativeSkillVfx.Number(key,"special","start")/1000f;
        float interval=NativeSkillVfx.Number(key,"special","interval",60)/1000f;
        float width=NativeSkillVfx.Number(key,"special","x",200)/Art.PPU;
        float height=NativeSkillVfx.Number(key,"special","y")/Art.PPU;
        float fall=height>0f ? NativeSkillVfx.Number(key,"special","duration",500)/1000f : 0f;
        StartCoroutine(BurstParts(key,parts,origin,facing,count,start,interval,width,height,fall));
    }

    IEnumerator BurstParts(string key,List<NativeVfxClip> parts,Vector3 origin,int facing,
        int count,float start,float interval,float width,float height,float fall)
    {
        if (start>0f) yield return new WaitForSeconds(start);
        for (int i=0;i<count;i++)
        {
            float x=origin.x+((i+.5f)/count*2f-1f)*width;
            var point=new Vector3(x,origin.y+height,0);
            var clip=parts[i%parts.Count];
            var fx=SkillFx.PlayPart(key,clip.path,point,facing,1,360);
            if (fx!=null && fall>0f) StartCoroutine(FallToGround(fx,point,new Vector3(x,PlayerCtrl.GroundAt(x,origin.y),0),fall));
            yield return new WaitForSeconds(Mathf.Max(.001f,interval));
        }
    }

    IEnumerator FallToGround(SkillFx fx,Vector3 from,Vector3 to,float duration)
    {
        for (float elapsed=0f;fx!=null && elapsed<duration;)
        {
            elapsed+=Time.deltaTime;
            fx.transform.position=Vector3.Lerp(from,to,Mathf.Clamp01(elapsed/duration));
            yield return null;
        }
        if (fx!=null) fx.Stop();
    }

    IEnumerator FlyBall(Transform foot,string key,Component target,int facing)
    {
        if (foot==null || target==null) yield break;
        Vector3 from=foot.position+Vector3.up*(28/Art.PPU);
        float duration=Vector3.Distance(from,HitAnchor(target,0))/ProjectileSpeed;
        var ball=SkillFx.PlayPart(key,"ball",from,facing,1,360,true);
        for (float elapsed=0f;elapsed<duration;)
        {
            if (foot==null || target==null) break;
            elapsed+=Time.deltaTime;
            if (ball!=null)
                ball.transform.position=Vector3.Lerp(from,HitAnchor(target,0),Mathf.Clamp01(elapsed/Mathf.Max(.001f,duration)));
            yield return null;
        }
        if (ball!=null) ball.Stop();
        if (target!=null) PlaySkillHit(key,target,facing);
    }

    static SkillFx SpawnPart(string key,NativeVfxClip clip,Vector3 point,int facing,Transform follow,int order=360)
    {
        var fx=SkillFx.PlayPart(key,clip.path,point,facing,1,order);
        if (fx!=null && follow!=null) fx.Follow(follow);
        return fx;
    }

    public void PlaySkillHit(int skillId,int level,Component target,int facing)
        => PlaySkillHit(NativeSkillVfx.Key(skillId,Mathf.Max(1,level)),target,facing);

    void PlaySkillHit(string key,Component target,int facing)
    {
        if (target==null) return;
        var hits=NativeSkillVfx.Parts(key,"hit");
        if (hits.Count==0) return;
        var clip=hits[hitVariant++%hits.Count];
        var fx=SkillFx.PlayPart(key,clip.path,HitAnchor(target,NativeSkillVfx.Number(key,clip.path,"pos")),facing,1,360);
        if (fx!=null) fx.Follow(target.transform);
    }

    // 和主角 SkillSystem.TargetAnchor 同一口径：取最高的身体 Sprite 中心；pos=2 是头顶状态节点。
    static Vector3 HitAnchor(Component target,float pos)
    {
        var renderers=target.GetComponentsInChildren<SpriteRenderer>();
        SpriteRenderer body=null;
        foreach (var renderer in renderers)
            if (renderer.enabled && renderer.sprite!=null && (body==null || renderer.bounds.size.y>body.bounds.size.y)) body=renderer;
        if (body==null) return target.transform.position+Vector3.up*.6f;
        return new Vector3(body.bounds.center.x,pos==2 ? body.bounds.max.y : body.bounds.center.y,0);
    }

    public void Express(int actorId, int expression)
    {
        if (!views.TryGetValue(actorId, out var view)) return;
        if (expression == 0) view.animation.ClearExpression();
        else view.animation.Express(expression);
    }

    public Vector2? PickupTarget(int actorId)
    {
        if (!views.TryGetValue(actorId, out var view)) return null;
        var sprite = view.animation.Renderer;
        float height = sprite != null && sprite.sprite != null ? sprite.bounds.size.y : 0;
        return (Vector2)view.root.transform.position + Vector2.up * (height * 0.5f);
    }

    // 脚下名牌用：root 就是脚点（和气泡的作者头顶口径一致）。
    public bool TryFeet(int actorId, out Vector3 feet)
    {
        feet = default;
        if (!views.TryGetValue(actorId, out var view) || view.root == null) return false;
        feet = view.root.transform.position;
        return true;
    }

    public void Clear()
    {
        if (market != null) market.Clear();
        foreach (var view in views.Values) RemoveView(view);
        views.Clear(); present.Clear(); removed.Clear(); pending.Clear(); balloons.Clear();
        synchronized = false;
        lastSync = -10f;
    }
    static void RemoveView(View view)
    {
        if (Application.isPlaying) Destroy(view.root);
        else DestroyImmediate(view.root);
    }
    void OnDisable()
    {
        if (output != null) output.Published -= OnOutput;
        output = null;
        Clear();
    }

    // ——————————————— 头顶聊天气泡 ———————————————

    // General chat includes the local speaker; it has no BotField.View.
    public void ShowLocalBalloon(string text) => ShowBalloon(GameSession.CharacterId, text);

    void ShowBalloon(int actorId, string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        if (balloons.Count > 64) balloons.Clear();   // 兜底：假人异常刷屏时不把 GUI 拖死
        balloons[actorId] = new Balloon { text = text, until = Time.unscaledTime + BalloonSeconds };
        // debug_balloon=1（一次有效）：气泡锚点画红点 + logcat 打说话人坐标。
        // 气泡以说话人的头顶为锚（UI 层，画在所有东西上面）；如果红点处看不到人，
        // 那人就在那儿、只是被前景物件（排序序号 > 300 的 obj）挡住了——源端同样如此。
        if (!balloonDebugTried)
        {
            balloonDebugTried = true;
            balloonDebug = PlayerPrefs.GetInt("debug_balloon", 0) != 0;
            if (balloonDebug) PlayerPrefs.DeleteKey("debug_balloon");
        }
        if (balloonDebug)
        {
            var speaker = BotRegistry.Get(actorId);
            int ring = actorId == GameSession.CharacterId ? NativeNameTag.RingStyle("chatBalloon") : 0;
            bool art = ring == 0 || NativeChatBalloon.Prepare(ring, 16, 1) != null;
            Debug.Log("[Bot/Balloon] speaker=" + (speaker?.name ?? "?") + "(#" + actorId + ")" +
                      " ring=" + ring + (ring == 0 ? "" : art ? " 有素材" : " 素材缺失→回退默认") +
                      " map=" + (speaker?.mapId ?? 0) + " x=" + (speaker?.x ?? 0) + " y=" + (speaker?.y ?? 0) +
                      " text=" + text);
        }
    }
    static bool balloonDebugTried, balloonDebug;

    static GUIStyle balloonStyle;
    static Texture2D balloonNw, balloonNe, balloonSw, balloonSe, balloonN, balloonS, balloonW, balloonE, balloonC, balloonArrow;
    static bool balloonArtTried;

    static void EnsureBalloonArt()
    {
        if (balloonArtTried) return;
        balloonArtTried = true;
        const string root = "UI/ChatBalloon.img/0/";
        balloonNw = TryTexture(root + "nw"); balloonNe = TryTexture(root + "ne");
        balloonSw = TryTexture(root + "sw"); balloonSe = TryTexture(root + "se");
        balloonN = TryTexture(root + "n"); balloonS = TryTexture(root + "s");
        balloonW = TryTexture(root + "w"); balloonE = TryTexture(root + "e");
        balloonC = TryTexture(root + "c"); balloonArrow = TryTexture(root + "arrow");
    }

    static Texture2D TryTexture(string path)
    {
        try
        {
            var node = WzAssets.Node(path);
            return node == null ? null : WzAssets.Texture(node);
        }
        catch (System.Exception) { return null; }
    }

    // 原端 UI 的绘制基准是 768 高的虚拟屏（NameTag / ChatBalloon 都是这套），
    // 所以气泡整体按 UiKit.H/768 缩放；九宫格在「原图像素」空间里平铺，
    // 缩放交给 GUI.matrix，6/12/14 的边框才不会被非整数缩放糊掉或挤出缝。
    const float ArtBaseHeight = 768f;

    void OnGUI()
    {
        // All hit tests below use Pointer on Repaint; balloons have no GUILayout/control state.
        if (Event.current.type != EventType.Repaint) return;
        using var guiPerf = Server.Bot.BotPerf.Timer("BotField.Repaint");
        if (ShopWindow.Visible) return;
        if (!synchronized && balloons.Count == 0) return;
        if (Event.current.type == EventType.Repaint && Pointer.DoubleClicked && !Pointer.Blocked && !GameWindows.AnyVisible)
        {
            var camera = Camera.main;
            if (camera != null)
            {
                var point = camera.ScreenToWorldPoint(new Vector3(Pointer.Pos.x * UiKit.Scale,
                    Screen.height - Pointer.Pos.y * UiKit.Scale,0));
                bool visited = market != null && market.TryVisit(point,
                    id => views.TryGetValue(id,out var view) ? view.animation.Renderer : null);
                if (!visited)
                {
                    foreach (var entry in views)
                    {
                        var renderer = entry.Value.animation.Renderer;
                        if (renderer != null && renderer.sprite != null &&
                            renderer.bounds.Contains(new Vector3(point.x, point.y, renderer.bounds.center.z)))
                        { NativeCharacterInfo.Open(BotRegistry.Get(entry.Key) as BotCharacter); break; }
                    }
                    if (!NativeCharacterInfo.Visible)
                    {
                        var renderer = GameLoop.I?.player?.GetComponentInChildren<SpriteAnim>()?.Renderer;
                        if (renderer != null && renderer.sprite != null &&
                            renderer.bounds.Contains(new Vector3(point.x, point.y, renderer.bounds.center.z))) NativeCharacterInfo.Open();
                    }
                }
            }
        }

        if (Event.current.type == EventType.Repaint && Pointer.RightPressed && !Pointer.Blocked && !NativeTradeWindow.Visible && !NativeUtilPrompt.Visible)
        {
            var camera = Camera.main;
            if (camera != null)
            {
                var world = camera.ScreenToWorldPoint(new Vector3(Pointer.Pos.x * UiKit.Scale, Screen.height - Pointer.Pos.y * UiKit.Scale, 0));
                foreach (var entry in views)
                {
                    var renderer = entry.Value.animation.Renderer;
                    if (renderer != null && renderer.sprite != null && renderer.bounds.Contains(new Vector3(world.x,world.y,renderer.bounds.center.z)))
                    { NativeCharacterMenu.Open(BotRegistry.Get(entry.Key)); break; }
                }
            }
        }
        if (balloons.Count == 0) return;
        var cam = Camera.main;
        if (cam == null) return;
        EnsureBalloonArt();
        if (balloonC == null) return;
        UiKit.Begin();
        if (balloonStyle == null)
            balloonStyle = new GUIStyle(GUI.skin.label)
            {
                font = UiKit.Font,
                fontSize = 12,                       // 原气泡正文 12px；行高由字体决定（ui.ttf 是 17）
                alignment = TextAnchor.MiddleCenter,
                wordWrap = false,
                clipping = TextClipping.Overflow,
            };
        float s = Mathf.Max(0.35f, UiKit.H / ArtBaseHeight);
        float now = Time.unscaledTime;
        foreach (var entry in balloons)
        {
            Transform root;
            SpriteRenderer renderer;
            if (entry.Key == GameSession.CharacterId)
            {
                var player = GameLoop.I?.player;
                if (!GameSession.InGame || player == null) continue;
                root = player.transform;
                renderer = player.GetComponentInChildren<SpriteAnim>()?.Renderer;
            }
            else
            {
                if (!views.TryGetValue(entry.Key, out var view)) continue;
                root = view.root.transform; renderer = view.animation.Renderer;
            }
            var balloon = entry.Value;
            float alpha = Mathf.Clamp01((balloon.until - now) / BalloonFadeSeconds);
            // 锚点 = 角色当前站立帧的头顶（不是脚点上固定 84px：矮角色会飘一截、高角色会压脸）
            float headY = renderer != null && renderer.sprite != null ? renderer.bounds.max.y : root.position.y + 0.55f;
            var sp = cam.WorldToScreenPoint(new Vector3(root.position.x, headY, 0f));
            if (sp.z < 0f) continue;
            var anchor = new Vector2(sp.x / UiKit.Scale / s, (Screen.height - sp.y) / UiKit.Scale / s);
            // 正文排版：原端气泡超出宽度就断行，长消息是往下长成多行，
            // 不是横向一直拉长（横向长度没有上限的话，短角色配超长气泡就完全脱节）。
            if (balloon.wrapped == null) Measure(balloon, balloonStyle);
            float innerW = balloon.innerW;
            float innerH = balloon.innerH;
            int ringStyle = entry.Key == GameSession.CharacterId ? NativeNameTag.RingStyle("chatBalloon") : 0;
            if(ringStyle!=0 && balloon.measuredRing!=ringStyle)MeasureRing(balloon,balloonStyle,ringStyle);
            var decorated = ringStyle == 0 ? null : NativeChatBalloon.Prepare(ringStyle,(int)balloon.ringWidth,balloon.ringLines.Length);
            if (decorated != null)
            {
                var decoratedMatrix = GUI.matrix;
                var decoratedColor = GUI.color;
                GUI.matrix = decoratedMatrix * Matrix4x4.Scale(new Vector3(s,s,1f));
                GUI.color = new Color(1,1,1,alpha);
                var frame = decorated.Frame(now);
                var bodyRect = new Rect(anchor.x-decorated.anchorX,anchor.y-frame.height,
                    frame.width,frame.height);
                GUI.DrawTexture(bodyRect,frame);
                balloonStyle.normal.textColor = decorated.color;
                var textRect = decorated.text; textRect.position += bodyRect.position;
                int previousSize=balloonStyle.fontSize;
                balloonStyle.fontSize=NativeChatBalloon.FontSize(ringStyle);
                textRect.height=decorated.lineHeight;
                foreach(string line in balloon.ringLines)
                {
                    GUI.Label(textRect,line,balloonStyle);
                    textRect.y+=decorated.lineHeight;
                }
                balloonStyle.fontSize=previousSize;
                GUI.color = decoratedColor; GUI.matrix = decoratedMatrix;
                continue;
            }
            float w = innerW + 12f, h = innerH + 12f;
            // 尾巴几何（UI/ChatBalloon.img/0/arrow，13x13，origin=(1,0)）：
            //   0 行 x0 透明、x1 起才有像素；图像左下角是尾尖，尾尖落在角色头顶；
            //   前 5 行是下边框带的逐像素复制（ee/ccddee/bbccdd/bbbbdd/黑），
            //   所以它必须整体「压」在主体的 6px 下边框上，不能挂在下边框下面——
            //   挂下面就是一块白板 + 一条边框线横切，气泡看上去被切成两半。
            //   这几行里没有圆角处的黑色 —— 尾巴是压在直边段上的，不是压住左下角。
            const float Border = 6f;                    // 九宫格下边框厚度（sw/s/se 的高度）
            // 水平：气泡整体以角色头顶（人物）为**中心**，尾巴也在气泡底边上**居中**，
            // 于是尾尖正好落在头顶（源端就是这么摆的，不是左对齐）。
            var rect = new Rect(anchor.x - w * 0.5f, anchor.y - (balloonArrow != null ? balloonArrow.height - Border : 7f) - h, w, h);
            var tailX = rect.x + (w - (balloonArrow != null ? balloonArrow.width : 13f)) * 0.5f;
            float viewW = UiKit.W / s, viewH = UiKit.H / s;
            if (rect.xMax < -40f || rect.x > viewW + 40f || rect.yMax < -40f || rect.y > viewH + 40f) continue;

            var previousMatrix = GUI.matrix;
            var previousColor = GUI.color;
            GUI.matrix = previousMatrix * Matrix4x4.Scale(new Vector3(s, s, 1f));
            GUI.color = new Color(1f, 1f, 1f, alpha);
            // 主体整张画（不是九块分开画）：分开画时 GUI 把每个四边形对齐到整数像素，
            // 缩放比又不是整数，相邻块之间会留下 1px 空隙——就是那条「透明的线」。
            var body = BalloonBody((int)innerW, (int)innerH);
            if (body != null) GUI.DrawTexture(rect, body);
            // 尾巴最后画（在主体之上）：尾巴顶部那 5 行本来就是下边框带的复制品，
            // 压上去才能把边框「抹掉」接成一体；画在下面就会露出一条边框线和白板。
            if (balloonArrow != null)
                GUI.DrawTexture(new Rect(tailX, rect.yMax - Border, balloonArrow.width, balloonArrow.height), balloonArrow);
            balloonStyle.normal.textColor = new Color(0f, 0f, 0f, alpha);   // clr = -16777216，黑字
            GUI.Label(new Rect(rect.x + 6f, rect.y + 6f, innerW, innerH), balloon.wrapped, balloonStyle);
            if (balloonDebug)
            {
                // 红点 = 气泡锚点 = 说话人头顶。红点落在空处 = 人在那儿、被前景物件挡住。
                var keep = GUI.color;
                GUI.color = new Color(1f, 0.2f, 0.2f, 1f);
                GUI.DrawTexture(new Rect(anchor.x - 3f, anchor.y - 3f, 6f, 6f), Texture2D.whiteTexture);
                GUI.color = keep;
            }
            GUI.color = previousColor;
            GUI.matrix = previousMatrix;
        }
    }

    // 一行最多 11 个字符（源端实测：`ieskk: 11222` 正好一行、长串一行 11 个），
    // **空格不占位**；宽度自适应（气泡宽度 = 最宽那行），四周内边距固定 6px。
    const int MaxCharsPerLine = 11;

    // 折行（源端行为）：一行**填满 11 个字符**再硬断，不在空格处退让——
    // 退让会让长消息的第一行只剩「昵称: 」，气泡里冒出半行空白，和源端截图不符。
    // 空格不占位（`ieskk: 11222` 是 11 个字符、正好一行）；显式 '\n' 也断行。
    // 同时算出正文区尺寸：宽 = 最宽那行的实际像素宽（向上取整到整像素，不做任何步进取整），
    // 高按行数 × 字体行距（**不能用 CalcSize 的高**，它含行距，单行会被撑成两层高、气泡中间多一圈空白）。
    static void Measure(Balloon balloon, GUIStyle style)
    {
        string text = balloon.text ?? string.Empty;
        var builder = new System.Text.StringBuilder(text.Length + 16);
        float widest = 0f;
        int lines = 0;
        int start = 0;
        while (start < text.Length)
        {
            int end = start, count = 0;
            while (end < text.Length && text[end] != '\n')
            {
                char c = text[end];
                if (c != ' ' && count + 1 > MaxCharsPerLine) break;   // 填满 11 个字，硬断
                if (c != ' ') count++;
                end++;
            }
            if (end == start && text[start] != '\n') end = start + 1;   // 兜底，别死循环
            string lineText = text.Substring(start, end - start).Trim();
            if (lineText.Length > 0)
            {
                if (lines > 0) builder.Append('\n');
                builder.Append(lineText);
                lines++;
                widest = Mathf.Max(widest, style.CalcSize(new GUIContent(lineText)).x);
            }
            start = end < text.Length && text[end] == '\n' ? end + 1 : end;
        }
        if (lines == 0) lines = 1;
        balloon.wrapped = builder.ToString();
        // 宽度自适应：正文区就是最宽那行的实际像素宽（**不要按 12 取整**，那是给分块平铺留余量用的；
        // 现在主体是整张合成图，切图最后一块会被裁切，任意宽度都不会出缝）。
        // 一句话的长度直接决定气泡宽度：短句就窄，长句才宽。只向上取整到整像素，
        // 避免合成图比文字少 1px 把正文压到右边框上。
        balloon.innerW = Mathf.Max(12f, Mathf.Ceil(widest));
        // 行高必须取字体自己的行盒（`style.lineHeight`），不能写死 14：
        // ui.ttf 在 fontSize=12 时 ascent=13 / descent=4 ⇒ 行盒 **17px**，而 CJK 墨迹只有 11px，
        // 上下各留 3px。正文区高 = 行数 × 行盒高，GUI.Label 的 MiddleCenter 居中的就是这个行盒，
        // 于是墨迹上下间距天然相等（3/3）。**把行高写死成 14 会让行盒比正文区高 3px、文字整体
        // 下沉、下沿被下边框裁掉——表现就是「上面内边距大、下面没有」（2026-10-27 用户在对照图上指出）。
        float lineHeight = Mathf.Max(14f, style.lineHeight);
        balloon.innerH = Mathf.Max(14f, lines * lineHeight);
    }

    static void MeasureRing(Balloon balloon,GUIStyle style,int ring)
    {
        int previousSize=style.fontSize;
        style.fontSize=NativeChatBalloon.FontSize(ring);
        float limit=NativeChatBalloon.TextWidth(ring),widest=0;
        string remaining=balloon.text ?? "";
        var lines=new List<string>();
        while(remaining.Length>0)
        {
            int end=0;
            while(end<remaining.Length && remaining[end]!='\n' &&
                style.CalcSize(new GUIContent(remaining.Substring(0,end+1))).x<=limit)end++;
            if(end==0 && remaining[0]!='\n')end=1;
            string line=remaining.Substring(0,end).TrimEnd(' ','\t','\r','\n');
            lines.Add(line);widest=Mathf.Max(widest,style.CalcSize(new GUIContent(line)).x);
            int next=end;
            while(next<remaining.Length && " \t\r\n".IndexOf(remaining[next])>=0)next++;
            remaining=remaining.Substring(next);
        }
        if(lines.Count==0)lines.Add("");
        balloon.ringLines=lines.ToArray();balloon.ringWidth=Mathf.Ceil(widest);balloon.measuredRing=ring;
        style.fontSize=previousSize;
    }

    // 九宫格先合成成一张整图（按尺寸缓存），再一次性画：内部不可能出现缝，
    // 缩放成任何比例都只有外边缘受影响。
    static readonly Dictionary<int, Texture2D> balloonBodies = new Dictionary<int, Texture2D>();

    static Texture2D BalloonBody(int innerW, int innerH)
    {
        int key = (innerW << 16) | innerH;
        if (balloonBodies.TryGetValue(key, out var cached) && cached != null) return cached;
        if (balloonBodies.Count > 48)
        {
            foreach (var texture in balloonBodies.Values) if (texture != null) Destroy(texture);
            balloonBodies.Clear();
        }
        int w = innerW + 12, h = innerH + 12;
        var pixels = new Color32[w * h];                 // 默认全透明
        Blit(balloonNw, pixels, w, h, 0, 0, 6, 6);
        Blit(balloonNe, pixels, w, h, w - 6, 0, 6, 6);
        Blit(balloonSw, pixels, w, h, 0, h - 6, 6, 6);
        Blit(balloonSe, pixels, w, h, w - 6, h - 6, 6, 6);
        Blit(balloonN, pixels, w, h, 6, 0, innerW, 6);
        Blit(balloonS, pixels, w, h, 6, h - 6, innerW, 6);
        Blit(balloonW, pixels, w, h, 0, 6, 6, innerH);
        Blit(balloonE, pixels, w, h, w - 6, 6, 6, innerH);
        Blit(balloonC, pixels, w, h, 6, 6, innerW, innerH);
        var body = new Texture2D(w, h, TextureFormat.RGBA32, false)
        {
            name = "ChatBalloon_" + w + "x" + h,
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
        };
        body.SetPixels32(pixels); body.Apply();
        balloonBodies[key] = body;
        return body;
    }

    // 把切图按原样平铺进整图；top 是自上而下的行号，越界自动裁掉。
    static void Blit(Texture2D piece, Color32[] dst, int dw, int dh, int x, int top, int w, int h)
    {
        if (piece == null || w <= 0 || h <= 0) return;
        var src = piece.GetPixels32();
        int sw = piece.width, sh = piece.height;
        for (int ty = 0; ty < h; ty++)
        {
            int y = top + ty;
            if (y < 0 || y >= dh) continue;
            int row = (dh - 1 - y) * dw;                 // GetPixels32 自下而上
            int sy = sh - 1 - (ty % sh);                 // 源图同样自下而上
            for (int tx = 0; tx < w; tx++)
            {
                int col = x + tx;
                if (col < 0 || col >= dw) continue;
                dst[row + col] = src[sy * sw + (tx % sw)];
            }
        }
    }
}
