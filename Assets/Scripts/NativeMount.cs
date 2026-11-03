using UnityEngine;

// StatEffect.isMonsterRiding/applyBuffEffect: ordinary equipment mounts from 9900009.
public static class NativeMount
{
    public static bool IsSkill(int id) => id==1004 || id==10001004 || id==20001004;
    // CUIEquip::Draw (7FEEC2): cash equipment covers the normal slot in the same cell.
    public static int VisibleSlot(int slot)
        => (slot==-18 || slot==-19) && Profile.EquipOfSlot(slot-100)>0 ? slot-100 : slot;
    public static int Saddle => Profile.EquipOfSlot(VisibleSlot(-19));
    public static int Item(PlayerCtrl player)
        => IsSkill(player.GetComponent<NativeBuffs>()?.Value("MONSTER_RIDING") ?? 0) ? Profile.EquipOfSlot(VisibleSlot(-18)) : 0;
    static bool Fits(int mount,int saddle) => NativeItemSource.Node(saddle)?.Get(mount.ToString())!=null;
    // Original client 4F1E58 / 4F292D: cash mount/saddle require their normal base.
    public static string WearError(int item,int slot)
    {
        if(slot!=-18 && slot!=-19 && slot!=-118 && slot!=-119) return null;
        bool cash=slot<-100;
        int normal=cash ? slot+100 : slot;
        if(cash && Profile.EquipOfSlot(normal)==0)
            return normal==-18 ? "请先装备普通骑兽，再装备现金骑兽" : "请先装备普通鞍具，再装备现金鞍具";
        int other=Profile.EquipOfSlot(normal==-18 ? (cash ? -119 : -19) : (cash ? -118 : -18));
        if(other>0 && !(normal==-18 ? Fits(item,other) : Fits(other,item)))
            return cash ? "现金骑兽与现金鞍具不匹配" : "鞍具与坐骑不匹配";
        return null;
    }
    public static string Toggle(PlayerCtrl player,int skill,int level)
    {
        var buffs=NativeBuffs.For(player);
        if(buffs.HasSource(skill)) { buffs.Cancel(skill); player.RefreshFieldAppearance(); return null; }
        if(player.hp<=0 || player.IsClimbing || player.Sitting) return "当前状态无法骑乘";
        int mount=Profile.EquipOfSlot(-18),saddle=Profile.EquipOfSlot(-19);
        if(mount/10000!=190 || saddle/10000!=191) return "请先装备普通骑兽和普通鞍具（现金骑宠不能代替）";
        int cashMount=Profile.EquipOfSlot(-118),cashSaddle=Profile.EquipOfSlot(-119);
        if(cashMount>0 || cashSaddle>0)
        {
            if(cashMount/10000!=190 || cashSaddle/10000!=191 || !Fits(cashMount,cashSaddle))
                return "现金骑兽需要装备配套的现金鞍具";
        }
        else if(!Fits(mount,saddle)) return "鞍具与坐骑不匹配";
        if(!buffs.ApplySkill(skill,level,NativeBuffs.Now)) return "骑兽技能效果数据缺失";
        player.RefreshFieldAppearance();
        return null;
    }
}
