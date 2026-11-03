using System;
using System.Collections.Generic;
using MapleDash.Wz;
using UnityEngine;

// 4890C4: place the nine canvases around the text rectangle using each IMG origin.
public static class NativeChatBalloon
{
    public sealed class Layout
    {
        public Texture2D texture;      // 第 0 帧（静态样式只有这一张）
        public Texture2D[] frames;     // 动画样式（如 21）的全部帧
        public int delay;              // 每帧毫秒，0 = 静态
        public Rect text; public Color color; public int anchorX, lineHeight;
        public Texture2D Frame(double now)
        {
            if (frames == null || frames.Length <= 1 || delay <= 0) return texture;
            int index = (int)(now * 1000.0 / delay) % frames.Length;
            return frames[index] != null ? frames[index] : texture;
        }
    }
    static readonly Dictionary<string,Layout> cache=new Dictionary<string,Layout>();
    static readonly HashSet<string> diagnosed=new HashSet<string>();
    static readonly string[] names={"c","n","s","w","e","nw","ne","sw","se","arrow","head"};
    public static Layout Prepare(int style,int innerWidth,int lines) => Prepare(style.ToString(),innerWidth,lines);
    // 字符串路径版：宠物气泡等子树样式（UI/ChatBalloon.img/pet/0）
    public static Layout Prepare(string stylePath,int innerWidth,int lines)
    {
        string key=stylePath+":"+innerWidth+":"+lines;
        if(cache.TryGetValue(key,out var found))return found;
        var root=WzAssets.Node("UI/ChatBalloon.img/"+stylePath);
        var frames=FrameRoots(root);
        if(frames.Count==0){ Diagnose(stylePath,"样式节点不存在或没有九宫格子节点"); cache[key]=null; return null; }
        var first=Collect(frames[0]);
        if(first==null){ Diagnose(stylePath,"九宫格子节点不全（c/n/s/w/e/nw/ne/sw/se 必须是 Canvas）"); cache[key]=null; return null; }
        int left=Math.Max(first.X("nw"),Math.Max(first.X("w"),first.X("sw")));
        int right=Math.Max(first.Node["ne"].Width-first.X("ne"),Math.Max(first.Node["e"].Width-first.X("e"),first.Node["se"].Width-first.X("se")));
        int top=Math.Max(first.Y("nw"),Math.Max(first.Y("n"),first.Y("ne")));
        int bottom=Math.Max(first.Node["sw"].Height-first.Y("sw"),Math.Max(first.Node["s"].Height-first.Y("s"),first.Node["se"].Height-first.Y("se")));
        if(first.Node.ContainsKey("arrow"))bottom=Math.Max(bottom,first.Node["arrow"].Height-first.Y("arrow"));
        // 489D0E..3E rounds a single line to an odd number of north tiles.
        // Multi-line balloons use the original seven-column width (48993D..55).
        int columns=lines>1?7:((Math.Max(1,innerWidth)+first.Node["n"].Width-1)/first.Node["n"].Width)|1;
        int minimumHeight=Math.Max(first.Node["ne"].Height-first.Y("ne")+first.Y("se"),first.Node["nw"].Height-first.Y("nw")+first.Y("sw"));
        innerWidth=columns*first.Node["n"].Width;
        int innerHeight=Math.Max(lines,(minimumHeight+first.Node["w"].Height-1)/first.Node["w"].Height)*first.Node["w"].Height;
        int width=left+innerWidth+right,height=top+innerHeight+bottom;
        if(width<=0 || height<=0){ Diagnose(stylePath,"合成尺寸非法 "+width+"x"+height); cache[key]=null; return null; }
        // 每一帧都按第 0 帧的几何拼：动画样式的框体尺寸必须恒定，否则气泡会随帧抽动。
        var built=new List<Texture2D>(frames.Count);
        for(int index=0;index<frames.Count;index++)
        {
            var nodes=index==0?first:Collect(frames[index]);
            if(nodes==null)continue;
            var pixels=Compose(nodes,left,right,top,bottom,columns,innerWidth,innerHeight,width,height);
            built.Add(ToTexture(pixels,width,height,"ChatBalloon_"+key+"#"+built.Count));
        }
        if(built.Count==0){ cache[key]=null; return null; }
        uint color=unchecked((uint)root.Int("clr",unchecked((int)0xff000000)));
        int delay=frames[0].Int("delay",0);
        var layout=new Layout {texture=built[0],frames=built.ToArray(),delay=built.Count>1?Math.Max(30,delay):0,
            text=new Rect(left,top-1,innerWidth,innerHeight),anchorX=left+innerWidth/2-3,lineHeight=first.Node["w"].Height,
            color=new Color32((byte)(color>>16),(byte)(color>>8),(byte)color,(byte)(color>>24))};
        if(cache.Count>=48){foreach(var item in cache.Values)Release(item);cache.Clear();}
        cache[key]=layout;return layout;
    }

    /// <summary>样式根下真正存放九宫格的节点。动画样式（21）的九宫格在 0/1/2… 帧子节点里。</summary>
    static List<ImgNode> FrameRoots(ImgNode root)
    {
        var list=new List<ImgNode>();
        if(root==null)return list;
        if(root.Get("nw")!=null){list.Add(root);return list;}
        for(int frame=0;frame<64;frame++)
        {
            var node=root.Get(frame.ToString())?.TryResolve();
            if(node==null)break;
            list.Add(node);
        }
        return list;
    }

    sealed class Pieces
    {
        public readonly Dictionary<string,ImgNode> Node=new Dictionary<string,ImgNode>();
        public int X(string name)=>Node[name].Get("origin")?.X ?? 0;
        public int Y(string name)=>Node[name].Get("origin")?.Y ?? 0;
    }

    static Pieces Collect(ImgNode container)
    {
        var pieces=new Pieces();
        foreach(string name in names)
        {
            var node=container?.Get(name)?.TryResolve();
            if(node?.Kind!="Canvas")
            {
                if(name=="arrow" || name=="head")continue;
                return null;
            }
            pieces.Node[name]=node;
        }
        return pieces;
    }

    static Color32[] Compose(Pieces nodes,int left,int right,int top,int bottom,int columns,int innerWidth,int innerHeight,int width,int height)
    {
        var pixels=new Color32[width*height];
        void Copy(string name,int x,int y)
        {
            var node=nodes.Node[name];var source=WzAssets.Texture(node).GetPixels32();
            x-=nodes.X(name);y-=nodes.Y(name);
            for(int sy=0;sy<node.Height;sy++)for(int sx=0;sx<node.Width;sx++)
            {
                int dx=x+sx,dy=y+sy;if(dx<0 || dx>=width || dy<0 || dy>=height)continue;
                var value=source[(node.Height-1-sy)*node.Width+sx];if(value.a==0)continue;
                int at=(height-1-dy)*width+dx;var under=pixels[at];
                float a=value.a/255f,b=under.a/255f,result=a+b*(1-a);
                pixels[at]=new Color32((byte)((value.r*a+under.r*b*(1-a))/result),
                    (byte)((value.g*a+under.g*b*(1-a))/result),(byte)((value.b*a+under.b*b*(1-a))/result),(byte)(result*255));
            }
        }
        // Original copy order: north/south, then west/centre/east rows, then corners.
        for(int column=0;column<columns;column++)
        {
            int x=left+column*nodes.Node["n"].Width;
            Copy(column==columns/2 && nodes.Node.ContainsKey("head")?"head":"n",x,top);
            Copy(column==columns/2 && nodes.Node.ContainsKey("arrow")?"arrow":"s",x,top+innerHeight);
        }
        for(int y=0;y<innerHeight;y+=nodes.Node["w"].Height)
        {
            Copy("w",left,top+y);
            for(int column=0;column<columns;column++)Copy("c",left+column*nodes.Node["n"].Width,top+y);
            Copy("e",left+innerWidth,top+y);
        }
        Copy("nw",left,top);Copy("ne",left+innerWidth,top);
        Copy("sw",left,top+innerHeight);Copy("se",left+innerWidth,top+innerHeight);
        return pixels;
    }

    static Texture2D ToTexture(Color32[] pixels,int width,int height,string name)
    {
        var texture=new Texture2D(width,height,TextureFormat.RGBA32,false) {name=name,filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp};
        texture.SetPixels32(pixels);texture.Apply();
        return texture;
    }

    static void Release(Layout layout)
    {
        if(layout?.frames==null)return;
        foreach(var frame in layout.frames)if(frame!=null)UnityEngine.Object.Destroy(frame);
    }

    // 样式在素材里缺节点时只提示一次：以前这里是永久 null 缓存且完全无声，
    // 于是「戴了气泡戒指还是默认气泡」在真机上完全查不出原因。
    static void Diagnose(string stylePath,string reason)
    {
        if(!diagnosed.Add(stylePath))return;
        Debug.LogWarning("[ChatBalloon] 样式 "+stylePath+" 无法合成（"+reason+"），回退默认气泡");
    }

    public static int TextWidth(int style) => 7*(PiecesOf(style)?.Node["n"].Width ?? 12);
    public static int FontSize(int style) => Math.Max(1,(PiecesOf(style)?.Node["w"].Height ?? 14)-2);
    static Pieces PiecesOf(int style)
    {
        var frames=FrameRoots(WzAssets.Node("UI/ChatBalloon.img/"+style));
        return frames.Count==0?null:Collect(frames[0]);
    }
}
