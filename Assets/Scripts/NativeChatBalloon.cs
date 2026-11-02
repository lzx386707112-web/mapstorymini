using System;
using System.Collections.Generic;
using MapleDash.Wz;
using UnityEngine;

// 4890C4: place the nine canvases around the text rectangle using each IMG origin.
public static class NativeChatBalloon
{
    public sealed class Layout { public Texture2D texture; public Rect text; public Color color; public int anchorX, lineHeight; }
    static readonly Dictionary<string,Layout> cache=new Dictionary<string,Layout>();
    static readonly string[] names={"c","n","s","w","e","nw","ne","sw","se","arrow","head"};
    public static Layout Prepare(int style,int innerWidth,int lines)
    {
        string key=style+":"+innerWidth+":"+lines;
        if(cache.TryGetValue(key,out var found))return found;
        var root=WzAssets.Node("UI/ChatBalloon.img/"+style);
        if(root==null){cache[key]=null;return null;}
        var nodes=new Dictionary<string,ImgNode>();
        foreach(string name in names)
        {
            var node=root.Get(name)?.TryResolve();
            if(node?.Kind!="Canvas")
            {
                if(name=="arrow" || name=="head")continue;
                cache[key]=null;return null;
            }
            nodes[name]=node;
        }
        int X(string name)=>nodes[name].Get("origin")?.X ?? 0;
        int Y(string name)=>nodes[name].Get("origin")?.Y ?? 0;
        int left=Math.Max(X("nw"),Math.Max(X("w"),X("sw")));
        int right=Math.Max(nodes["ne"].Width-X("ne"),Math.Max(nodes["e"].Width-X("e"),nodes["se"].Width-X("se")));
        int top=Math.Max(Y("nw"),Math.Max(Y("n"),Y("ne")));
        int bottom=Math.Max(nodes["sw"].Height-Y("sw"),Math.Max(nodes["s"].Height-Y("s"),nodes["se"].Height-Y("se")));
        if(nodes.ContainsKey("arrow"))bottom=Math.Max(bottom,nodes["arrow"].Height-Y("arrow"));
        // 489D0E..3E rounds a single line to an odd number of north tiles.
        // Multi-line balloons use the original seven-column width (48993D..55).
        int columns=lines>1?7:((Math.Max(1,innerWidth)+nodes["n"].Width-1)/nodes["n"].Width)|1;
        int minimumHeight=Math.Max(nodes["ne"].Height-Y("ne")+Y("se"),nodes["nw"].Height-Y("nw")+Y("sw"));
        innerWidth=columns*nodes["n"].Width;
        int innerHeight=Math.Max(lines,(minimumHeight+nodes["w"].Height-1)/nodes["w"].Height)*nodes["w"].Height;
        int width=left+innerWidth+right,height=top+innerHeight+bottom;
        if(width<=0 || height<=0)return null;
        var pixels=new Color32[width*height];
        void Copy(string name,int x,int y,int limitWidth=-1,int limitHeight=-1)
        {
            var node=nodes[name];var source=WzAssets.Texture(node).GetPixels32();
            x-=X(name);y-=Y(name);
            int copyWidth=limitWidth<0?node.Width:Math.Min(node.Width,limitWidth);
            int copyHeight=limitHeight<0?node.Height:Math.Min(node.Height,limitHeight);
            for(int sy=0;sy<copyHeight;sy++)for(int sx=0;sx<copyWidth;sx++)
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
            int x=left+column*nodes["n"].Width;
            Copy(column==columns/2 && nodes.ContainsKey("head")?"head":"n",x,top);
            Copy(column==columns/2 && nodes.ContainsKey("arrow")?"arrow":"s",x,top+innerHeight);
        }
        for(int y=0;y<innerHeight;y+=nodes["w"].Height)
        {
            Copy("w",left,top+y);
            for(int column=0;column<columns;column++)Copy("c",left+column*nodes["n"].Width,top+y);
            Copy("e",left+innerWidth,top+y);
        }
        Copy("nw",left,top);Copy("ne",left+innerWidth,top);
        Copy("sw",left,top+innerHeight);Copy("se",left+innerWidth,top+innerHeight);
        var output=new Texture2D(width,height,TextureFormat.RGBA32,false) {name="ChatBalloon_"+key,filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp};
        output.SetPixels32(pixels);output.Apply();
        uint color=unchecked((uint)root.Int("clr",unchecked((int)0xff000000)));
        var layout=new Layout {texture=output,text=new Rect(left,top-1,innerWidth,innerHeight),anchorX=left+innerWidth/2-3,lineHeight=nodes["w"].Height,
            color=new Color32((byte)(color>>16),(byte)(color>>8),(byte)color,(byte)(color>>24))};
        if(cache.Count>=64){foreach(var item in cache.Values)if(item!=null)UnityEngine.Object.Destroy(item.texture);cache.Clear();}
        cache[key]=layout;return layout;
    }
    public static int TextWidth(int style) => 7*(WzAssets.Node("UI/ChatBalloon.img/"+style+"/n")?.TryResolve()?.Width ?? 12);
    public static int FontSize(int style) => Math.Max(1,(WzAssets.Node("UI/ChatBalloon.img/"+style+"/w")?.TryResolve()?.Height ?? 14)-2);
}
