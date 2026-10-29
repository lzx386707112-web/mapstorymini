using System;

// BeiDou.exe 4FB08F / 500971 / 94BA6A: clock in milliseconds, no catch-up burst.
public static class NativeHeldSkill
{
    public static bool Continuous(int id) => id==3121004 || id==5221004 || id==13111002;
    public static bool KeyDown(int id)
    {
        switch(id)
        {
            case 0x111ae9: case 0x12a189: case 0x142829: case 0x205d29:
            case 0x21e3c9: case 0x236a69: case 0x2f9f6c: case 0x312609:
            case 0x4dd5cc: case 0x4f5c6a: case 0x4faa8c: case 0xc80eda:
            case 0xd7511e: case 0xe66c4b: case 0x1518a28: case 0x151ff59: return true;
            default:return false;
        }
    }
    public static int ReleaseElapsed(int id,long elapsed) => KeyDown(id) ? (int)Math.Min(Continuous(id)?2000:1000,Math.Max(30,elapsed)) : 0;
    public static bool ShotDue(long now,long previous) => now-previous>100;
}
