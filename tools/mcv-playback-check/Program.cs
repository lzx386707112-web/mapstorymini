using System;
using System.IO;
using System.Threading;
using System.Diagnostics;

static class Program
{
    static int Main(string[] args)
    {
        var bytes=File.ReadAllBytes(args[0]);
        // Cancel while the producer is blocked on a full three-frame queue.
        for(int i=0;i<3;i++) {
            using(var cancelled=new McvPlayback(bytes)) Thread.Sleep(150);
        }
        int frames=0; bool transparent=false, colored=false;
        using(var playback=new McvPlayback(bytes)) {
            var timeout=Stopwatch.StartNew();
            while(!playback.Finished) {
                if(timeout.Elapsed.TotalSeconds>60) throw new Exception("Playback deadlock");
                var pixels=playback.TakeFrame();
                if(pixels!=null) {
                    frames++;
                    if(pixels.Length!=playback.Video.Width*playback.Video.Height*4) throw new Exception("Pixel size");
                    for(int i=0;i<pixels.Length;i+=4) {
                        transparent|=pixels[i+3]<255;
                        colored|=pixels[i]!=pixels[i+1] || pixels[i+1]!=pixels[i+2];
                    }
                }
                Thread.Sleep(1);
            }
            if(playback.Error!=null) throw playback.Error;
        }
        if(frames<2 || !transparent || !colored) throw new Exception("Missing color/alpha frames");
        // Library load/decode failure is observable, and remains disposable.
        var bad=(byte[])bytes.Clone(); var info=McvVideo.Parse(bad);
        Array.Clear(bad,(int)info.Frames[0].colorOffset,(int)info.Frames[0].colorSize);
        using(var failed=new McvPlayback(bad)) {
            var timeout=Stopwatch.StartNew();
            while(!failed.Finished && timeout.Elapsed.TotalSeconds<5) Thread.Sleep(1);
            if(failed.Error==null) throw new Exception("Corrupt VPX silently accepted");
        }
        Console.WriteLine($"MCV_PLAYBACK_PASS displayed={frames} alpha={transparent} color={colored} cancellation=3 corrupt=reported");
        return 0;
    }
}
