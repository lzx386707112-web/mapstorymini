using System;
using System.Collections.Generic;
using MapleDash.Wz;
using UnityEngine;

// Original Enchant.img coordinates, fonts and artwork. See docs/tms282-skill/16-enhancement-entry.md.
public static class NativeEnhancementWindow
{
    // 前四个页签用旧版 Enchant.img（像素内嵌、ARGB4444，现有绘制坐标都按它标定）；
    // 星火(bonusStat)与灵魂(soulWeapon)是 282 才加的分区，只在 Enchant282.img 里，
    // 它的画布是 _outlink 到 UI/_Canvas/Enchant.img 的 RGB565/ARGB4444。
    // 两个文件并存：换掉旧文件会把 effect/fullScreen 那批 2050 格式的画布带进来，
    // ImgFile.Rgba 解不了，现有的星力/潜能特效就全废了。
    // 282 整理后六页签（含星火 bonusStat / 灵魂 soulWeapon）全部内嵌在这一个文件里，
    // 不再有 Enchant282.img 与 UI/_Canvas/Enchant.img 的分体结构。
    const string Root="UI/Enchant.img/";
    // 0 星力 / 1 主潜能 / 2 附加潜能 / 3 痕迹复原 / 4 轮回星火 / 5 灵魂武器
    static readonly string[] Pages={"starForce","potential","additionalPotential","traceTransfer","bonusStat","soulWeapon"};
    static readonly string[] Titles={"星力强化","主潜能","附加潜能","痕迹复原","轮回星火","灵魂武器"};
    static readonly string[] Grades={"Rare","Rare","Epic","Unique","Legendary"};
    static InventoryEntry equipment,material;
    static StarForceResponse star;
    static PotentialResponse potential;
    static FlameResponse flame;
    static SoulResponse soul;
    static int page,owner,gridRow,statRow;
    static bool safeguard,dragging,positioned;
    static Vector2 position,grab;
    static readonly Dictionary<string,Texture2D> compactTextures=new Dictionary<string,Texture2D>();
    static string error;
    const string LegacySoulMaterialMessage="已切换为282新版灵魂武器，不再消耗旧附魔石或灵魂宝珠。请放入武器后点击武器格启用，成长和重设烙印使用灵魂碎片。";
    static bool IsLegacySoulMaterial(int id)=>id/1000==SoulCatalog.EnchanterBase/1000||id/1000==SoulCatalog.SoulBase/1000;
    public static bool Visible {get;private set;}
    // One stable outer canvas for all tabs, including an outstanding cube choice.
    // 星火/灵魂页签的素材是完整 418x720，没有可裁的空白，所以按页签放宽外框。
    static float Width=>page==5?612:565;
    static float Height=>page>=4?720:620;
    static readonly float[] PotentialCuts={214,240,400,455,580,599};
    static readonly float[] StarCuts={214,228,383,455,572,578,606,614};
    static readonly float[] TraceCuts={280,310,370,420,580,600};
    static readonly float[] NoCuts={};
    static float[] Cuts=>page==0?StarCuts:page==3?TraceCuts:page>=4?NoCuts:PotentialCuts;
    static float Y(float y)
    {
        var cuts=Cuts;
        float mapped=y;
        for(int i=0;i<cuts.Length;i+=2)mapped-=Mathf.Clamp(y-cuts[i],0,cuts[i+1]-cuts[i]);
        return mapped;
    }
    static float S=>Mathf.Min(BottomBar.S,Mathf.Min((UiKit.W-24)/Width,UiKit.H*.80f/Height));
    static Rect Window
    {
        get
        {
            var p=positioned?NativeUi.ClampWindowPosition(position,new Vector2(Width,Height),S):
                new Vector2((UiKit.W/S-Width)/2,(UiKit.H/S-Height)/2);
            return new Rect(p.x*S,p.y*S,Width*S,Height*S);
        }
    }
    static Rect RawAt(Rect r,float x,float y,float w,float h)=>new Rect(r.x+x*S,r.y+y*S,w*S,h*S);
    static Rect At(Rect r,float x,float y,float w,float h)=>RawAt(r,x,Y(y),w,Y(y+h)-Y(y));
    static Rect ChoiceArea=>RawAt(new Rect((UiKit.W-478*S)/2,UiKit.H/2-140*S,0,0),0,0,478,255);
    public static bool AtPointer=>Visible&&(dragging||Window.Contains(Pointer.Pos)||potential?.pending==true&&ChoiceArea.Contains(Pointer.Pos)||page==5&&NativeSoulWeaponPanel.AtPointer);
    static Rect PageRect=>At(Window,147,0,418,720);
    static bool Repaint=>Event.current.type==EventType.Repaint;
    static int Value(InventoryEntry item,string key)
        =>item?.equipmentData!=null&&item.equipmentData.TryGetValue(key,out var v)?Convert.ToInt32(v):0;
    static InventoryEntry Find(long id)=>GameSession.Character?.inventory.Find(x=>x.itemRowId==id);
    public static void Close()
    {
        NativeSoulWeaponPanel.Reset();
        Visible=dragging=positioned=false;equipment=null;material=null;star=null;potential=null;flame=null;soul=null;error=null;
        foreach(var texture in compactTextures.Values)UnityEngine.Object.Destroy(texture);
        compactTextures.Clear();
    }
    public static void Open(int mode=0,InventoryEntry cost=null)
    {
        Close();if(!GameSession.InGame)return;
        Visible=true;owner=GameSession.CharacterId;page=Mathf.Clamp(mode,0,5);material=cost;gridRow=statRow=0;safeguard=false;
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
        if(page==5)NativeSoulWeaponPanel.Refresh();
        if(potential!=null)return;
        foreach(var item in GameSession.Character.inventory)
            if(!string.IsNullOrEmpty(item.flameChoice))
            {equipment=item;material=null;page=4;Preview();break;}
        if(page==5&&material!=null&&IsLegacySoulMaterial(material.itemId))
        {material=null;BottomBar.Toast(LegacySoulMaterialMessage);}
    }
    public static string Select(InventoryEntry item)
    {
        if(!Visible||item==null)return "请选择持有的装备或方块";
        if(potential?.pending==true)return "请先选择上一次方块结果";
        if(flame?.pending==true)return "请先选择上一次星火结果";
        if(item.itemId/1000000==1)
        {
            if(page==3&&equipment!=null&&Value(item,"starForceVestige")==0&&item.itemId==equipment.itemId&&item.itemRowId!=equipment.itemRowId)
                material=item;
            else {equipment=item;material=page==3?null:material;safeguard=false;statRow=0;}
        }
        else
        {
            // 当前停在星火/灵魂页签时，先按页签认自己的道具，别被方块族抢走。
            if(page==4&&FlameCatalog.CreateTypeOf(item.itemId)>=0){material=item;Preview();return error;}
            if(IsLegacySoulMaterial(item.itemId))
            {page=5;material=null;Preview();return LegacySoulMaterialMessage;}
            var family=CubeRules.GetFamily(item.itemId);
            bool scroll=item.itemId==2049400||item.itemId==2049401;
            if(FlameCatalog.CreateTypeOf(item.itemId)>=0){page=4;material=item;Preview();return error;}
            if(family==null&&!scroll)return "此物品不是已接入的强化道具";
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
        star=null;potential=null;flame=null;soul=null;error=null;
        if(page==5){NativeSoulWeaponPanel.Refresh();return;}
        if(equipment==null)return;
        equipment=Find(equipment.itemRowId);
        if(equipment==null){error="装备已不在身上";return;}
        if(material!=null)material=Find(material.itemRowId);
        if(page>=4)
        {
            if(page==4&&!string.IsNullOrEmpty(equipment.flameChoice))
            {
                flame=GameSession.Net.Flame(new FlameRequest{characterId=owner,inventoryItemId=equipment.itemRowId,choose=true});
                if(!flame.ok)error=flame.error;
                return;
            }
            if(material==null){error="请点击下方材料格，选择要消耗的道具";return;}
            if(page==4)
            {
                flame=GameSession.Net.Flame(new FlameRequest{characterId=owner,inventoryItemId=equipment.itemRowId,
                    flameInventoryItemId=material.itemRowId});
                if(!flame.ok)error=flame.error;
            }
            else
            {
                soul=GameSession.Net.Soul(new SoulRequest{characterId=owner,inventoryItemId=equipment.itemRowId,
                    soulInventoryItemId=material.itemRowId,operation=SoulOp(material.itemId)});
                if(!soul.ok)error=soul.error;
            }
            return;
        }
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
        if(flame?.pending==true)return "请先选择星火结果";
        if(page>=4)return ConfirmGrowth();
        if(page==0||page==3)
        {
            if(star?.ok!=true||page==3&&material==null)return error??"请选择复原材料";
            var result=GameSession.Net.StarForce(new StarForceRequest {characterId=owner,inventoryItemId=equipment.itemRowId,
                sacrificeInventoryItemId=page==3?material.itemRowId:0,safeguard=safeguard,commit=true,revision=star.revision});
            if(!result.ok){Preview();return result.error;}
            Settle(result.inventory,result.meso);
            // 称号试炼·死神：只计星力页(0)的升星成功（page==3 是痕迹复原，不算）。
            if(page==0&&result.result?.outcome==StarForceRules.Outcome.Success)NativeTitleTrials.AddStar(1);
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
        if(!response.pending){Preview();if(string.IsNullOrEmpty(response.scrollResult))BottomBar.Toast("潜能已重新设定");}return null;
    }
    // 2590xxx 是开孔器、2591xxx 是灵魂石；同一个页签里靠道具 id 分派服务端操作。
    static SoulOperation SoulOp(int itemId)
        =>itemId/1000==SoulCatalog.EnchanterBase/1000?SoulOperation.Socket:SoulOperation.Enchant;
    static string ConfirmGrowth()
    {
        if(material==null)return "请选择要消耗的道具";
        if(page==4)
        {
            if(flame?.ok!=true)return error??"请先取得有效预览";
            var result=GameSession.Net.Flame(new FlameRequest{characterId=owner,inventoryItemId=equipment.itemRowId,
                flameInventoryItemId=material.itemRowId,commit=true,revision=flame.revision});
            if(!result.ok){Preview();return result.error;}
            Settle(result.inventory,GameSession.Character.record.meso);
            if(result.pending){flame=result;BottomBar.Toast("请选择要保留的星火结果");return null;}
            BottomBar.Toast(result.candidate!=null&&result.candidate.Count>0?"星火已附加":"星火没有产生任何属性");
            Preview();return null;
        }
        var operation=SoulOp(material.itemId);
        var soulResult=GameSession.Net.Soul(new SoulRequest{characterId=owner,inventoryItemId=equipment.itemRowId,
            soulInventoryItemId=material.itemRowId,operation=operation,commit=true});
        if(!soulResult.ok){Preview();return soulResult.error;}
        Settle(soulResult.inventory,GameSession.Character.record.meso);
        BottomBar.Toast(operation==SoulOperation.Socket
            ?(soulResult.success?"灵魂插槽已开启":"开孔失败，开孔器已消耗")
            :"灵魂已镶嵌："+soulResult.optionKey);
        Preview();return null;
    }
    // 黑星火/深渊星火的两段式：先摇、再前后对比选保留原结果还是替换成新的。
    public static string ChooseFlame(bool keepNew)
    {
        if(!Visible||equipment==null||page!=4||flame?.pending!=true)return "没有待确认的星火结果";
        var response=GameSession.Net.Flame(new FlameRequest{characterId=owner,inventoryItemId=equipment.itemRowId,
            choose=true,commit=true,keepNew=keepNew,revision=flame.revision});
        if(!response.ok)return response.error;
        Settle(response.inventory,GameSession.Character.record.meso);
        BottomBar.Toast(keepNew?"已使用新的星火结果":"已保留原来的星火结果");
        Preview();return null;
    }
    public static string Choose(bool keepNew)
    {
        if(!Visible||equipment==null||potential?.pending!=true)return "没有待选择的结果";
        var response=GameSession.Net.Potential(new PotentialRequest {characterId=owner,inventoryItemId=equipment.itemRowId,
            choose=true,commit=true,keepNew=keepNew,revision=potential.revision});
        if(!response.ok)return response.error;
        Settle(response.inventory,response.meso);Preview();return null;
    }
    static void Image(Rect r,string path,float x=0,float y=0,bool remap=true)
    {
        var node=WzAssets.Node(Root+path);if(node==null)return;
        var texture=WzAssets.Texture(node);if(texture==null)return;
        var origin=WzAssets.Origin(node);float top=y-origin.y,left=x-origin.x;
        if(!remap){GUI.DrawTexture(RawAt(r,left,top,texture.width,texture.height),texture);return;}
        int height=Mathf.RoundToInt(Y(top+texture.height)-Y(top));
        if(height<=0)return;
        if(height!=texture.height)
        {
            string key=path+"|"+page+"|"+top;
            if(!compactTextures.TryGetValue(key,out var compact))
            {
                // Join original IMG rows before scaling. Separate GUI quads can round
                // their shared edges differently and leave horizontal seams.
                var pixels=WzAssets.Pixels(node);int stride=texture.width*4,output=0;
                var joined=new byte[stride*height];
                for(int row=0;row<texture.height;row++)
                    if(Y(top+row+1)>Y(top+row))
                    {Buffer.BlockCopy(pixels,row*stride,joined,output*stride,stride);output++;}
                compact=WzAssets.Upload(joined,texture.width,height,"Enchant/"+key);
                compactTextures.Add(key,compact);
            }
            texture=compact;
        }
        GUI.DrawTexture(RawAt(r,left,Y(top),texture.width,texture.height),texture);
    }

    // The English captions are baked into the original backgrounds. Reuse a blank
    // strip of the same title bar beneath the user-requested Chinese text.
    static void ChineseTitle(Rect r,string background,string title,float blankX,float width,bool remap=true)
    {
        var texture=WzAssets.Texture(WzAssets.Node(Root+background));
        if(texture==null)return;
        GUI.DrawTextureWithTexCoords(RawAt(r,8,7,width,18),texture,
            new Rect(blankX/texture.width,1-25f/texture.height,1f/texture.width,18f/texture.height));
        Text(r,title,10,8,width-4,"common/font:meso",remap:remap);
    }
    static bool Button(Rect r,string path)
    {
        var node=WzAssets.Node(Root+path+"/normal/0");if(node==null)return false;
        var t=WzAssets.Texture(node);if(t==null)return false;
        var o=WzAssets.Origin(node);var rect=At(r,-o.x,-o.y,t.width,t.height);
        bool hover=rect.Contains(Pointer.Pos);
        bool enabled=GUI.enabled&&(!NativeUtilPrompt.Visible||NativeUtilPrompt.Drawing);
        string state=!enabled?"disabled":hover?Pointer.Down?"pressed":"mouseOver":"normal";
        var texture=WzAssets.Texture(WzAssets.Node(Root+path+"/"+state+"/0"))??t;
        GUI.DrawTexture(rect,texture);
        if(!enabled||!Repaint||!Pointer.Clicked||!hover)return false;
        Pointer.ConsumeClick();AudioManager.Play("UI/BtMouseClick",.7f,false);return true;
    }
    static void Text(Rect r,string text,float x,float y,float w,string font,TextAnchor align=TextAnchor.UpperLeft,float height=20,bool remap=true)
    {
        var n=WzAssets.Node(Root+font);var style=new GUIStyle {font=UiKit.Font,fontSize=Mathf.Max(1,Mathf.RoundToInt(Mathf.Max(14,n?.Int("fontSize",12)??12)*S)),
            fontStyle=FontStyle.Bold,alignment=align,wordWrap=true};
        string color=n?.Text("fontColor")??"FFFFFFFF";
        if(uint.TryParse(color,System.Globalization.NumberStyles.HexNumber,null,out uint c))
            style.normal.textColor=new Color32((byte)(c>>16),(byte)(c>>8),(byte)c,(byte)(c>>24));
        GUI.Label(remap?At(r,x,y,w,height):RawAt(r,x,y,w,height),text,style);
    }
    static void Icon(Rect r,InventoryEntry item,float x,float y,float size=32)
    {
        if(item==null)return;
        if(size==64)
        {
            var node=NativeItemSource.IconNode(item.itemId);if(node==null)return;
            var texture=WzAssets.Texture(node);
            // Original frame center is (itemIcon.x+43, itemIcon.y-1).
            if(texture!=null)GUI.DrawTexture(At(r,x+11,y-33,64,64),texture,ScaleMode.ScaleToFit);
            return;
        }
        var t=NativeItemSource.Icon(item.itemId);
        if(t!=null)GUI.DrawTexture(At(r,x,y,size,size),t,ScaleMode.ScaleToFit);
    }
    static string Describe(int option)
    {
        if(option==0)return "";if(option<0)return "未鉴定潜能";
        int level=(NativeItemSource.Node(equipment.itemId)?.Int("info/reqLevel")??0)+Value(equipment,"iIncReq");
        return PotentialCatalog.Shared.Describe(option,PotentialCatalog.OptionLevel(level)).Replace("STR","力量").Replace("DEX","敏捷").Replace("INT","智力").Replace("LUK","幸运").Replace("BOSS","首领");
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
            var r=RawAt(ChoiceArea,side*244,0,234,255);
            Image(r,root+"canvas:base"+(side==0?"Before":"After"),remap:false);
            ChineseTitle(r,root+"canvas:base"+(side==0?"Before":"After"),side==0?"保留原潜能":"使用新潜能",220,210,remap:false);
            bool hover=r.Contains(Pointer.Pos);
            if(hover)Image(r,root+"layer:mouseOver",remap:false);
            int grade=side==0?Value(equipment,page==2?"bonusGrade":"potentialGrade"):potential.grade;
            Image(r,root+"title/canvas:title_"+Grades[Mathf.Clamp(grade,1,4)].ToLowerInvariant(),remap:false);
            var lines=side==0?old:potential.candidate;
            for(int i=0;i<3;i++)Text(r,Describe(lines[i]),24,93+i*29,184,root+"detail/font:detail",height:29,remap:false);
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
        var window=Window;
        if(Repaint&&GUI.enabled)
        {
            // Exclude the close button at page-local x395 from the title drag strip.
            if(Pointer.Pressed&&RawAt(window,0,0,Width-30,28).Contains(Pointer.Pos))
            {dragging=positioned=true;position=window.position/S;grab=Pointer.Pos/S-position;Pointer.ConsumeClick();}
            if(dragging)
            {
                if(Pointer.Down)position=NativeUi.ClampWindowPosition(Pointer.Pos/S-grab,new Vector2(Width,Height),S);
                else {dragging=false;Pointer.ConsumeClick();}
                window=Window;
            }
        }
        var r=At(window,147,0,418,720);string mode=Pages[page];
        NativeUi.RegisterWindow("NativeEnhancementWindow",window,()=>Visible);
        Image(window,"common/backgrnd");if(page!=5)Image(r,mode+"/backgrnd");
        ChineseTitle(window,"common/backgrnd","装备强化",120,125);
        if(page!=5)ChineseTitle(r,mode+"/backgrnd",Titles[page],330,300);
        for(int i=0;i<Pages.Length;i++)
        {
            var tab=RawAt(window,13,66+i*62,120,59);
            var tabNode=WzAssets.Node(Root+Pages[i]+"/tab/"+(i==page?"checked":"normal")+"/0");
            var tabTexture=tabNode!=null?WzAssets.Texture(tabNode):null;
            if(tabTexture!=null)GUI.DrawTexture(tab,tabTexture);
            if(GUI.enabled&&Repaint&&Pointer.Clicked&&tab.Contains(Pointer.Pos)&&potential?.pending!=true&&flame?.pending!=true)
            {if(page==5&&NativeSoulWeaponPanel.HasModal)continue;Pointer.ConsumeClick();page=i;material=null;safeguard=false;gridRow=0;Preview();return;}
        }
        Text(window,Profile.gold.ToString("N0"),4,701,138,"common/font:meso",TextAnchor.UpperCenter,height:18);
        if(page==5){if(NativeSoulWeaponPanel.Draw(r,S,equipment))Close();return;}
        if(Button(r,"common/commonButton/button:close")){Close();return;}
        if(potential?.pending==true){DrawChoice();return;}
        if(page<4&&equipment==null)Image(r,mode+"/layer:waitEquip");
        else if(page<4)
        {
            if(page==3){Image(r,mode+"/layer:onEquip");Icon(r,equipment,81,112,64);Icon(r,material,251,112,64);}
            else {Icon(r,equipment,166,112,64);Text(r,Server.Bot.BotItemCatalog.Shared.Name(equipment.itemId),9,184,400,mode+"/item/font:itemName",TextAnchor.UpperCenter);}
        }
        if(page>=4)
        {
            if(page==4)DrawFlameTab(r,mode);else DrawSoulTab(r,mode);
        }
        else if(page==0)
        {
            if(equipment==null)Image(r,mode+"/layer:waitEquipMeso");
            else
            {
                foreach(string layer in new[]{"summaryBox","diffBox","statBox","probBox","costMesoBox"})Image(r,mode+"/layer:"+layer);
                bool superior=(NativeItemSource.Node(equipment.itemId)?.Int("info/superiorEqp")??0)!=0;
                // Original star row is anchored on starForce/vector:summary (209,260); the cap comes
                // from the authoritative preview so the row can never disagree with the quote.
                int cap=star?.cap>0?star.cap:StarForceRules.Shared.Cap(equipment.itemId,
                    (NativeItemSource.Node(equipment.itemId)?.Int("info/reqLevel")??0)+Value(equipment,"iIncReq"),superior);
                StarForceStars.Draw(r.x+209*S,r.y+Y(260)*S,S,equipment.starLevel,cap,superior);
                if(star?.ok==true && star.quote!=null)
                {
                    string diff=mode+"/diff/"+(superior?"blue":"normal");
                    Image(r,diff+"/before/"+equipment.starLevel);
                    Image(r,diff+"/layer:arrow");
                    Image(r,diff+"/after/"+(equipment.starLevel+1));
                }
                else Text(r,equipment.starLevel>=cap?"已达 "+cap+" 星上限":"当前 "+equipment.starLevel+" 星",66,285,286,
                    mode+"/font:statValue",TextAnchor.UpperCenter);
                if(star?.ok==true&&star.quote!=null)
                {
                    Text(r,"失败（不破坏）",18,489,180,mode+"/font:probName",height:16);
                    Text(r,"破坏概率",18,505,180,mode+"/font:probName",height:16);
                    Text(r,(star.quote.success/100.0).ToString("0.##")+"%",214,473,180,mode+"/font:probNormal",height:16);
                    Text(r,((10000-star.quote.success-star.quote.destroy)/100.0).ToString("0.##")+"%",214,489,180,mode+"/font:probNormal",height:16);
                    Text(r,(star.quote.destroy/100.0).ToString("0.##")+"%",214,505,180,mode+"/font:probNormal",height:16);
                    Text(r,star.quote.cost.ToString("N0"),96,549,240,mode+"/costMeso/font:costMesoNormal",TextAnchor.UpperCenter);
                    if(star.quote.chanceTime)Image(r,mode+"/layer:chanceTime");
                    string[] statKeys={"watk","matk","str","dex","int","luk","hp","mp","wdef","mdef","acc","avoid","speed","jump"};
                    string[] statNames={"星力攻击力","星力魔法力","星力力量","星力敏捷","星力智力","星力运气","星力HP","星力MP","星力防御力","星力魔法防御力","星力命中率","星力回避率","星力移动速度","星力跳跃力"};
                    var stats=new List<int>();
                    for(int i=0;i<statKeys.Length;i++)
                        if(star.nextStats.TryGetValue(statKeys[i],out int next)&&next!=0)stats.Add(i);
                    if(GUI.enabled&&Repaint&&At(r,18,318,382,64).Contains(Pointer.Pos))statRow-=(int)Mathf.Sign(Input.mouseScrollDelta.y);
                    statRow=Mathf.Clamp(statRow,0,Math.Max(0,stats.Count-4));
                    for(int i=0;i<4&&i+statRow<stats.Count;i++)
                    {
                        int index=stats[i+statRow];string key=statKeys[index];
                        star.derivedStats.TryGetValue(key,out int before);int next=star.nextStats[key];
                        Text(r,statNames[index],18,318+i*16,169,mode+"/font:statName",TextAnchor.UpperRight,height:16);
                        Text(r,"+"+before+" → +"+next+"（增加 "+(next-before)+"）",196,318+i*16,204,mode+"/font:statValue",height:16);
                    }
                }
                if(superior)Image(r,mode+"/layer:superior");
                bool canProtect=star?.ok==true&&StarForceRules.Shared.CanSafeguard(equipment.starLevel,superior);
                bool enabled=GUI.enabled;GUI.enabled&=canProtect;
                if(Button(r,mode+"/button_protectDestroy/button")){safeguard=!safeguard;Preview();}
                Image(r,mode+"/button_protectDestroy/"+(safeguard?canProtect?"checked":"checkedAndDisabled":"unchecked"));GUI.enabled=enabled;
                if(enabled&&Repaint&&Pointer.Clicked&&At(r,111,579,195,22).Contains(Pointer.Pos)&&!canProtect)
                {
                    Pointer.ConsumeClick();BottomBar.Toast(superior?"尊贵装备不能使用防破坏保护":
                        star?.ok!=true?error??"请先取得有效强化预览":"仅当前15、16、17星的普通装备可开启，费用为3倍");
                }
            }
        }
        else
        {
            if(page!=3&&equipment!=null)
            {
                int grade=Value(equipment,page==2?"bonusGrade":"potentialGrade");
                if(grade>0)Image(r,mode+"/summaryBox/layer:summary"+Grades[Mathf.Clamp(grade,1,4)]);
                Image(r,mode+"/layer:statBox");var options=Options(page==2);
                // statBox 烘焙标题（潛在能力/附加潛在能力）占底图 y298..309，条目必须从 313 起，否则压住标题。
                for(int i=0;i<3;i++)Text(r,Describe(options[i]),24,313+i*24,370,mode+"/font:stat",height:24);
                if(potential?.ok==true&&string.IsNullOrEmpty(error))Text(r,"费用："+potential.cost.ToString("N0"),104,644,210,mode+"/font:stat",TextAnchor.UpperCenter);
            }
            DrawMaterials(r);
        }
        if(!string.IsNullOrEmpty(error))Text(r,error,18,page>=4?640:615,382,mode+(page==5?"/detailSoul/font:statNormal":page==4?"/font:statNormal":"/font:stat"),TextAnchor.UpperCenter,32);
        // 黑星火/深渊星火摇完是「先落地待确认」，这时只能二选一，不能再点确认（会再消耗一颗）。
        if(page==4&&flame?.pending==true){DrawFlameChoice(r);return;}
        bool wasEnabled=GUI.enabled;
        GUI.enabled&=equipment!=null&&(page==0||page==3?star?.ok==true:page==4?flame?.ok==true:page==5?soul?.ok==true:potential?.ok==true);
        string confirmName=page==4?"button:confirm":page==5
            ?(SoulOp(material?.itemId??0)==SoulOperation.Socket?"button:confirmEnchanter":"button:confirmSoul")
            :"button:confirm";
        if(Button(r,mode+"/"+confirmName)){string message=Confirm();if(message!=null)BottomBar.Toast(message);}
        GUI.enabled=wasEnabled;
        if(Repaint&&equipment!=null&&At(r,9,32,409,219).Contains(Pointer.Pos))ItemTooltip.SetHover(equipment.itemId,false,equipment);
    }

    // ——————————————— 轮回星火页签（Enchant282.img/bonusStat） ———————————————
    // 坐标取自 282 素材：itemLT(9,32)/itemRB(418,251)、summaryBox(67,242) 287x26、
    // statBox(10,271) 398x140、atkPowBox(10,415) 398x43、costLT(10,490)/costRB(408,605)、
    // 材料格从 (20,517) 起 38x38 步长 41（9 列 2 行）。素材里成本有两种页签
    // （tab:cost 的金币/道具），轮回星火只消耗道具，所以固定走道具格那一套。
    static void DrawFlameTab(Rect r,string mode)
    {
        if(equipment==null)
        {
            Image(r,mode+"/layer:waitEquip");
            DrawGrowthGrid(r,true);
            return;
        }
        Icon(r,equipment,166,112,64);
        Text(r,Server.Bot.BotItemCatalog.Shared.Name(equipment.itemId),9,184,400,mode+"/item/font:itemName",TextAnchor.UpperCenter);
        Image(r,mode+"/layer:summaryBox");
        Text(r,"轮回星火",30,246,160,mode+"/font:statHighlight",height:20);
        var lines=NativeGrowthStats.DescribeFlame(equipment);
        Text(r,lines.Count+" 条属性",206,246,140,mode+"/font:statNormal",TextAnchor.UpperRight,20);

        Image(r,mode+"/layer:statBox");
        bool pending=flame?.pending==true;
        // 待确认时左半边画原结果、右半边画新结果，一眼能对比（282 的 fullScreen_bonusStat 同义）。
        // statBox 烘焙标题「追加屬性」占底图 y279..289，条目从 293 起、行距 30，四行收在底框（y411）内。
        float lineX=pending?18:40, lineW=pending?176:330;
        for(int i=0;i<lines.Count&&i<4;i++)
        {
            var line=lines[i];
            if(line.value<=0)continue;
            Text(r,line.label,lineX,293+i*30,lineW,mode+"/font:statNormal",height:26);
            Text(r,"+"+line.value,lineX,293+i*30,lineW,mode+"/font:statHighlight",TextAnchor.UpperRight,26);
        }
        if(pending)
        {
            var candidate=flame.candidate;
            for(int i=0;i<candidate.Count&&i<4;i++)
            {
                var line=candidate[i];
                if(line.value<=0)continue;
                Text(r,line.label,214,293+i*30,176,mode+"/font:statNormal",height:26);
                Text(r,"+"+line.value,214,293+i*30,176,mode+"/font:statHighlight",TextAnchor.UpperRight,26);
            }
        }

        Image(r,mode+"/layer:atkPowBox");
        int beforePower=Power(lines),afterPower=pending?Power(flame.candidate):beforePower;
        if(pending)
            Text(r,"攻击力 "+beforePower+" → "+afterPower,60,422,300,
                afterPower>=beforePower?mode+"/font:atkPow":mode+"/font:atkPowMinus",TextAnchor.UpperCenter,22);
        else
            Text(r,"攻击力 "+beforePower,60,422,300,mode+"/font:atkPow",TextAnchor.UpperCenter,22);

        Image(r,mode+"/tab:cost/normal/0");
        Image(r,mode+"/tab:cost/selected/1");
        DrawGrowthGrid(r,true);
    }
    // 星火对面板的贡献：沿用结算层的同一套换算，避免窗口写一套、结算写另一套。
    static int Power(List<FlameLine> lines)
    {
        if(lines==null)return 0;
        int total=0;
        foreach(var line in lines)
        {
            if(line.value<=0)continue;
            switch(line.statVal)
            {
                case 10: case 11: total+=line.value*4; break;      // 攻击/魔力折算
                case 0: case 1: case 2: case 3: total+=line.value; break;
                case 4: case 5: case 6: case 7: case 8: case 9: total+=line.value*2; break;
                case 13: total+=line.value/10; break;
                default: total+=line.value; break;
            }
        }
        return total;
    }
    // 待确认：只能二选一（保留原结果 / 使用新结果），点确认不会再次消耗星火。
    static void DrawFlameChoice(Rect r)
    {
        string mode=Pages[page];
        Text(r,"请选择要保留的结果",10,636,398,mode+"/font:statNormal",TextAnchor.UpperCenter,18);
        if(Button(r,mode+"/button:confirm1"))
        {string message=ChooseFlame(false);if(message!=null)BottomBar.Toast(message);return;}
        if(Button(r,mode+"/button:confirm"))
        {string message=ChooseFlame(true);if(message!=null)BottomBar.Toast(message);}
    }

    // ——————————————— 灵魂武器页签（Enchant282.img/soulWeapon） ———————————————
    // itemLT/itemRB 同星火；summaryBox(66,242) 286x37、summary(209,259) 居中；
    // detailBox(10,282) 398x130；costInvenBox(11,420) 396x115、
    // 材料格从 (20,444) 起 38x38 步长 41（9 列 2 行）；按钮 (147,675) 129x35。
    static void DrawSoulTab(Rect r,string mode)
    {
        if(equipment==null)
        {
            Image(r,mode+"/layer:waitEquip");
            DrawGrowthGrid(r,false);
            return;
        }
        Icon(r,equipment,166,112,64);
        Text(r,Server.Bot.BotItemCatalog.Shared.Name(equipment.itemId),9,184,400,mode+"/item/font:itemName",TextAnchor.UpperCenter);

        Image(r,mode+"/layer:summaryBox");
        if(equipment.soulSocketId<=0)
            Text(r,"尚未开孔",66,250,286,mode+"/font:summaryMagnificentSoul",TextAnchor.UpperCenter,22);
        else if(equipment.soulItemId<=0)
            Text(r,"插槽已开启，尚未镶魂",66,250,286,mode+"/font:summaryMagnificentSoul",TextAnchor.UpperCenter,22);
        else
            Text(r,Server.Bot.BotItemCatalog.Shared.Name(equipment.soulItemId),66,250,286,
                mode+"/font:summaryMagnificentSoul",TextAnchor.UpperCenter,22);

        bool socketing=material!=null&&SoulOp(material.itemId)==SoulOperation.Socket;
        if(socketing)
        {
            Image(r,mode+"/detailEnchanter/layer:detailEnchanterBox");
            Text(r,"灵魂开孔",20,290,200,mode+"/detailEnchanter/font:probName",height:20);
            Text(r,"成功率",176,340,140,mode+"/detailEnchanter/font:probName",height:18);
            Text(r,SoulSuccessText(),210,340,180,mode+"/detailEnchanter/font:probDetail",height:18);
            Text(r,equipment.soulSocketId>0?"这件武器已经有插槽":"武器等级需达到 "+SoulCatalog.MinLevel+" 级",20,360,370,
                mode+"/detailEnchanter/font:probDetail",height:20);
        }
        else
        {
            Image(r,mode+"/detailSoul/layer:detailSoulBox");
            Text(r,"灵魂属性",20,290,200,mode+"/detailSoul/font:statNormal",height:20);
            var currentSoul=SoulCatalog.Of(equipment.soulOption);
            Text(r,currentSoul==null?"—":currentSoul.key+" +"+currentSoul.value,20,360,120,
                mode+"/detailSoul/font:statNormal",height:20);
            Text(r,"→",150,360,30,mode+"/detailSoul/font:statNormal",TextAnchor.UpperCenter,20);
            string after=soul?.optionKey is string key&&!string.IsNullOrEmpty(key)?key+" +"+soul.optionValue:"—";
            Text(r,after,200,360,190,mode+"/detailSoul/font:statHighlight",height:20);
        }
        DrawGrowthGrid(r,false);
    }
    static string SoulSuccessText()
    {
        if(material==null)return "—";
        var info=Server.Bot.BotItemCatalog.Shared.Info(material.itemId);
        int success=info==null?100:Server.Bot.BotItemCatalog.Int(info,"success");
        return (success<=0?100:success)+"%";
    }

    // 两个页签共用的材料格：星火认 createType、灵魂认 2590/2591 段。
    static void DrawGrowthGrid(Rect r,bool flameTab)
    {
        string mode=Pages[page];
        if(flameTab)Image(r,mode+"/costItem/layer:costItemBox",10,490);
        else Image(r,mode+"/layer:costInvenBox");
        var rows=new List<InventoryEntry>();
        foreach(var item in GameSession.Character.inventory)
        {
            if(item.position<=0||item.itemRowId==equipment?.itemRowId)continue;
            int id=item.itemId;
            bool match=flameTab?FlameCatalog.CreateTypeOf(id)>=0
                :(id/1000==SoulCatalog.EnchanterBase/1000||id/1000==SoulCatalog.SoulBase/1000);
            if(match)rows.Add(item);
        }
        rows.Sort((a,b)=>a.position.CompareTo(b.position));
        float gx=20,gy=flameTab?517:444;
        int max=Math.Max(0,(rows.Count+8)/9-2);gridRow=Mathf.Clamp(gridRow,0,max);
        if(GUI.enabled&&Repaint&&At(r,gx,gy,369,83).Contains(Pointer.Pos)&&Mathf.Abs(Input.mouseScrollDelta.y)>.01f)
            gridRow=Mathf.Clamp(gridRow-(int)Mathf.Sign(Input.mouseScrollDelta.y),0,max);
        for(int i=0;i<18&&i+gridRow*9<rows.Count;i++)
        {
            var item=rows[i+gridRow*9];
            float sx=gx+i%9*41,sy=gy+i/9*41;
            if(material?.itemRowId==item.itemRowId)Image(r,mode+(flameTab?"/costItem/inven/selected":"/costInven/selected"),sx-1,sy-1);
            Icon(r,item,sx+4,sy+2);
            Text(r,item.quantity.ToString(),sx+2,sy+24,36,mode+(flameTab?"/costItem/font:costItem":"/font:costName"),TextAnchor.UpperRight,16);
            if(Repaint&&At(r,sx,sy,38,38).Contains(Pointer.Pos))
            {
                ItemTooltip.SetHover(item.itemId,false,item);
                if(GUI.enabled&&Pointer.Clicked){Pointer.ConsumeClick();material=item;Preview();}
            }
        }
        if(material!=null)
            Text(r,Server.Bot.BotItemCatalog.Shared.Name(material.itemId),20,flameTab?606:530,388,
                mode+(flameTab?"/costItem/font:costItem":"/font:costName"),TextAnchor.UpperCenter,18);
        else
            Text(r,rows.Count==0?"背包中没有对应的强化道具":"点击材料格选择道具（每次消耗1个）",20,flameTab?606:530,388,
                mode+(flameTab?"/costItem/font:costItem":"/font:costName"),TextAnchor.UpperCenter,32);
    }

}
