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
    public int ContinuousSkillId => heldSkill?.native.Id ?? 0;
    public bool TryCastBoundSkill(int id,GameAction action)
    {
        boundHeldAction=action;
        try { return TryCastSkill(id); }
        finally { boundHeldAction=null; }
    }
    void StartHeld(SkillDef definition,GameAction action)
    {
        heldSkill=definition;heldAction=action;
        p.PlaySkill(definition.action);
        heldPreparation=StartCoroutine(HeldVisuals(definition));
        AudioManager.Play("Skill/"+definition.native.Id+"/Use",.9f);
    }
    IEnumerator HeldVisuals(SkillDef definition)
    {
        foreach(var clip in NativeSkillVfx.Parts(definition.vfx,"prepare"))
            heldVisuals.Add(Visual(definition.vfx,clip.path,p.FeetPos,p.facing,p.transform));
        float preparation=Preparation(definition);
        if(preparation>0)yield return new WaitForSeconds(preparation);
        if(heldSkill!=definition)yield break;
        foreach(string branch in new[]{"keydown","keydown0"})
            foreach(var clip in NativeSkillVfx.Parts(definition.vfx,branch))
                heldVisuals.Add(Visual(definition.vfx,clip.path,p.FeetPos,p.facing,p.transform,true,
                    NativeSkillVfx.Number(definition.vfx,clip.path,"z")<0 ? p.SortingOrder-1 : 360));
        heldPreparation=null;
    }
    void StopHeld(bool release)
    {
        var previous=heldSkill;heldSkill=null;
        if(heldPreparation!=null){StopCoroutine(heldPreparation);heldPreparation=null;}
        foreach(var fx in heldVisuals)if(fx!=null)fx.Stop();
        heldVisuals.Clear();
        if(previous==null)return;
        p?.FinishHeldSkill();actionUntil=0;
        if(release && p!=null)
            foreach(var clip in NativeSkillVfx.Parts(previous.vfx,"keydownend"))
                Visual(previous.vfx,clip.path,p.FeetPos,p.facing,p.transform);
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
        long now=(long)(Time.realtimeSinceStartupAsDouble*1000);
        if(!NativeHeldSkill.ShotDue(now,lastHeldShot))return;
        lastHeldShot=now;
        if(!BeginCast(heldSkill,true)){Report(LastFailure);StopHeld(true);}
    }
}
