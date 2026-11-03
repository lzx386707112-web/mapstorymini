using System;
using System.Collections.Generic;
using MapleDash.Wz;
using UnityEngine;

// Original Enchant.img coordinates, fonts and artwork. See docs/tms282-skill/16-enhancement-entry.md.
public static class NativeEnhancementWindow
{
    const string Root="UI/Enchant.img/";
    static readonly string[] Pages={"starForce","potential","additionalPotential","traceTransfer"};
    static readonly string[] Grades={"Rare","Rare","Epic","Unique","Legendary"};
    static InventoryEntry equipment,material;
    static StarForceResponse star;
    static PotentialResponse potential;
    static int page,owner,gridRow,statRow;
    static bool safeguard;
    static string error;
    public static bool Visible {get;private set;}
    // Mobile presentation: preserve IMG geometry and leave 9% of the viewport above/below.
    static float S=>Mathf.Min(BottomBar.S,Mathf.Min((UiKit.W-20)/565f,UiKit.H*.82f/720f));
    static Rect Window=>new Rect((UiKit.W-565*S)/2,(UiKit.H-720*S)/2,565*S,720*S);
    static Rect At(Rect r,float x,float y,float w,float h)=>new Rect(r.x+x*S,r.y+y*S,w*S,h*S);
    static Rect ChoiceArea=>new Rect((UiKit.W-474*S)/2,UiKit.H/2-140*S,474*S,255*S);
    public static bool AtPointer=>Visible&&(Window.Contains(Pointer.Pos)||potential?.pending==true&&ChoiceArea.Contains(Pointer.Pos));
    static Rect PageRect=>At(Window,147,0,418,720);
    static bool Repaint=>Event.current.type==EventType.Repaint;
    static int Value(InventoryEntry item,string key)
        =>item?.equipmentData!=null&&item.equipmentData.TryGetValue(key,out var v)?Convert.ToInt32(v):0;
    static InventoryEntry Find(long id)=>GameSession.Character?.inventory.Find(x=>x.itemRowId==id);
    public static void Close(){Visible=false;equipment=null;material=null;star=null;potential=null;error=null;}
    public static void Open(int mode=0,InventoryEntry cost=null)
    {
        Close();if(!GameSession.InGame)return;
        Visible=true;owner=GameSession.CharacterId;page=Mathf.Clamp(mode,0,3);material=cost;gridRow=statRow=0;safeguard=false;
        // The persisted journal is authoritative, even if its last cube has already been consumed.
        foreach(var item in GameSession.Character.inventory)
            if(item.equipmentData!=null&&item.equipmentData.TryGetValue("potentialChoice",out var raw)&&raw is string text&&!string.IsNullOrEmpty(text))
            {
                var pending=MiniJsonDb.Parse(text) as Dictionary<string,object>;
                if(pending==null)continue;
                equipment=item;page=(bool)pending["bonus"]?2:1;
                potential=GameSession.Net.Potential(new PotentialRequest {characterId=owner,inventoryItemId=item.itemRowId,choose=true});
                if(!potential.ok){error=potential.error;potential=null;}break;
            }
    }
    public static string Select(InventoryEntry item)
    {
        if(!Visible||item==null)return "请选择持有的装备或方块";
        if(potential?.pending==true)return "请先选择上一次方块结果";
        if(item.itemId/1000000==1)
        {
            if(page==3&&equipment!=null&&Value(item,"starForceVestige")==0&&item.itemId==equipment.itemId&&item.itemRowId!=equipment.itemRowId)
                material=item;
            else {equipment=item;material=page==3?null:material;safeguard=false;statRow=0;}
        }
        else
        {
            var family=CubeRules.GetFamily(item.itemId);
            bool scroll=item.itemId==2049400||item.itemId==2049401;
            if(family==null&&!scroll)return "此物品不是已接入的方块或潜能赋予卷轴";
            page=family?.bonus==true?2:1;material=item;
        }
        Preview();return error;
    }
    public static bool Drop(Vector2 pointer,int id,int position)
    {
        if(!Visible||!Window.Contains(pointer))return false;
        // Only the item's original hit region accepts a target; other window areas swallow the drop.
        var item=GameSession.Character.inventory.Find(x=>x.itemId==id&&x.position==position);
        var r=PageRect;
        if(At(r,9,32,409,page==3?199:219).Contains(pointer)||id/1000000!=1)
        {string message=Select(item);if(message!=null)BottomBar.Toast(message);}
        return true;
    }
    static void Preview()
    {
        star=null;potential=null;error=null;
        if(equipment==null)return;
        equipment=Find(equipment.itemRowId);
        if(equipment==null){error="装备已不在身上";return;}
        if(material!=null)material=Find(material.itemRowId);
        if(page==0||page==3)
        {
            if(page==3&&material==null)return;
            star=GameSession.Net.StarForce(new StarForceRequest {characterId=owner,inventoryItemId=equipment.itemRowId,
                sacrificeInventoryItemId=page==3?material.itemRowId:0,safeguard=safeguard});
            if(!star.ok)error=star.error;
        }
        else
        {
            if(material==null)return;
            potential=GameSession.Net.Potential(new PotentialRequest {characterId=owner,inventoryItemId=equipment.itemRowId,cubeInventoryItemId=material.itemRowId});
            if(!potential.ok)error=potential.error;
        }
    }
    static void Settle(List<InventoryEntry> inventory,long meso)
    {
        GameSession.Character.inventory=inventory;GameSession.Character.record.meso=meso;Profile.Data.gold=meso;
        GameSession.ApplyInventoryToProfile();GameLoop.I?.player?.RefreshBuffAttributes();Profile.Save();
        if(equipment!=null)equipment=Find(equipment.itemRowId);
        if(material!=null)material=Find(material.itemRowId);
    }
    public static string Confirm()
    {
        if(!Visible||owner!=GameSession.CharacterId||equipment==null)return "请先选择装备";
        if(potential?.pending==true)return "请先选择方块结果";
        if(page==0||page==3)
        {
            if(star?.ok!=true||page==3&&material==null)return error??"请选择复原材料";
            var result=GameSession.Net.StarForce(new StarForceRequest {characterId=owner,inventoryItemId=equipment.itemRowId,
                sacrificeInventoryItemId=page==3?material.itemRowId:0,safeguard=safeguard,commit=true,revision=star.revision});
            if(!result.ok){Preview();return result.error;}
            Settle(result.inventory,result.meso);
            BottomBar.Toast(result.restored?"装备痕迹已复原":result.result.vestige?"装备已损坏，痕迹已保留":result.result.outcome==StarForceRules.Outcome.Success?"强化成功":"强化失败");
            if(result.result?.vestige==true){page=3;material=null;}
            if(page==0&&equipment!=null&&!StarForceRules.Shared.CanSafeguard(equipment.starLevel,(NativeItemSource.Node(equipment.itemId)?.Int("info/superiorEqp")??0)!=0))safeguard=false;
            Preview();return null;
        }
        if(potential?.ok!=true||material==null)return error??"请选择方块或潜能赋予卷轴";
        var response=GameSession.Net.Potential(new PotentialRequest {characterId=owner,inventoryItemId=equipment.itemRowId,
            cubeInventoryItemId=material.itemRowId,commit=true,revision=potential.revision});
        if(!response.ok){Preview();return response.error;}
        Settle(response.inventory,response.meso);potential=response;
        if(!string.IsNullOrEmpty(response.scrollResult))BottomBar.Toast(response.scrollResult=="success"?"潜能赋予成功":"潜能赋予失败");
        if(!response.pending)Preview();return null;
    }
    public static string Choose(bool keepNew)
    {
        if(!Visible||equipment==null||potential?.pending!=true)return "没有待选择的结果";
        var response=GameSession.Net.Potential(new PotentialRequest {characterId=owner,inventoryItemId=equipment.itemRowId,
            choose=true,commit=true,keepNew=keepNew,revision=potential.revision});
        if(!response.ok)return response.error;
        Settle(response.inventory,response.meso);Preview();return null;
    }
    static void Image(Rect r,string path,float x=0,float y=0)
    {
        var node=WzAssets.Node(Root+path);if(node==null)return;
        var texture=WzAssets.Texture(node);if(texture==null)return;
        var origin=WzAssets.Origin(node);GUI.DrawTexture(At(r,x-origin.x,y-origin.y,texture.width,texture.height),texture);
    }
    // The English captions are baked into the original backgrounds. Reuse a blank
    // strip of the same title bar beneath the user-requested Chinese text.
    static void ChineseTitle(Rect r,string background,string title,float blankX,float width)
    {
        var texture=WzAssets.Texture(WzAssets.Node(Root+background));
        if(texture==null)return;
        GUI.DrawTextureWithTexCoords(At(r,8,7,width,18),texture,
            new Rect(blankX/texture.width,1-25f/texture.height,1f/texture.width,18f/texture.height));
        Text(r,title,10,8,width-4,"common/font:meso");
    }
    static bool Button(Rect r,string path)
    {
        var node=WzAssets.Node(Root+path+"/normal/0");if(node==null)return false;
        var t=WzAssets.Texture(node);if(t==null)return false;
        var o=WzAssets.Origin(node);return NativeUi.Button(At(r,-o.x,-o.y,t.width,t.height),Root+path);
    }
    static void Text(Rect r,string text,float x,float y,float w,string font,TextAnchor align=TextAnchor.UpperLeft,float height=20)
    {
        var n=WzAssets.Node(Root+font);var style=new GUIStyle {font=UiKit.Font,fontSize=Mathf.Max(1,Mathf.RoundToInt((n?.Int("fontSize",12)??12)*S)),
            fontStyle=n?.Int("fontBold")==1?FontStyle.Bold:FontStyle.Normal,alignment=align,wordWrap=true};
        string color=n?.Text("fontColor")??"FFFFFFFF";
        if(uint.TryParse(color,System.Globalization.NumberStyles.HexNumber,null,out uint c))
            style.normal.textColor=new Color32((byte)(c>>16),(byte)(c>>8),(byte)c,(byte)(c>>24));
        GUI.Label(At(r,x,y,w,height),text,style);
    }
    static void Icon(Rect r,InventoryEntry item,float x,float y,float size=32)
    {
        if(item==null)return;
        if(size==64)
        {
            var node=NativeItemSource.IconNode(item.itemId);if(node==null)return;
            var texture=WzAssets.Texture(node);var origin=WzAssets.Origin(node);
            if(texture!=null)GUI.DrawTexture(At(r,x-origin.x*2,y+(32-origin.y)*2,texture.width*2,texture.height*2),texture);
            return;
        }
        var t=NativeItemSource.Icon(item.itemId);
        if(t!=null)GUI.DrawTexture(At(r,x,y,size,size),t,ScaleMode.ScaleToFit);
    }
    static string Describe(int option)
    {
        if(option==0)return "";if(option<0)return "未鉴定潜能";
        int level=(NativeItemSource.Node(equipment.itemId)?.Int("info/reqLevel")??0)+Value(equipment,"iIncReq");
        return PotentialCatalog.Shared.Describe(option,PotentialCatalog.OptionLevel(level));
    }
    static int[] Options(bool bonus)
        =>new[]{Value(equipment,bonus?"bonusOption1":"potentialOption1"),Value(equipment,bonus?"bonusOption2":"potentialOption2"),Value(equipment,bonus?"bonusOption3":"potentialOption3")};
    static void DrawChoice()
    {
        string root="fullScreen_"+Pages[page]+"/choiceBox/";
        NativeUi.RegisterWindow("NativeEnhancementChoice",ChoiceArea,()=>Visible&&potential?.pending==true);
        var old=Options(page==2);
        for(int side=0;side<2;side++)
        {
            var r=At(ChoiceArea,side*244,0,232,255);
            Image(r,root+"canvas:base"+(side==0?"Before":"After"));
            bool hover=r.Contains(Pointer.Pos);
            if(hover)Image(r,root+"layer:mouseOver");
            int grade=side==0?Value(equipment,page==2?"bonusGrade":"potentialGrade"):potential.grade;
            Image(r,root+"title/canvas:title_"+Grades[Mathf.Clamp(grade,1,4)].ToLowerInvariant());
            var lines=side==0?old:potential.candidate;
            for(int i=0;i<3;i++)Text(r,Describe(lines[i]),24,93+i*29,184,root+"detail/font:detail",height:29);
            if(GUI.enabled&&Repaint&&Pointer.Clicked&&hover)
            {Pointer.ConsumeClick();string message=Choose(side==1);if(message!=null)BottomBar.Toast(message);return;}
        }
    }
    static void DrawMaterials(Rect r)
    {
        bool restore=page==3;string mode=Pages[page];
        float x=restore?19:page==2?8:20,y=restore?489:page==2?461:492;
        if(!restore)Image(r,page==2?mode+"/layer:costInvenBox":mode+"/costItem/layer:costItemBox",page==2?0:10,page==2?0:467);
        var rows=new List<InventoryEntry>();
        foreach(var item in GameSession.Character.inventory)
        {
            if(item.position<=0||item.itemRowId==equipment?.itemRowId)continue;
            var family=CubeRules.GetFamily(item.itemId);
            if(restore?equipment!=null&&item.itemId==equipment.itemId&&Value(item,"starForceVestige")==0:
                family!=null&&family.bonus==(page==2)||page==1&&(item.itemId==2049400||item.itemId==2049401))rows.Add(item);
        }
        rows.Sort((a,b)=>a.position.CompareTo(b.position));
        int max=Math.Max(0,(rows.Count+8)/9-2);gridRow=Mathf.Clamp(gridRow,0,max);
        if(GUI.enabled&&Repaint&&At(r,x,y,380,83).Contains(Pointer.Pos)&&Mathf.Abs(Input.mouseScrollDelta.y)>.01f)
            gridRow=Mathf.Clamp(gridRow-(int)Mathf.Sign(Input.mouseScrollDelta.y),0,max);
        for(int i=0;i<18&&i+gridRow*9<rows.Count;i++)
        {
            var item=rows[i+gridRow*9];float sx=x+i%9*41,sy=y+2+i/9*41;
            if(material?.itemRowId==item.itemRowId)Image(r,mode+"/costItem/inven/selected",sx,sy);
            Icon(r,item,sx+4,sy-1);
            Text(r,item.quantity.ToString(),sx,sy+25,38,mode+"/costItem/font:costItem");
            if(Repaint&&At(r,sx,sy,38,38).Contains(Pointer.Pos))
            {
                ItemTooltip.SetHover(item.itemId,false,item);
                if(GUI.enabled&&Pointer.Clicked){Pointer.ConsumeClick();material=item;Preview();}
            }
        }
        if(material!=null)Text(r,Server.Bot.BotItemCatalog.Shared.Name(material.itemId),19,restore?467:474,380,mode+"/costItem/font:costItem",TextAnchor.UpperCenter);
    }
    public static void Draw()
    {
        if(!Visible)return;
        if(!GameSession.InGame||owner!=GameSession.CharacterId){Close();return;}
        var window=Window;var r=PageRect;string mode=Pages[page];
        NativeUi.RegisterWindow("NativeEnhancementWindow",window,()=>Visible);
        Image(window,"common/backgrnd");Image(r,mode+"/backgrnd");
        ChineseTitle(window,"common/backgrnd","装备强化",120,125);
        ChineseTitle(r,mode+"/backgrnd",new[]{"星力强化","主潜能","附加潜能","痕迹复原"}[page],330,300);
        for(int i=0;i<Pages.Length;i++)
        {
            var tab=At(window,13,66+i*62,120,59);
            Image(tab,Pages[i]+"/tab/"+(i==page?"checked":"normal")+"/0");
            if(GUI.enabled&&Repaint&&Pointer.Clicked&&tab.Contains(Pointer.Pos)&&potential?.pending!=true)
            {Pointer.ConsumeClick();page=i;material=null;safeguard=false;gridRow=0;Preview();return;}
        }
        Text(window,Profile.gold.ToString("N0"),4,701,138,"common/font:meso",TextAnchor.UpperCenter);
        if(Button(r,"common/commonButton/button:close")){Close();return;}
        if(potential?.pending==true){DrawChoice();return;}
        if(equipment==null)Image(r,mode+"/layer:waitEquip");
        else
        {
            if(page==3){Image(r,mode+"/layer:onEquip");Icon(r,equipment,81,112,64);Icon(r,material,251,112,64);}
            else {Icon(r,equipment,166,112,64);Text(r,Server.Bot.BotItemCatalog.Shared.Name(equipment.itemId),9,155,400,mode+"/item/font:itemName",TextAnchor.UpperCenter);}
        }
        if(page==0)
        {
            if(equipment==null)Image(r,mode+"/layer:waitEquipMeso");
            else
            {
                foreach(string layer in new[]{"summaryBox","diffBox","statBox","probBox","costMesoBox"})Image(r,mode+"/layer:"+layer);
                Text(r,equipment.starLevel.ToString(),104,285,50,mode+"/font:statValue",TextAnchor.UpperCenter);
                Text(r,(equipment.starLevel+1).ToString(),234,285,50,mode+"/font:statValue",TextAnchor.UpperCenter);
                if(star?.ok==true&&star.quote!=null)
                {
                    Text(r,(star.quote.success/100.0).ToString("0.##")+"%",214,473,180,mode+"/font:probNormal");
                    Text(r,((10000-star.quote.success-star.quote.destroy)/100.0).ToString("0.##")+"%",214,489,180,mode+"/font:probNormal");
                    Text(r,(star.quote.destroy/100.0).ToString("0.##")+"%",214,505,180,mode+"/font:probNormal");
                    Text(r,star.quote.cost.ToString("N0"),96,549,240,mode+"/costMeso/font:costMesoNormal",TextAnchor.UpperCenter);
                    if(star.quote.chanceTime)Image(r,mode+"/layer:chanceTime");
                    var stats=new List<string>();foreach(var pair in star.nextStats)if(pair.Value!=0||star.derivedStats.TryGetValue(pair.Key,out int v)&&v!=0)stats.Add(pair.Key);
                    if(GUI.enabled&&Repaint&&At(r,18,318,382,136).Contains(Pointer.Pos))statRow=Mathf.Clamp(statRow-(int)Mathf.Sign(Input.mouseScrollDelta.y),0,Math.Max(0,stats.Count-8));
                    for(int i=0;i<8&&i+statRow<stats.Count;i++)
                    {string key=stats[i+statRow];star.derivedStats.TryGetValue(key,out int before);Text(r,key.ToUpperInvariant(),18,318+i*16,169,mode+"/font:statName",TextAnchor.UpperRight);Text(r,before+" → "+star.nextStats[key],196,318+i*16,204,mode+"/font:statValue");}
                }
                bool superior=(NativeItemSource.Node(equipment.itemId)?.Int("info/superiorEqp")??0)!=0;
                if(superior)Image(r,mode+"/layer:superior");
                bool enabled=GUI.enabled;GUI.enabled&=StarForceRules.Shared.CanSafeguard(equipment.starLevel,superior);
                if(Button(r,mode+"/button_protectDestroy/button")){safeguard=!safeguard;Preview();}
                Image(r,mode+"/button_protectDestroy/"+(safeguard?"checked":"unchecked"));GUI.enabled=enabled;
            }
        }
        else
        {
            if(page!=3&&equipment!=null)
            {
                int grade=Value(equipment,page==2?"bonusGrade":"potentialGrade");
                if(grade>0)Image(r,mode+"/summaryBox/layer:summary"+Grades[Mathf.Clamp(grade,1,4)]);
                Image(r,mode+"/layer:statBox");var options=Options(page==2);
                for(int i=0;i<3;i++)Text(r,Describe(options[i]),105,305+i*24,295,mode+"/font:stat",height:24);
                if(potential?.ok==true)Text(r,potential.cost.ToString("N0"),104,644,210,mode+"/font:stat",TextAnchor.UpperCenter);
            }
            DrawMaterials(r);
        }
        if(!string.IsNullOrEmpty(error))Text(r,error,18,615,382,mode+"/font:stat",TextAnchor.UpperCenter,50);
        bool wasEnabled=GUI.enabled;GUI.enabled&=equipment!=null&&(page==0||page==3?star?.ok==true:potential?.ok==true);
        if(Button(r,mode+"/button:confirm")){string message=Confirm();if(message!=null)BottomBar.Toast(message);}
        GUI.enabled=wasEnabled;
        if(Repaint&&equipment!=null&&At(r,9,32,409,219).Contains(Pointer.Pos))ItemTooltip.SetHover(equipment.itemId,false,equipment);
    }
}
