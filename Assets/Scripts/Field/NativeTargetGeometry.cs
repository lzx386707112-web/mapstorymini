using System;
using System.Collections.Generic;
using MapleDash.Wz;
using UnityEngine;

// Source LifeFactory.buildMonsterBoundingBox and AbstractDealDamageHandler.getWorldBbox.
// This is the server attack-box geometry, not the shrunken contact-damage rectangle.
public static class NativeTargetGeometry
{
    static readonly Dictionary<int,Rect?> boxes=new Dictionary<int,Rect?>();
    public static Rect? Build(ImgNode root)
    {
        Rect? result=null;
        if (root==null) return null;
        foreach (var action in root.Children)
        {
            string name=action.Name;
            if (!(name.StartsWith("stand",StringComparison.Ordinal) || name.StartsWith("move",StringComparison.Ordinal) ||
                name.StartsWith("fly",StringComparison.Ordinal) || name.StartsWith("jump",StringComparison.Ordinal) ||
                name.StartsWith("attack",StringComparison.Ordinal) || name.StartsWith("hit",StringComparison.Ordinal))) continue;
            var resolved=action.TryResolve();
            if (resolved==null) continue;
            foreach (var child in resolved.Children)
            {
                var frame=child.TryResolve();
                if (frame==null || frame.Kind!="Canvas") continue;
                var lt=frame.Get("lt");var rb=frame.Get("rb");
                int left,top,right,bottom;
                if (lt!=null && rb!=null) { left=lt.X;top=lt.Y;right=rb.X;bottom=rb.Y; }
                else
                {
                    var origin=frame.Get("origin");
                    if (origin==null || frame.Width<=0 || frame.Height<=0) continue;
                    left=-origin.X;top=-origin.Y;right=left+frame.Width;bottom=top+frame.Height;
                }
                var box=Rect.MinMaxRect(left,top,right,bottom);
                result=result.HasValue ? Rect.MinMaxRect(Math.Min(result.Value.xMin,box.xMin),Math.Min(result.Value.yMin,box.yMin),
                    Math.Max(result.Value.xMax,box.xMax),Math.Max(result.Value.yMax,box.yMax)) : box;
            }
        }
        return result;
    }
    public static Rect World(Rect pixels,Vector2 position,bool mirror)
    {
        float left=mirror ? -pixels.xMax : pixels.xMin;
        float right=mirror ? -pixels.xMin : pixels.xMax;
        float minimumX=Math.Min(left,right),minimumY=Math.Min(pixels.yMin,pixels.yMax);
        float maximumX=minimumX+Math.Max(1,Math.Abs(right-left));
        float maximumY=minimumY+Math.Max(1,Math.Abs(pixels.yMax-pixels.yMin));
        return Rect.MinMaxRect(position.x+minimumX/Art.PPU,position.y-maximumY/Art.PPU,
            position.x+maximumX/Art.PPU,position.y-minimumY/Art.PPU);
    }
    // LifeFactory.resolveMonsterVisualData: usable own visuals win over info/link.
    // A primary Canvas can be usable even when it carries no bbox metadata.
    static ImgNode VisualRoot(int id)
    {
        var original=WzAssets.FileRoot("Mob/"+id.ToString("D7")+".img");
        var current=original;
        var visited=new HashSet<int>();
        while (current!=null && visited.Add(id))
        {
            var fly=current.Get("fly/0")?.TryResolve();
            var stand=current.Get("stand/0")?.TryResolve();
            if (fly?.Kind=="Canvas" || stand?.Kind=="Canvas" || Build(current).HasValue) return current;
            int link=current.Int("info/link");
            if (link==0) break;
            id=link;
            current=WzAssets.FileRoot("Mob/"+id.ToString("D7")+".img");
        }
        return original;
    }
    public static Rect? Bounds(EnemyUnit mob,bool mirror)
    {
        if (!boxes.TryGetValue(mob.fieldMobId,out var local))
            boxes[mob.fieldMobId]=local=Build(VisualRoot(mob.fieldMobId));
        if (!local.HasValue) return null;
        bool fixedStance=WzAssets.FileRoot("Mob/"+mob.fieldMobId.ToString("D7")+".img").Int("info/noFlip")>0;
        return World(local.Value,mob.transform.position,mirror && !fixedStance);
    }
}
