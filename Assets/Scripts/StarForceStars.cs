using UnityEngine;

// TMS282 star row, drawn from the original UI/Enchant.img/starForce/summaryStar canvases.
// Geometry comes from the IMG itself: icon 11x10, origin (5,5), space X1=0 X5=10 Y=5,
// anchored on starForce/vector:summary and wrapped at 15 per row (the 30 star ceiling is
// reached with the 0..29 / 1..30 diff canvases). Colours are the original pixels:
// normal #FFD800, superior blue #75E6FE, empty #1D232A at 70% alpha. Nothing is tinted
// or generated, so the enhancement window and the item tooltip share one appearance.
public static class StarForceStars
{
    const string Root="UI/Enchant.img/starForce/summaryStar/";
    public const int PerRow=15,IconWidth=11,IconHeight=10,BlockGap=10,RowGap=5;
    static Texture2D gold,superior,empty;
    static bool loaded;
    public static bool Ready{get{Load();return gold!=null;}}
    static Texture2D Load(string name)
    {
        var node=WzAssets.Node(Root+name);
        return node==null?null:WzAssets.Texture(node);
    }
    static void Load()
    {
        if(loaded)return;
        loaded=true;gold=Load("normal");superior=Load("blue");empty=Load("empty");
    }
    public static int Rows(int capacity)=>capacity<=0?0:(capacity+PerRow-1)/PerRow;
    // Width of one row: X1=0 means the 11px icon already touches its neighbour,
    // X5=10 opens a gap after every fifth star.
    public static float RowWidth(int count)=>count<=0?0:count*IconWidth+(count-1)/5*BlockGap;
    public static float BlockHeight(int rows)=>rows<=0?0:rows*IconHeight+(rows-1)*RowGap;
    /// <summary>Draws the row block centred on (centerX,centerY) in page coordinates.</summary>
    public static void Draw(float centerX,float centerY,float scale,int stars,int capacity,bool isSuperior)
    {
        Load();
        if(gold==null||capacity<=0||scale<=0)return;
        int rows=Rows(capacity);
        var filled=isSuperior&&superior!=null?superior:gold;
        var blank=empty??gold;
        float top=centerY-BlockHeight(rows)*scale*.5f;
        for(int row=0;row<rows;row++)
        {
            int count=Mathf.Min(PerRow,capacity-row*PerRow);
            float left=centerX-RowWidth(count)*scale*.5f;
            float y=top+row*(IconHeight+RowGap)*scale;
            for(int i=0;i<count;i++)
            {
                var texture=row*PerRow+i<stars?filled:blank;
                float x=left+(i*IconWidth+(i/5)*BlockGap)*scale;
                GUI.DrawTexture(new Rect(x,y,IconWidth*scale,IconHeight*scale),texture);
            }
        }
    }
}
