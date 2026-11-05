using System;
using System.Collections.Generic;
using MapleDash.Wz;
using UnityEngine;

// Wz2 original widget positions and images; hosted by the enhancement window.
public static class NativeSoulWeaponPanel
{
    const string Root="UI/UIWindowWz2.img/SoulWeaponWz2/";
    static float scale;
    static string modal;
    static Rect modalRect;
    static InventoryEntry selected;
    static int owner;
    static string amountText="1";
    static int previousGrade;
    static SoulWeaponWz2State State=>GameSession.Character?.soulWeapon ?? new SoulWeaponWz2State();
    public static bool AtPointer=>modal!=null && modalRect.Contains(Pointer.Pos);
    public static bool HasModal=>modal!=null;
    public static void CloseModal(){modal=null;if(NativeEditKeyboard.Owns("SoulWeaponLevel"))NativeEditKeyboard.Close();}
    public static void Reset()
    {
        CloseModal();selected=null;owner=0;
        if(NativeSoulWeapon.IsSkill(SkillWindow.DragSkillId))SkillWindow.CancelSkillDrag();
    }
    public static void Refresh()
    {
        if(!GameSession.InGame)return;
        var response=GameSession.Net.Soul(new SoulRequest {characterId=GameSession.CharacterId,operation=SoulOperation.ReadModern});
        if(response.ok)GameSession.Character.soulWeapon=response.modern;
        else BottomBar.Toast(response.error);
        owner=GameSession.CharacterId;
    }
    static ImgNode Node(string path)=>WzAssets.Node(Root+path);
    static Rect At(Rect r,float x,float y,float w,float h)=>new Rect(r.x+x*scale,r.y+y*scale,w*scale,h*scale);
    static void Image(Rect r,string path,float x=0,float y=0)
    {
        var n=Node(path);var t=WzAssets.Texture(n);if(t==null)return;var o=WzAssets.Origin(n);
        GUI.DrawTexture(At(r,x-o.x,y-o.y,t.width,t.height),t);
    }
    static bool Button(Rect r,string path,float x=0,float y=0,bool enabled=true)
    {
        var n=Node(path);var offset=n?.Get("slotOffset");
        if(offset!=null){x+=offset.X;y+=offset.Y;}
        var normal=Node(path+"/normal/0");var t=WzAssets.Texture(normal);if(t==null)return false;
        var o=WzAssets.Origin(normal);var rect=At(r,x-o.x,y-o.y,t.width,t.height);
        bool can=enabled && GUI.enabled,hover=rect.Contains(Pointer.Pos);
        string state=!can?"disabled":hover?Pointer.Down?"pressed":"mouseOver":"normal";
        GUI.DrawTexture(rect,WzAssets.Texture(Node(path+"/"+state+"/0"))??t);
        if(Event.current.type!=EventType.Repaint || !Pointer.Clicked || !hover || !GUI.enabled)return false;
        Pointer.ConsumeClick();if(!can)return false;
        AudioManager.Play("UI/BtMouseClick",.7f,false);return true;
    }
    static void Label(Rect r,string text,string path,float x=0,float y=0)
    {
        var border=Node(path);if(border==null)return;
        var lt=border.Get("lt");var rb=border.Get("rb");if(lt==null||rb==null)return;
        ImgNode block=null;foreach(var c in border.Children)if(c.Text("class")=="TextBlock"){block=c;break;}
        var style=new GUIStyle {font=UiKit.Font,fontSize=Math.Max(1,Mathf.RoundToInt((block?.Int("font/size",12)??12)*scale)),
            wordWrap=true,alignment=TextAnchor.MiddleLeft};
        string align=border.Text("HAlign");if(align=="Center")style.alignment=TextAnchor.MiddleCenter;else if(align=="Right")style.alignment=TextAnchor.MiddleRight;
        style.normal.textColor=Color.white;
        GUI.Label(At(r,x+lt.X,y+lt.Y,rb.X-lt.X,rb.Y-lt.Y),text,style);
    }
    static bool Send(SoulOperation operation,int amount=0)
    {
        var response=GameSession.Net.Soul(new SoulRequest {characterId=GameSession.CharacterId,inventoryItemId=selected?.itemRowId??0,
            operation=operation,amount=amount,revision=State.revision,commit=true});
        if(response.modern!=null)GameSession.Character.soulWeapon=response.modern;
        if(!response.ok){BottomBar.Toast(response.error);return false;}
        if(response.inventory!=null){GameSession.Character.inventory=response.inventory;GameSession.ApplyInventoryToProfile();}
        NativeGrowthStats.Invalidate();GameSession.MarkStatsDirty();
        GameLoop.I?.player?.RefreshBuffAttributes();return true;
    }
    public static bool Draw(Rect r,float s,InventoryEntry target)
    {
        scale=s;if(owner!=GameSession.CharacterId)Refresh();selected=target;
        var state=State;
        var active=GameSession.Character.inventory.Find(i=>i.itemRowId==state.weapon);
        bool bound=state.unlocked && active!=null;
        // 已启用时拖入另一件武器：整块面板预览它自己的成长；否则显示当前启用武器的成长。
        bool preview=bound && target!=null && target.itemRowId!=active.itemRowId;
        var item=preview?target:bound?active:target;
        bool live=bound && !preview;                                   // 显示的就是当前启用武器
        var growth=item==null?null:state.Of(item.itemRowId);           // 每件武器各自独立，别借用别的武器
        bool grown=item!=null && state.Has(item.itemRowId);
        int grade=Mathf.Clamp(growth?.grade??1,1,10),level=growth?.level??1,exp=growth?.exp??0,option=growth?.option??0;
        Image(r,"Main/backgrnd");
        bool gui=GUI.enabled;GUI.enabled&=modal==null;
        if(Button(r,"Main/$btExit")){GUI.enabled=gui;return true;}
        if(Button(r,"Main/$btHelp"))NativeUtilPrompt.Pages("灵魂武器",new[]{
            "200级后可启用灵魂武器。武器需求等级须达到150级。将背包或装备栏的武器放入上方武器格，点击武器确认启用；点击已启用的武器可以停用；已有启用武器时放入另一件武器并点击，可确认切换。角色同一时间只能启用一件。启用期间不能转移武器，停用后按武器原有交易规则处理。",
            "灵魂等级、阶级、经验和烙印跟着武器实例走：换一把武器就是它自己的进度，灵魂碎片和Boss单人击破记录是全角色共用的。",
            "单人击败列表内Boss可获得灵魂碎片。每周同一Boss只补发较高难度与已获得碎片的差额；周四00:00（UTC+8）重置。碎片用于成长和重设灵魂烙印。",
            "灵魂等级上限100级、阶级上限10阶。当前阶级经验蓄满且完成对应Boss单人击破后可升阶。共鸣效果和烙印只有装备启用的武器才生效。",
            "灵魂的斗志持续40秒、冷却120秒。点击技能图标拿起并放到快捷栏，双击图标施放。停用、换下或失去武器时属性立即失效。旧灵魂转换仅在原端转换活动时间内、首次启用时处理。"});
        if(Button(r,"Main/$btReward"))ShowBosses();
        string weapon="Main/WeaponArea/$WeaponAreaSwitcher/"+(live?"Enable":"Disable");
        Image(r,weapon+"/back/image",12,36);
        if(live){Image(r,weapon+"/slot/image",12,36);Animate(r,weapon+"/ani/Timeline",12,36,60);}
        var icon=item==null?null:NativeItemSource.Icon(item.itemId);
        if(icon!=null)GUI.DrawTexture(At(r,201,91,64,64),icon);
        var slot=At(r,182,73,102,100);
        if(gui&&modal==null&&Event.current.type==EventType.Repaint && slot.Contains(Pointer.Pos))
        {
            if(item!=null)ItemTooltip.SetHover(item.itemId,false,item);
            if(Pointer.Clicked)
            {
                Pointer.ConsumeClick();
                if(item==null)BottomBar.Toast("请从背包或装备栏放入武器");
                else if(!bound)NativeUtilPrompt.Confirm(
                    "确定将这件武器启用为灵魂武器？\n启用期间无法转移；装备该武器后，共鸣和烙印才会生效。\n主动技能可放入快捷栏使用，也可在面板中双击施放。",
                    ok=>{if(ok)Send(SoulOperation.ActivateModern);});
                else if(live)NativeUtilPrompt.Confirm(
                    "确定停用这件灵魂武器？\n成长和烙印会保留，共鸣和主动技能效果会停止。",
                    ok=>{if(ok)Send(SoulOperation.DeactivateModern);});
                else NativeUtilPrompt.Confirm(
                    "确定切换灵魂武器？\n将停用当前武器并启用这件武器；等级、阶级、经验和烙印各自独立，切换后显示的是这件武器自己的进度。\n切换后需装备新武器，共鸣和烙印才会生效。",
                    ok=>{if(Send(SoulOperation.DeactivateModern))Send(SoulOperation.ActivateModern);});
            }
        }
        string gradePath="Main/$GradeAreaSwitcher/"+(live?"Enable/$GradeAreaEnableSwitcher/":"Disable/$GradeAreaDisableSwitcher/")+(grade-1);
        Image(r,gradePath,7,210);
        string skill="Main/ActiveSkillArea/$ActiveSkillAreaSwitcher/"+(live?"Enable":"Disable");
        Image(r,skill+"/back/image",375,236);
        if(live)Animate(r,skill+"/ani/Timeline",402,267,60);
        int sid=SoulWeaponWz2Catalog.Int(SoulWeaponWz2Catalog.Grade(grade),"ActiveSkillID");
        var skillIcon=WzAssets.Texture(WzAssets.Node("Skill/800128.img/skill/"+sid+"/icon"));
        // Switcher lt(345,226) + animation offset(57,41) = icon centre(402,267).
        if(skillIcon!=null)GUI.DrawTexture(At(r,386,251,32,32),skillIcon);
        var skillSlot=At(r,387,251,59,46);
        if(gui&&modal==null&&Event.current.type==EventType.Repaint&&skillSlot.Contains(Pointer.Pos)&&Pointer.Clicked)
        {
            Pointer.ConsumeClick();
            if(Event.current.clickCount>=2)
            {
                if(!live)BottomBar.Toast("请先启用这件武器");
                else{SkillWindow.CancelSkillDrag();string error=NativeSoulWeapon.Cast(sid);if(error!=null)BottomBar.Toast(error);}
            }
            else if(live)SkillWindow.PickSkill(sid);
        }
        Label(r,"攻／魔 +"+SoulWeaponWz2Catalog.ActiveAttackPercent(grade)+"%（40秒）\n单击放快捷栏／双击施放\n冷却120秒",
            "Main/ActiveSkillArea/ActiveSkillDescBorder");
        Label(r,grown?SoulWeaponWz2Catalog.DescribeOption(option):"尚未启用灵魂武器","Main/PotentialOptionArea/PotentialOptionTextBorder");
        if(Button(r,"Main/PotentialOptionArea/$btRoll",enabled:live))
            NativeUtilPrompt.Confirm("当前烙印："+SoulWeaponWz2Catalog.DescribeOption(option)+"\n消耗灵魂碎片 "+SoulWeaponWz2Catalog.Int(SoulWeaponWz2Catalog.Grade(grade),"OptionRollCost")+" 个重设？",ok=>{if(ok)Send(SoulOperation.RollModern);});
        Digits(r,"L"+level,"Main/PassiveSkillArea/SoulLevelTextBorder/$txtSoulLevel/font",38,418);
        int need=SoulWeaponWz2Catalog.RequiredExp(level+1);
        Image(r,"Main/PassiveSkillArea/$SoulGauge/backgrnd",88,413);
        var fill=WzAssets.Texture(Node("Main/PassiveSkillArea/$SoulGauge/fill"));
        float ratio=level>=100?1:need<=0?0:Mathf.Clamp01((float)exp/need);
        if(fill!=null && ratio>0)GUI.DrawTextureWithTexCoords(At(r,88,413,263*ratio,18),fill,new Rect(0,0,ratio,1));
        Label(r,level>=100?"MAX":exp+" / "+need,"Main/PassiveSkillArea/SoulExpTextBorder");
        bool gradeUp=level>=SoulWeaponWz2Catalog.MaxLevel(grade);
        string up="Main/PassiveSkillArea/$LevelGradeUpButtonSwitcher/"+(gradeUp?"GradeUp/$btGradeUp":"LevelUp/$btLevelUp");
        if(Button(r,up,352,412,live && level<100))
        {
            if(gradeUp){modal="GradeUp";}
            else
            {
                int max=Math.Min(state.fragments,NativeSoulWeaponWz2Service.RemainingExp(growth??new SoulGrowth()));
                if(max<=0)BottomBar.Toast("灵魂碎片不足");
                else {amountText="1";modal="LevelUp";}
            }
        }
        Label(r,"物理攻击力／魔法攻击力 + "+SoulWeaponWz2Catalog.PassiveAttack(level),"Main/PassiveSkillArea/PassiveSkillOptionTextBorder");
        Image(r,"Main/FragmentArea/FragmentImage/image",351,498);
        Label(r,state.fragments+"个","Main/FragmentArea/FragmentAmountTextBorder");
        GUI.enabled=gui;
        if(modal!=null)DrawModal();
        return false;
    }
    static void Digits(Rect r,string text,string path,float x,float y)
    {
        foreach(char c in text){var t=WzAssets.Texture(Node(path+"/"+c));if(t==null)continue;Image(r,path+"/"+c,x,y);x+=t.width;}
    }
    static void Animate(Rect r,string path,float x,float y,int delay)
    {
        var n=Node(path);if(n==null)return;int count=0;foreach(var c in n.Children)if(int.TryParse(c.Name,out _))count++;
        if(count>0)Image(r,path+"/"+((int)(Time.realtimeSinceStartup*1000/delay)%count),x,y);
    }
    static void DrawModal()
    {
        var node=Node(modal=="GradeUp"?"GradeUp/$GradeUpSwitcher/Confirm/back/image":modal=="GradeResult"?"GradeUp/$GradeUpSwitcher/Result/back/backgrnd":modal+"/backgrnd");
        var texture=WzAssets.Texture(node);if(texture==null){modal=null;return;}
        modalRect=new Rect((UiKit.W-texture.width*scale)/2,(UiKit.H-texture.height*scale)/2,texture.width*scale,texture.height*scale);
        NativeUi.RegisterWindow("SoulWeaponWz2Modal",modalRect,()=>modal!=null);
        if(modal=="LevelUp")
        {
            DrawLevelUp();
        }
        else if(modal=="GradeResult")
        {
            string root="GradeUp/$GradeUpSwitcher/Result";
            var bound=State.Active??new SoulGrowth();
            Image(modalRect,root+"/back/backgrnd");
            Image(modalRect,root+"/$ResultCurrentGradeImageSwiter/"+(bound.grade-1),90,0);
            Image(modalRect,root+"/$ResultBeforeGradeImageSwitcher/"+(previousGrade-1),94,223);
            Image(modalRect,root+"/$ResultAfterGradeImageSwitcher/"+(bound.grade-1),220,215);
            if(Button(modalRect,root+"/btResultOK"))CloseModal();
        }
        else if(modal=="GradeUp")
        {
            string root="GradeUp/$GradeUpSwitcher/Confirm";
            var bound=State.Active??new SoulGrowth();
            Image(modalRect,root+"/back/image");
            Image(modalRect,root+"/$ConfrimCurrentGradeImageSwitcher/"+(bound.grade-1),90,0);
            Image(modalRect,root+"/$ConfirmBeforeGradeImageSwitcher/"+(bound.grade-1),94,205);
            Image(modalRect,root+"/$ConfirmAfterGradeImageSwitcher/"+Math.Min(9,bound.grade),220,196);
            Image(modalRect,root+"/ConditionArea/back/image",46,321);
            int quest=SoulWeaponWz2Catalog.Int(SoulWeaponWz2Catalog.Grade(bound.grade),"GradeUpReqQuestID");
            Label(modalRect,ConditionText(quest),root+"/ConditionArea/QuestNameBorder");
            Image(modalRect,root+"/ConditionArea/$ConditionAreaSwitcher/"+(State.cleared.Contains(quest)?"Enable":"Disable")+"/back/image",271,329);
            if(Button(modalRect,root+"/$btConfirmOK")){previousGrade=bound.grade;if(Send(SoulOperation.GradeModern))modal="GradeResult";}
            if(Button(modalRect,root+"/btConfirmCancel"))modal=null;
        }
        else
        {
            Image(modalRect,modal+"/backgrnd");
            if(Button(modalRect,modal+"/$btOK")){if(Send(modal=="Notice/Deactivate"?SoulOperation.DeactivateModern:SoulOperation.ActivateModern))modal=null;}
            if(modal!=null && Button(modalRect,modal+"/$btClose"))modal=null;
        }
    }
    static void DrawLevelUp()
    {
        const string root="LevelUp";
        var bound=State.Active??new SoulGrowth();
        Image(modalRect,root+"/backgrnd");
        Digits(modalRect,bound.level.ToString(),root+"/LevelArea/LevelTextBorder/$txtLevel/font",33,39);
        int need=SoulWeaponWz2Catalog.RequiredExp(bound.level+1);
        Image(modalRect,root+"/LevelArea/$ExpGauge/backgrnd",60,41);
        var fill=WzAssets.Texture(Node(root+"/LevelArea/$ExpGauge/fill"));
        float ratio=need<=0?0:Mathf.Clamp01((float)bound.exp/need);
        if(fill!=null&&ratio>0)GUI.DrawTextureWithTexCoords(At(modalRect,60,41,166*ratio,10),fill,new Rect(0,0,ratio,1));
        Digits(modalRect,bound.exp.ToString(),root+"/LevelArea/CurrentExpTextBorder/$txtCurrentExp/font",234,43);
        Digits(modalRect,need.ToString(),root+"/LevelArea/ReqExpTextBorder/$txtReqExp/font",267,43);
        Image(modalRect,root+"/AmountControlArea/IconImage/image",42,78);
        Image(modalRect,root+"/AmountControlArea/InputBoxArea/InputBoxImage/image",71,73);
        NativeEditKeyboard.Sync("SoulWeaponLevel",ref amountText);
        var input=At(modalRect,71,75,109,20);
        var style=new GUIStyle {font=UiKit.Font,fontSize=Math.Max(1,Mathf.RoundToInt(12*scale)),alignment=TextAnchor.MiddleRight};
        style.normal.textColor=Color.white;
        amountText=GUI.TextField(input,amountText,10,style);
        if(Event.current.type==EventType.Repaint&&Pointer.Clicked&&input.Contains(Pointer.Pos))
        {Pointer.ConsumeClick();NativeEditKeyboard.Open("SoulWeaponLevel",amountText,TouchScreenKeyboardType.NumberPad,10,true);}
        int max=Math.Min(State.fragments,NativeSoulWeaponWz2Service.RemainingExp(bound));
        int.TryParse(amountText,out int amount);
        if(Button(modalRect,root+"/AmountControlArea/$btUp",enabled:amount<max))amountText=Math.Min(max,Math.Max(0,amount)+1).ToString();
        if(Button(modalRect,root+"/AmountControlArea/$btDown",enabled:amount>1))amountText=Math.Max(1,amount-1).ToString();
        if(Button(modalRect,root+"/AmountControlArea/$btMax",enabled:max>0))amountText=max.ToString();
        if(Button(modalRect,root+"/$btOK",enabled:amount>0&&amount<=max))
        {if(Send(SoulOperation.LevelModern,amount))CloseModal();}
        if(Button(modalRect,root+"/btClose"))CloseModal();
    }
    static string ConditionText(int quest)
    {
        var names=new List<string>();foreach(var pair in SoulWeaponWz2Catalog.Bosses)
            if(pair.Value is Dictionary<string,object> b && SoulWeaponWz2Catalog.Int(b,"QuestID_GradeUp")==quest)names.Add(SoulWeaponWz2Catalog.BossName(b));
        return names.Count==0?"已达最高阶级":string.Join(" / ",names)+" 单人击破";
    }
    static void ShowBosses()
    {
        var pages=new List<string>();string text="";
        foreach(var pair in SoulWeaponWz2Catalog.Bosses)
        {
            var b=pair.Value as Dictionary<string,object>;int q=SoulWeaponWz2Catalog.Int(b,"BossIcon");
            bool done=State.week==NativeWeeklyGemstoneService.Week(DateTimeOffset.UtcNow)&&State.weekly.TryGetValue(q.ToString(),out int received)&&received>=SoulWeaponWz2Catalog.Int(b,"FragmentAmount");
            text+=SoulWeaponWz2Catalog.BossName(b)+"："+SoulWeaponWz2Catalog.Int(b,"FragmentAmount")+"个"+(done?"（本周已记录）":"")+"\n";
            if(text.Split('\n').Length>=7){pages.Add(text);text="";}
        }
        if(text!="")pages.Add(text);NativeUtilPrompt.Pages("灵魂碎片获得记录",pages.ToArray());
    }
}
