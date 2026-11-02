using System.Collections.Generic;
using UnityEngine;

// CUIFamily 80862B/808A54/8091D2, CUIFamilyTree 809CCC/80AD31.
public static class NativeFamilyWindow
{
    static Vector2 position = new Vector2(80, 40), grab;
    static bool dragging, tree;
    static List<CommunityMember> treeMembers;
    static int promptedInvite, promptedSummon;
    static int privilege, focusId, selectedId;
    static List<object> entitlements;
    const string Root = "UI/UIWindow.img/Family/", TreeRoot = "UI/UIWindow.img/FamilyTree/";
    static readonly Vector2[] places = { new Vector2(222,40), new Vector2(222,94), new Vector2(222,146),
        new Vector2(222,198),new Vector2(365,198),new Vector2(80,250),new Vector2(365,250),
        new Vector2(13,302),new Vector2(148,302),new Vector2(297,302),new Vector2(432,302) };
    public static void Open(string name = null)
    {
        tree = false; privilege = 0; focusId = GameSession.CharacterId; selectedId = 0;
        treeMembers = null; promptedInvite = promptedSummon = 0;
        if (!string.IsNullOrEmpty(name) && name != GameSession.Character.record.name) {
            NativeCommunityWindow.InputName = name;
            var result = Request(CommunityOperation.ReadFamilyTree);
            if (result.ok) { tree = true; focusId = result.treeTarget; treeMembers = result.familyTree; }
            else NativeCommunityWindow.Visible = false;
        }
    }
    static CommunityResponse Request(CommunityOperation op, int target = 0, int value = 0)
        => NativeCommunityWindow.Request(op, target, value);
    static void Invite()
    {
        NativeUtilPrompt.Text("请输入要登记为直属同学的角色名。必须位于相同频道和地图。", NativeCommunityWindow.InputName, 2, 13,
            name => { NativeCommunityWindow.InputName = name; Request(CommunityOperation.InviteFamily); });
    }
    static void Use()
    {
        int index = privilege;
        void Submit()
        {
            var result = Request(CommunityOperation.UseFamily, 0, index);
            if (result.ok && result.map != Profile.LastMapId) { NativeCommunityWindow.Visible = false; GameLoop.I?.field?.WarpTo(result.map); }
        }
        if (index <= 1) NativeUtilPrompt.Text("请输入学院成员的角色名。", "", 2, 13,
            name => { NativeCommunityWindow.InputName = name; Submit(); });
        else NativeUtilPrompt.Confirm("是否使用此名门世家特权？", yes => { if (yes) Submit(); });
    }
    static void Separate(List<CommunityMember> family)
    {
        var self=family.Find(m=>m.id==GameSession.CharacterId);
        void Confirm(CommunityMember relation)
        {
            if(self==null || relation==null)return;
            bool senior=relation.id==self.senior;
            if(!senior && relation.senior!=self.id)return;
            var junior=senior?self:relation;
            int penalty=(junior.level/20+10)*junior.level*2;
            string message=senior?"学长将被扣除 "+penalty+" 点声望。":"你将被扣除 "+penalty+" 点声望。";
            var parent=senior?relation:self;
            if(parent.senior!=0)message+="上一级学长将被扣除 "+(penalty/2)+" 点声望。";
            // 80B385/80B5D2 show the impact on the entire lower branch before submitting.
            message+="所有下属学院成员也会受到影响。是否与所选角色解除关系？";
            NativeUtilPrompt.Confirm(message,yes=> {if(yes) {
                var reply=Request(CommunityOperation.SeparateFamily,senior?0:relation.id);
                if(reply.ok) {treeMembers=null;focusId=GameSession.CharacterId;selectedId=0;}
            }});
        }
        var selected=family.Find(m=>m.id==selectedId);
        if(selected!=null)Confirm(selected);
        else NativeUtilPrompt.Text("请输入要解除关系的角色名。","",4,12,
            name=>Confirm(family.Find(m=>m.name==name)));
    }
    public static void Draw(CommunityResponse state)
    {
        if (!NativeUtilPrompt.Visible && state.familyInvite > 0 && promptedInvite != state.familyInvite) {
            promptedInvite = state.familyInvite;
            NativeUtilPrompt.Confirm("是否接受学长的直属同学登记邀请？", yes => { if (yes) Request(CommunityOperation.AcceptFamily); });
        }
        if (!NativeUtilPrompt.Visible && state.summonFrom > 0 && promptedSummon != state.summonFrom) {
            promptedSummon = state.summonFrom;
            NativeUtilPrompt.Confirm("是否接受学院同学的召集？", yes => {
                var reply = Request(CommunityOperation.AnswerSummon, 0, yes ? 1 : 0);
                if (yes && reply.ok && reply.map != Profile.LastMapId) { NativeCommunityWindow.Visible = false; GameLoop.I?.field?.WarpTo(reply.map); }
            });
        }
        entitlements ??= (List<object>)MiniJsonDb.Parse(Resources.Load<TextAsset>("FamilyEntitlements").text);
        float scale = BottomBar.S; int width = tree ? 578 : 224, height = tree ? 386 : 392;
        position = NativeUi.ClampWindowPosition(position, new Vector2(width,height),scale);
        var pointer = Pointer.Pos / scale;
        if (Event.current.type == EventType.Repaint && Pointer.Pressed && new Rect(position.x,position.y,width-24,20).Contains(pointer))
        { dragging = true; grab = pointer-position; }
        if (dragging && Pointer.Down) position = NativeUi.ClampWindowPosition(pointer-grab,new Vector2(width,height),scale);
        if (!Pointer.Down) dragging = false;
        NativeUi.RegisterWindow("NativeFamilyWindow", new Rect(position.x*scale,position.y*scale,width*scale,height*scale),
            () => NativeCommunityWindow.SchoolVisible);
        var matrix = GUI.matrix; bool enabled = GUI.enabled;
        GUI.matrix = matrix * Matrix4x4.Scale(new Vector3(scale,scale,1));
        GUI.BeginGroup(new Rect(position.x,position.y,width,height));
        try
        {
            var p = pointer-position; string root = tree ? TreeRoot : Root;
            void Image(string path, float x, float y)
            { var tex=Art.Tex("wz://"+path); if(tex!=null) GUI.DrawTexture(new Rect(x,y,tex.width,tex.height),tex); }
            bool Button(string node,int x,int y,bool active=true)
            {
                var tex=Art.Tex("wz://"+root+node+"/normal/0"); if(tex==null)return false;
                GUI.enabled=enabled && active && !NativeUtilPrompt.Visible;
                return NativeUi.Button(new Rect(x,y,tex.width,tex.height),root+node,p);
            }
            var style = new GUIStyle { font=UiKit.Font,fontSize=12,wordWrap=false,clipping=TextClipping.Clip };
            style.normal.textColor=Color.black;
            void Text(string text,int x,int y,int w) => GUI.Label(new Rect(x,y,w,17),text,style);
            Image(root+"backgrnd",0,0);
            // 808404 / 809ABB pass close type3 at206,6 / type4 at558,10 to92C0BF.
            GUI.enabled=enabled && !NativeUtilPrompt.Visible;
            string closePath=tree?TreeRoot+"BtClose":"UI/Basic.img/BtClose";
            var close=Art.Tex("wz://"+closePath+"/normal/0");
            if(close!=null && NativeUi.Button(new Rect(tree?558:206,tree?10:6,close.width,close.height),closePath,p))
            {NativeCommunityWindow.Visible=false;return;}
            var family = tree && treeMembers != null ? treeMembers : state.family;
            CommunityMember Find(int id) => family.Find(m=>m.id==id);
            var self=Find(GameSession.CharacterId);
            var leader=family.Find(m=>m.senior==0);
            if(tree)
            {
                bool ownTree=focusId==GameSession.CharacterId;
                Text(leader==null?"尚未加入学院":leader.name+"学院",205,10,167);
                var center=Find(focusId)??self;
                var members=new CommunityMember[11]; members[0]=leader;members[3]=center;
                members[2]=Find(center?.senior??0);members[1]=Find(members[2]?.senior??0);
                if(members[2]!=null) members[4]=family.Find(m=>m.senior==members[2].id && m.id!=center?.id);
                var juniors=family.FindAll(m=>m.senior==center?.id);
                for(int i=0;i<2 && i<juniors.Count;i++)
                {
                    members[5+i]=juniors[i];var children=family.FindAll(m=>m.senior==juniors[i].id);
                    for(int j=0;j<2 && j<children.Count;j++)members[7+i*2+j]=children[j];
                }
                if(!NativeUtilPrompt.Visible) {
                    selectedId=0;
                    if(ownTree)foreach(int index in new[] {2,5,6})
                        if(members[index]!=null && new Rect(places[index].x,places[index].y,133,34).Contains(p))selectedId=members[index].id;
                }
                if(Button("BtJuniorEntry",21,57,ownTree && juniors.Count<2)) Invite();
                if(Button("BtBye",71,57,ownTree)) Separate(family);
                for(int i=0;i<members.Length;i++)
                {
                    var m=members[i];var at=places[i];
                    if(m==null) {if(i!=3)Text(i==0?"尚未有学院长":i<3?"尚未有学长":"尚未有同学",(int)at.x,(int)at.y+11,133);continue;}
                    Image(TreeRoot+(i==0?"PlateLeader/":"PlateOthers/")+(m.online?1:0),at.x,at.y);
                    Text(m.name,(int)at.x,(int)at.y+5,133);Text("Lv."+m.level+" "+JobDefs.NameOf(m.job),(int)at.x-10,(int)at.y+20,153);
                    if(selectedId==m.id)Image(TreeRoot+"selected",at.x-3,at.y-3);
                }
                return;
            }
            Text(leader==null?"尚未加入学院":leader.name+"学院",7,33,209);
            Text(state.family.FindAll(m=>m.senior==GameSession.CharacterId).Count+" / 2",52,81,37);
            Text(state.reputation.ToString("N0")+" / "+state.totalRep.ToString("N0"),54,111,159);
            Text(state.todayRep.ToString("N0"),106,128,107);
            if(Button("BtJuniorEntry",95,77,state.family.FindAll(m=>m.senior==GameSession.CharacterId).Count<2))Invite();
            if(Button("BtTree",164,77)) {tree=true;treeMembers=null;focusId=GameSession.CharacterId;}
            if(Button("BtLeft",15,177))privilege=(privilege+10)%11;
            if(Button("BtRight",196,177))privilege=(privilege+1)%11;
            if(Button("BtFamilyPrecept",209,55,state.familyId>0 && self?.senior==0))NativeUtilPrompt.Text("请输入学院家训。",state.precepts,1,200,
                text=>{NativeCommunityWindow.InputText=text;Request(CommunityOperation.FamilyPrecepts);});
            if(Button("BtSpecial",152,322,state.familyId>0 && !state.used.Contains(privilege) && state.reputation>=NativeCommunityService.FamilyCosts[privilege]))Use();
            if(Button("BtOK",93,368))NativeCommunityWindow.Visible=false;
            var entry=(Dictionary<string,object>)entitlements[privilege];
            Text(entry["name"].ToString(),15,178,193);Text(NativeCommunityService.FamilyCosts[privilege].ToString("N0"),74,198,43);
            Text((state.used.Contains(privilege)?1:0)+" / 1",175,198,32);Text((privilege+1)+" / 11",158,158,40);
            style.wordWrap=true;
            GUI.Label(new Rect(16,214,195,104),entry["description"].ToString().Replace("#c", "").Replace("#", ""),style);
        }
        finally {GUI.EndGroup();GUI.matrix=matrix;GUI.enabled=enabled;}
    }
}
