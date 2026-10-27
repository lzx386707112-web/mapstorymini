using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
class Program
{
    static void Check(bool ok,string name){if(!ok)throw new Exception(name);}
    static byte[] Packet(string path,int duration,int mode=1)
    {
        var bytes=new List<byte>{0x3F,0x37,(byte)mode};
        bytes.AddRange(BitConverter.GetBytes((ushort)path.Length));bytes.AddRange(Encoding.ASCII.GetBytes(path));bytes.AddRange(BitConverter.GetBytes(duration));return bytes.ToArray();
    }
    static void Main(string[] args)
    {
        var spear = new NativeLightningSpearState();
        Check(spear.TryPress(0) && !spear.TryPress(179) && spear.TryPress(180),"spear minimum press interval");
        for(int i=2;i<12;i++) Check(spear.TryPress(i*180),"spear press "+i);
        Check(spear.Finishing && !spear.TryPress(70000),"finishing blocks further presses until lifecycle completes");
        spear.Reset();Check(spear.TryPress(80000)&&spear.PressCount==1,"spear lifecycle reset");
        Check(spear.TryPress(140000)&&spear.PressCount==1,"spear 60s expiry");
        foreach(var path in new[]{"a.mcv","Video/a.mcv","Map/Effect.img/customSkill/a"})Check(NativeSkillBackdropPacket.ValidPath(path),"valid path");
        foreach(var path in new[]{"Video/.mcv","Video/a/b.mcv","video/a.mcv","a..mcv","Map/a.img/a b","a.MCV","/a.mcv","Video/汉.mcv"})Check(!NativeSkillBackdropPacket.ValidPath(path),"invalid path "+path);
        Check(NativeSkillBackdropPacket.Normalize(" a.mcv ")=="Video/a.mcv","server trim");
        var packet=Packet("a.mcv",-1,255);
        for(int n=0;n<packet.Length;n++){var truncated=new byte[n];Array.Copy(packet,truncated,n);Check(!NativeSkillBackdropPacket.TryRead(truncated,0,out _,out _),"truncated");}
        Check(NativeSkillBackdropPacket.TryRead(packet,0,out var path1,out int ms) && path1=="a.mcv"&&ms==0,"nonzero mode/infinite");
        var prefixed=new byte[packet.Length+7];Array.Copy(packet,0,prefixed,7,packet.Length);Check(NativeSkillBackdropPacket.TryRead(prefixed,7,out _,out _),"relative entry");
        var back=new NativeSkillBackdropState();back.Show("a.mcv",100,1000);Check(back.Remaining(1099)==1&&back.Path=="Video/a.mcv","remaining");Check(back.Remaining(1100)==0&&back.Path==null,"expiry");back.Show("a.mcv",0,0);Check(back.Remaining(long.MaxValue)==0&&back.Path!=null,"infinite");
        var weather=new NativeWeatherState();weather.Set(9999,1,99,1,0,0x123456,1,999,-1,99,1000);
        Check(weather.Minute==0&&weather.MsPerMinute==100&&weather.Sky==0&&weather.Tint==0x123456&&weather.Token==0,"normalize/legacy token");
        weather.Tick(1000);weather.Tick(1500);Check(weather.Minute==5,"clock advance");weather.Tick(61500);Check(weather.Minute==5,"ignore 60s stall");
        weather.Set(0,100,4,2,1,0,2,1,1000,9,1000);weather.Tick(62000);Check(weather.Minute==0&&weather.Age(2000)==2000&&weather.Tint==0x123456&&weather.Token==9,"frozen ages retain black tint");
        weather.Set(12,10,4,8,0,0,999,26,0,1,1000);Check(weather.Minute==720&&weather.Sky==0&&weather.Frozen&&weather.Rainbow(1000)==0,"disabled");
        Check(NativeWeatherState.Night(300)==1&&NativeWeatherState.Night(360)==.5f&&NativeWeatherState.Night(420)==0&&NativeWeatherState.Night(1080)==.5f,"day curve");
        // Minimum 8-byte weather payload with a nonzero callback entry.
        byte[] old={0,0,0,0x3D,0x37,0xD0,2,0x10,0x27,0,0,4,1};Check(weather.Read(old,3,1000)&&weather.Minute==720&&weather.Sky==4,"legacy packet offset");
        using var json=JsonDocument.Parse(System.IO.File.ReadAllText(args[0]));var rows=json.RootElement.GetProperty("regions");var regions=new NativeWeatherRegion[rows.GetArrayLength()];
        for(int i=0;i<regions.Length;i++){var row=rows[i];var weights=new List<double>();foreach(var w in row.GetProperty("weights").EnumerateArray())weights.Add(w.GetDouble());regions[i]=new NativeWeatherRegion{Name=row.GetProperty("name").GetString(),Forced=row.GetProperty("forced").GetInt32(),Tint=row.GetProperty("tint").GetInt32(),Weights=weights.ToArray()};}
        var server=new NativeWeatherService(regions,1000,()=>.5);server.Sync(211000000,true,weather,1000);Check(weather.Sky==2&&weather.Palette==0,"forced El Nath");server.Sync(800000000,true,weather,1000);Check(weather.Sky==7&&weather.Palette==2,"shrine exception");
        server.Override(1,720,1000,1000);server.Sync(100000000,true,weather,1000);Check(weather.Sky==1&&weather.Frozen,"override");server.Override(0,null,1000,1100);server.Sync(100000000,true,weather,1100);Check(weather.Rainbow(1100)==180,"rainbow transition");server.Sync(100000000,true,weather,2200);Check(!weather.Frozen,"override expires");server.Enabled=false;server.Sync(100000000,true,weather,2300);Check(weather.Disabled&&weather.Sky==0,"server disabled");
        if(args.Length>1)
        {
            using var stream=System.IO.File.OpenRead(args[1]);using var reader=new System.IO.BinaryReader(stream);int vectors=0;
            while(stream.Position<stream.Length)
            {
                int palette=reader.ReadInt32();uint age=reader.ReadUInt32(),seed=reader.ReadUInt32();float expected=reader.ReadSingle();
                float actual=NativeWeatherLightning.Evaluate(age,seed,palette);Check(Math.Abs(actual-expected)<.000001f,"lightning oracle");vectors++;
            }
            Console.WriteLine("LIGHTNING_ORACLE_PASS vectors="+vectors);
        }
        Console.WriteLine("DLL_STATE_PASS backdrop validation/truncation/expiry; weather legacy/clock/regions/override/rainbow/disabled");
    }
}
