using System;
using System.IO;

static class Program
{
    static int Main(string[] args)
    {
        try
        {
            var video = McvVideo.Parse(File.ReadAllBytes(args[0]));
            Console.WriteLine($"{video.FourCc} {video.Width} {video.Height} {video.Flags} {video.DurationNanoseconds} {video.Frames.Length}");
            foreach (var f in video.Frames)
                Console.WriteLine($"{f.colorOffset} {f.colorSize} {f.alphaOffset} {f.alphaSize} {f.startNanoseconds} {f.delayNanoseconds}");
            return 0;
        }
        catch (InvalidDataException) { return 1; }
    }
}
