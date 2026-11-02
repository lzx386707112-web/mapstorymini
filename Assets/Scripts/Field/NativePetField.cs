using System;
using System.Collections.Generic;
using MapleDash.Wz;
using UnityEngine;

// CPet / CVecCtrlPet (703314, 70403D, 9C4122). Same field coordinates and
// footholds as the owner; never move a pet by parenting it to the player.
public sealed class NativePetField : MonoBehaviour
{
    sealed class Pet
    {
        public int id, template, slot, mode, facing=1, direction, clothing;
        public int fullness;
        public float x,y,vx,vy,climbWait;
        public bool ground, climbing;
        public string action;
        public double animationTime;
        public int frame;
        public GameObject root;
        public SpriteRenderer body,coat;
        public ImgNode image,equipment;
        public List<ImgNode> frames;
    }
    readonly Dictionary<int,Pet> pets=new Dictionary<int,Pet>();
    NativeMovementPhysics physics;
    FieldMap map;
    double readAt;
    float accumulated;
    float previousHp=-1,previousMp=-1;

    public void RecordVitals(PlayerCtrl owner) {previousHp=owner.hp;previousMp=owner.mp;}

    public void Tick(PlayerCtrl owner,float dt)
    {
        var field=GameLoop.I?.field;
        if(!GameSession.InGame || field?.Map==null || owner==null) {Clear();return;}
        if(map!=field.Map) {Clear();map=field.Map;readAt=0;}
        if(Time.realtimeSinceStartupAsDouble>=readAt)
        {
            readAt=Time.realtimeSinceStartupAsDouble+.25;
            Sync(owner);
        }
        NativePetAutopot.Tick(owner,previousHp,previousMp,pets.Count>0);
        previousHp=owner.hp; previousMp=owner.mp;
        physics ??= NativeMovementPhysics.Load();
        accumulated+=dt;
        while(accumulated>=.03f)
        {
            accumulated-=.03f;
            foreach(var p in pets.Values)
            {
                Move(p,owner,.03f);
                if(!p.climbing) GroundItem.TryPetPickup(new Vector2(p.x,p.y),p.slot,
                    CanPickup(p.slot,true),CanPickup(p.slot,false),()=>PickupTarget(p.slot));
            }
        }
        foreach(var p in pets.Values) Draw(p,owner,dt);
    }
    void Sync(PlayerCtrl owner)
    {
        var active=new HashSet<int>();
        for(int i=0;i<3;i++)
        {
            var item=GameSession.Net.ChangeInventory(new InventoryChangeRequest {
                characterId=GameSession.CharacterId,getPet=true,petSlot=i}).pet;
            if(item==null) continue;
            active.Add(item.petId);
            if(!pets.TryGetValue(item.petId,out var p))
            {
                p=new Pet {id=item.petId,template=item.itemId,slot=i,
                    image=WzAssets.FileRoot("Item/Pet/"+item.itemId.ToString("D7")+".img")};
                p.root=new GameObject("pet-"+item.petId);p.root.transform.SetParent(transform,false);
                p.body=p.root.AddComponent<SpriteRenderer>();
                var coat=new GameObject("equipment");coat.transform.SetParent(p.root.transform,false);
                p.coat=coat.AddComponent<SpriteRenderer>();
                Place(p,owner.FeetPos);
                pets.Add(p.id,p);
            }
            p.slot=i;p.fullness=Convert.ToInt32(item.petData["fullness"]);
            int clothing=Profile.EquipOfSlot(NativePetItems.EquipmentSlot(1802000,i));
            if(clothing!=p.clothing)
            {
                p.clothing=clothing;
                p.equipment=clothing>0 ? WzAssets.Node("Character/PetEquip/"+clothing.ToString("D8")+".img/"+p.template) : null;
            }
        }
        var removed=new List<int>();foreach(var pair in pets) if(!active.Contains(pair.Key)) removed.Add(pair.Key);
        foreach(int id in removed) {Remove(pets[id].root);pets.Remove(id);}
        foreach(var p in pets.Values)
            p.mode=pets.Count==3 ? new[] {5,3,4}[p.slot] : pets.Count==2 ? p.slot+1 : 0;
    }
    void Place(Pet p,Vector2 owner)
    {
        p.x=owner.x;p.y=owner.y+12/Art.PPU;p.vx=p.vy=0;p.ground=false;
        // SpawnPetProcessor uses owner.y-12 then the foothold below that point.
        if(PlayerCtrl.FindLanding(p.x,p.y,float.NegativeInfinity,-1,out float landing))
        {p.y=landing;p.ground=true;}
    }
    public Vector2? PickupTarget(int slot)
    {
        foreach(var p in pets.Values) if(p.slot==slot) return new Vector2(p.x,p.y);
        return null;
    }
    public static bool CanPickup(int slot,bool meso)
    {
        int id=meso ? 1812000 : 1812001;
        int worn=Profile.EquipOfSlot(NativePetItems.EquipmentSlot(id,slot));
        return worn>0 && (EquipCatalog.Info(worn)?.Int(meso ? "pickupMeso" : "pickupItem") ?? 0)!=0;
    }
    void Move(Pet p,PlayerCtrl owner,float dt)
    {
        Vector2 target=owner.FeetPos;
        float dx=(target.x-p.x)*Art.PPU,dy=(target.y-p.y)*Art.PPU;
        // Native 9C4422..9C4555: owner outside pet +/- (300,200),
        // validate the owner's foothold within +/-50, then warp.
        if((Mathf.Abs(dx)>300 || Mathf.Abs(dy)>200) &&
            PlayerCtrl.FindLanding(target.x,target.y+10/Art.PPU,float.NegativeInfinity,-1,out float ownerGround) &&
            Mathf.Abs(ownerGround-target.y)<50/Art.PPU)
        {
            NativeImgAnimation.Spawn("Effect/PetEff.img/Basic/Teleport",transform,
                new Vector3(p.x,p.y,0),owner.SortingOrder-1);
            Place(p,target);
        }
        if(owner.IsClimbing)
        {
            p.x=target.x;p.y=target.y;p.vx=p.vy=0;p.climbing=true;p.ground=false;p.climbWait=0;
            return;
        }
        if(p.climbing)
        {
            p.climbWait+=dt;
            if(p.climbWait<.2f) return;
            p.climbing=false;p.vy=owner.jumpSpeed;p.ground=false;
        }
        // CUserLocal 95ACFD / CVecCtrlPet 9C4797: native two/three-pet
        // formation, including the asymmetric 160/40px offsets on turning.
        float stop=80;
        if(owner.facing<0 && (p.mode==1 || p.mode==3)) target.x-=160/Art.PPU;
        if(owner.facing>0 && (p.mode==2 || p.mode==4)) target.x+=160/Art.PPU;
        if(owner.facing>0 && p.mode==3) {target.x-=40/Art.PPU;stop=70;}
        if(owner.facing<0 && p.mode==4) {target.x+=40/Art.PPU;stop=70;}
        if(p.mode==5) stop=60;
        dx=(target.x-p.x)*Art.PPU;
        // 9C4797 / 9C4B76: ordinary pursuit starts/stops at 80 pixels;
        // direction remains stable inside the band instead of chasing each frame.
        if(Mathf.Abs(dx)<stop) p.direction=0;
        else if(p.direction!=0 || Mathf.Abs(dx)>80) p.direction=dx<0 ? -1:1;
        if(p.direction!=0) p.facing=p.direction;
        for(float left=dt;left>0;)
        {
            float step=Mathf.Min(.008f,left);left-=step;
            float oldX=p.x,oldY=p.y;
            float force=0,drag=1,slope=0;
            if(p.ground && PlayerCtrl.Terrain!=null)
                for(int index=0;index<PlayerCtrl.Terrain.Count;index++)
                {
                    var f=PlayerCtrl.Terrain[index];
                    if(Mathf.Abs(f.z-f.x)>.001f && oldX>=Mathf.Min(f.x,f.z) && oldX<=Mathf.Max(f.x,f.z) &&
                        Mathf.Abs(Mathf.Lerp(f.y,f.w,(oldX-f.x)/(f.z-f.x))-oldY)<1.5f/Art.PPU)
                    {
                        slope=-(f.w-f.y)/(f.z-f.x);
                        if(PlayerCtrl.TerrainForces!=null && index<PlayerCtrl.TerrainForces.Count) force=PlayerCtrl.TerrainForces[index]*.01f;
                        if(PlayerCtrl.TerrainDrag!=null && index<PlayerCtrl.TerrainDrag.Count && PlayerCtrl.TerrainDrag[index]!=0) drag=PlayerCtrl.TerrainDrag[index]*.01f;
                        break;
                    }
                }
            p.vx=(float)(p.ground ? physics.Ground(p.vx*Art.PPU,p.direction,force,drag,
                owner.moveSpeed*Art.PPU/physics.WalkSpeed,slope,step) : physics.Air(p.vx*Art.PPU,-p.vy*Art.PPU,p.direction,step))/Art.PPU;
            float nextX=Mathf.Clamp(oldX+p.vx*step,PlayerCtrl.XMin,PlayerCtrl.XMax);
            bool wall=false;
            if(PlayerCtrl.Terrain!=null)
                foreach(var f in PlayerCtrl.Terrain)
                    if(Mathf.Abs(f.z-f.x)<.001f && oldY>Mathf.Min(f.y,f.w)+.001f && oldY<Mathf.Max(f.y,f.w) &&
                        (oldX<f.x && nextX>=f.x || oldX>f.x && nextX<=f.x))
                    {nextX=f.x;wall=true;break;}
            bool support=PlayerCtrl.FollowGround(oldX,oldY,nextX,out float ground);
            // Native 9C488A..9C499E / 9C4ED0: jump across foothold
            // boundaries toward an owner on a higher platform; walls also detach.
            if(p.ground && (JumpToOwnerGroup(p,owner) || p.direction!=0 && (wall || !support && target.y>oldY)))
            {p.ground=false;p.vy=owner.jumpSpeed;}
            p.x=nextX;
            if(p.ground && support) p.y=ground;
            else
            {
                p.ground=false;
                p.vy=Mathf.Max(p.vy-(float)physics.Gravity/Art.PPU*step,-(float)physics.FallSpeed/Art.PPU);
                p.y+=p.vy*step;
                if(p.vy<=0 && PlayerCtrl.FindLanding(p.x,oldY,p.y,-1,out float landing))
                {p.y=landing;p.vy=0;p.ground=true;}
            }
        }
    }
    bool JumpToOwnerGroup(Pet p,PlayerCtrl owner)
    {
        if(!owner.grounded || owner.FeetPos.y<=p.y) return false;
        // 9C488A..9C499E: different foothold groups, owner above pet,
        // and pet strictly inside the owner's group horizontal bounds.
        FieldFoothold underOwner=null,underPet=null;
        foreach(var f in map.footholds)
        {
            if(Mathf.Abs(f.x2-f.x1)<.001f) continue;
            bool At(float x,float y)=>x*Art.PPU>=Mathf.Min(f.x1,f.x2) && x*Art.PPU<=Mathf.Max(f.x1,f.x2) &&
                Mathf.Abs(-y*Art.PPU-Mathf.Lerp(f.y1,f.y2,(x*Art.PPU-f.x1)/(f.x2-f.x1)))<1.5f;
            if(At(owner.FeetPos.x,owner.FeetPos.y)) underOwner=f;
            if(At(p.x,p.y)) underPet=f;
        }
        if(underOwner==null || underPet==null || underOwner.group==underPet.group && underOwner.layer==underPet.layer) return false;
        float min=float.PositiveInfinity,max=float.NegativeInfinity;
        foreach(var f in map.footholds) if(f.group==underOwner.group && f.layer==underOwner.layer)
        {min=Mathf.Min(min,f.x1,f.x2);max=Mathf.Max(max,f.x1,f.x2);}
        return p.x*Art.PPU>min && p.x*Art.PPU<max;
    }
    void Draw(Pet p,PlayerCtrl owner,float dt)
    {
        string action=p.climbing ? "hang" : !p.ground ? "jump" : p.direction!=0 ? "move" : p.fullness<=50 ? "hungry" : "stand0";
        if(action!=p.action)
        {
            p.action=action;p.animationTime=0;p.frame=0;
            p.frames=WzAssets.Frames(p.image.Get(action));
        }
        p.animationTime+=dt;
        if(p.frames.Count>0)
        {
            double total=0;foreach(var frame in p.frames) total+=Math.Max(1,Math.Abs(frame.Int("delay",100)))/1000.0;
            double t=p.animationTime%total;
            for(int i=0;i<p.frames.Count;i++)
            {
                double duration=Math.Max(1,Math.Abs(p.frames[i].Int("delay",100)))/1000.0;
                if(t<duration) {p.frame=i;break;}t-=duration;
            }
            p.body.sprite=WzAssets.Sprite(p.frames[p.frame]);
            // Clothing shares the pet's action and frame number, not its own clock.
            var clothing=p.equipment?.Get(action+"/"+p.frames[p.frame].Name)?.Resolve();
            p.coat.sprite=clothing!=null ? WzAssets.Sprite(clothing) : null;
        }
        p.body.flipX=p.coat.flipX=p.facing>0;
        p.body.sortingOrder=owner.SortingOrder-2-p.slot*2;
        p.coat.sortingOrder=p.body.sortingOrder+1;
        p.root.transform.position=new Vector3(p.x,p.y,0);
    }
    static void Remove(GameObject go) {if(Application.isPlaying) Destroy(go);else DestroyImmediate(go);}
    public void Clear() {foreach(var p in pets.Values) Remove(p.root);pets.Clear();accumulated=0;previousHp=previousMp=-1;}
    void LateUpdate()
    {
        if(!GameSession.InGame || GameLoop.I?.field?.Map==null) Clear();
    }
    void OnDisable()=>Clear();
}
