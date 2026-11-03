using System;
using System.Collections.Generic;
using MapleDash.Wz;
using UnityEngine;

// CUser::UpdateRing (92E964) and CUser name layers (5F0334).
public static class NativeNameTag
{
    public sealed class Layout
    {
        public Texture2D texture;
        public int width, height, textX, textY;
        public Color color;
    }
    static readonly Dictionary<string, Layout> cache = new Dictionary<string, Layout>();
    static readonly int[] ringSlots = {12,13,15,16};
    public static int RingStyle(string field)
    {
        int selected = 0;
        // EXE BD8590 = {12,13,15,16}; each nonzero property replaces the previous one.
        foreach (int slot in ringSlots)
        {
            int id = Profile.EquipOfSlot(-slot-100);
            if (id == 0) id = Profile.EquipOfSlot(-slot);
            int style = EquipCatalog.Info(id)?.Int(field) ?? 0;
            if (style != 0) selected = style;
        }
        return selected;
    }

    /// <summary>
    /// 四枚戒指里是否有任意一枚在该字段上有值。源端 isNameTagRing 只要求 info/nameTag
    /// <b>节点存在</b>（值可以是 0），名牌附加行的显示条件就走这一条。
    /// </summary>
    public static bool WearsField(string field)
    {
        foreach (int slot in ringSlots)
        {
            int id = Profile.EquipOfSlot(-slot-100);
            if (id == 0) id = Profile.EquipOfSlot(-slot);
            if (id != 0 && EquipCatalog.Info(id)?.Get(field) != null) return true;
        }
        return false;
    }
    public static Layout Prepare(string path, int textWidth)
    {
        string key = path+":"+textWidth;
        if (cache.TryGetValue(key, out var hit)) return hit;
        var root = WzAssets.Node(path);
        var w = root?.Get("w")?.TryResolve();
        var c = root?.Get("c")?.TryResolve();
        var e = root?.Get("e")?.TryResolve();
        if (w?.Kind != "Canvas" || c?.Kind != "Canvas" || e?.Kind != "Canvas" || c.Width <= 0)
        { cache[key] = null; return null; }
        int OriginX(ImgNode n) => n.Get("origin")?.X ?? 0;
        int OriginY(ImgNode n) => n.Get("origin")?.Y ?? 0;
        int middle = (Math.Max(0,textWidth)+c.Width-1)/c.Width*c.Width;
        int baseline = Math.Max(OriginY(w),Math.Max(OriginY(c),OriginY(e)));
        int width = OriginX(w)+middle+e.Width-OriginX(e);
        int height = baseline+Math.Max(w.Height-OriginY(w),Math.Max(c.Height-OriginY(c),e.Height-OriginY(e)));
        if (width <= 0 || height <= 0) return null;
        var pixels = new Color32[width*height];
        void Copy(ImgNode node,int x,int y)
        {
            var texture = WzAssets.Texture(node);
            var source = texture.GetPixels32();
            for (int sy=0;sy<node.Height;sy++)
                for (int sx=0;sx<node.Width;sx++)
                {
                    int dx=x+sx,dy=y+sy;
                    if(dx<0 || dx>=width || dy<0 || dy>=height) continue;
                    var value=source[(node.Height-1-sy)*node.Width+sx];
                    if(value.a==0) continue;
                    int at=(height-1-dy)*width+dx;
                    var under=pixels[at];
                    float alpha=value.a/255f, old=under.a/255f, result=alpha+old*(1-alpha);
                    pixels[at]=new Color32((byte)((value.r*alpha+under.r*old*(1-alpha))/result),
                        (byte)((value.g*alpha+under.g*old*(1-alpha))/result),
                        (byte)((value.b*alpha+under.b*old*(1-alpha))/result),(byte)(result*255));
                }
        }
        for (int x=0;x<middle;x+=c.Width) Copy(c,OriginX(w)+x,baseline-OriginY(c));
        Copy(w,0,baseline-OriginY(w));
        Copy(e,width-e.Width,0);
        var output=new Texture2D(width,height,TextureFormat.RGBA32,false) {name="NameTag_"+key,filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp};
        output.SetPixels32(pixels);output.Apply();
        uint color=unchecked((uint)root.Int("clr",-1));
        var layout=new Layout {texture=output,width=width,height=height,
            textX=OriginX(w)+(middle-textWidth)/2,textY=baseline-2,
            color=new Color32((byte)(color>>16),(byte)(color>>8),(byte)color,(byte)(color>>24))};
        cache[key]=layout;return layout;
    }
    public static bool Draw(string path,string text,Vector2 anchor,GUIStyle style,out Rect bounds)
    {
        int textWidth=Mathf.CeilToInt(style.CalcSize(new GUIContent(text)).x);
        var layout=Prepare(path,textWidth);
        bounds=default;
        if(layout==null) return false;
        bounds=new Rect(anchor.x-layout.width/2,anchor.y,layout.width,layout.height);
        GUI.DrawTexture(bounds,layout.texture);
        var previous=style.normal.textColor;
        style.normal.textColor=layout.color;
        GUI.Label(new Rect(bounds.x+layout.textX,bounds.y+layout.textY,textWidth+1,layout.height),text,style);
        style.normal.textColor=previous;
        return true;
    }
}
