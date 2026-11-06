using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public partial class SkillSystem
{
    GameAction? boundHeldAction;
    GameAction heldAction;
    SkillDef heldSkill;
    long lastHeldShot;
    Coroutine heldPreparation;
    readonly List<SkillFx> heldVisuals=new List<SkillFx>();
    // 蓄力型：按住只播表现，松手再走一次完整施法（扣 MP、结算）。
    bool heldCharging;
    // 连射的「第一发」由 HeldVisuals 在起手动作播完之后打出，之后 Update 按 attackDelay 续。
    // 没有这个闸门的话 Update 会在 prepare 还没播完时就开火，伤害比特效先出来。
    bool heldReady;
    // 这次按住期间有没有真的打出过伤害发。点一下就松手时用来补保底一发。
    bool heldFired;
    public int ContinuousSkillId => heldSkill?.native.Id ?? 0;
    public bool TryCastBoundSkill(int id,GameAction action)
    {
        boundHeldAction=action;
        try { return TryCastSkill(id); }
        finally { boundHeldAction=null; }
    }
    void StartHeld(SkillDef definition,GameAction action,bool charging=false)
    {
        heldSkill=definition;heldAction=action;heldCharging=charging;heldReady=false;heldFired=false;
        p.PlaySkill(definition.action);
        heldPreparation=StartCoroutine(HeldVisuals(definition));
        AudioManager.Play("Skill/"+definition.native.Id+"/Use",.9f);
    }
    IEnumerator HeldVisuals(SkillDef definition)
    {
        foreach(string branch in NativeSkillVfx.Layers(definition.vfx,"prepare"))
            foreach(var clip in NativeSkillVfx.Parts(definition.vfx,branch))
                heldVisuals.Add(Visual(definition.vfx,clip.path,p.FeetPos,p.facing,p.transform));
        float preparation=Preparation(definition);
        if(preparation>0)yield return new WaitForSeconds(preparation);
        if(heldSkill!=definition)yield break;
        // keydown / keydown0 同一族的蓄力循环层
        foreach(string branch in NativeSkillVfx.Layers(definition.vfx,"keydown"))
            foreach(var clip in NativeSkillVfx.Parts(definition.vfx,branch))
                heldVisuals.Add(Visual(definition.vfx,clip.path,p.FeetPos,p.facing,p.transform,true,
                    NativeSkillVfx.Number(definition.vfx,clip.path,"z")<0 ? p.SortingOrder-1 : 360));
        heldPreparation=null;
        // 起手动作播完 = 光束已经出来，从这里开始 Update 才会逐发结算。
        heldReady=true;
    }
    void StopHeld(bool release)
    {
        var previous=heldSkill;heldSkill=null;
        bool charging=heldCharging;heldCharging=false;heldReady=false;
        if(heldPreparation!=null){StopCoroutine(heldPreparation);heldPreparation=null;}
        foreach(var fx in heldVisuals)if(fx!=null)fx.Stop();
        heldVisuals.Clear();
        if(previous==null)return;
        p?.FinishHeldSkill();ClearActionLock();
        if(release && p!=null)
        {
            foreach(string branch in NativeSkillVfx.Layers(previous.vfx,"keydownend"))
                foreach(var clip in NativeSkillVfx.Parts(previous.vfx,branch))
                    Visual(previous.vfx,clip.path,p.FeetPos,p.facing,p.transform);
            // 蓄力松手才真正施放一次：这条调用不带 boundHeldAction，
            // 所以不会再次落进 held 分支（不会自我递归）。
            if(charging && previous.native!=null)TryCastSkill(previous.native.Id);
            // 连射的保底一发：起手动作还没播完（0.9s 的 prepare）就松手时，
            // Update 一次都没轮到，不补这一发就是「特效放了、怪一点没掉血」。
            else if(!charging && !heldFired && previous.native!=null)BeginCast(previous,true);
        }
    }
    void Update()
    {
        if(heldSkill==null)return;
        var game=GameLoop.I;
        if(p==null || p.hp<=0 || game==null || game.gameOver || game.state!=GameLoop.GameState.Playing)
        {StopHeld(false);return;}
        int slot=QuickSlots.Index(heldAction);
        if(!InputHub.Held(heldAction) || slot<0 || QuickSlots.Id(slot)!=heldSkill.native.Id ||
            PlayerCtrl.InputLocked || PlayerCtrl.ScriptUiLock>0 || NpcSession.Blocking ||
            KeyConfigWindow.Visible || VirtualControlsEditor.Visible)
        {StopHeld(true);return;}
        if(Time.deltaTime<=0)return;
        // 蓄力型不逐发发弹，按住就是攒着，松手在 StopHeld 里结算一次。
        if(heldCharging)return;
        // 起手动作还没播完（prepare 层还在放）之前不开火，第一发由 HeldVisuals 补。
        if(!heldReady)return;
        long now=(long)(Time.realtimeSinceStartupAsDouble*1000);
        if(!NativeHeldSkill.ShotDue(now,lastHeldShot,heldSkill.native!=null ? heldSkill.native.AttackDelay : 0))return;
        lastHeldShot=now;
        if(!BeginCast(heldSkill,true)){Report(LastFailure);StopHeld(true);}
        else heldFired=true;
    }
}
