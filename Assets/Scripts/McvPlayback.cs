using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

// No Unity API on the decoder thread. One instance owns one original video channel.
public sealed class McvPlayback : IDisposable
{
    [DllImport("mapledash_mcv", CallingConvention = CallingConvention.Cdecl)]
    static extern IntPtr mcv_create(uint codec, uint width, uint height, int alpha);
    [DllImport("mapledash_mcv", CallingConvention = CallingConvention.Cdecl)]
    static extern void mcv_destroy(IntPtr decoder);
    [DllImport("mapledash_mcv", CallingConvention = CallingConvention.Cdecl)]
    static extern int mcv_decode(IntPtr decoder, [In] byte[] bytes, uint length,
        uint colorOffset, uint colorSize, uint alphaOffset, uint alphaSize, [Out] byte[] output, uint capacity);

    sealed class Slot { public byte[] pixels; public ulong start; }
    readonly object gate = new object();
    readonly Slot[] slots = new Slot[3];
    readonly Thread worker;
    readonly byte[] data;
    readonly Stopwatch clock = new Stopwatch();
    bool stopped, complete;
    Exception error;
    public McvVideo Video { get; }
    public Exception Error { get { lock (gate) return error; } }
    public bool Finished { get { lock (gate) return complete && (error != null || Elapsed >= Video.DurationNanoseconds); } }
    ulong Elapsed => (ulong)(clock.Elapsed.TotalSeconds * 1000000000.0);

    public McvPlayback(byte[] bytes)
    {
        Video = McvVideo.Parse(bytes); data = bytes;
        worker = new Thread(Decode) { IsBackground = true, Name = "MCV decoder" };
        worker.Start();
    }

    void Decode()
    {
        IntPtr decoder = IntPtr.Zero;
        try
        {
            decoder = mcv_create(Video.FourCc, Video.Width, Video.Height, Video.Flags & 1);
            if (decoder == IntPtr.Zero) throw new InvalidDataException("MCV decoder initialization failed");
            foreach (var frame in Video.Frames)
            {
                lock (gate) if (stopped) break;
                var pixels = new byte[checked(Video.Width * Video.Height * 4)];
                if (mcv_decode(decoder, data, (uint)data.Length, frame.colorOffset, frame.colorSize,
                    frame.alphaOffset, frame.alphaSize, pixels, (uint)pixels.Length) == 0)
                    throw new InvalidDataException("MCV VPX frame decode failed");
                lock (gate)
                {
                    int free;
                    while ((free = Array.FindIndex(slots, s => s == null)) < 0 && !stopped) Monitor.Wait(gate);
                    if (stopped) break;
                    slots[free] = new Slot { pixels = pixels, start = frame.startNanoseconds };
                    if (!clock.IsRunning) clock.Start();
                }
            }
        }
        catch (Exception e) { lock (gate) error = e; }
        finally
        {
            if (decoder != IntPtr.Zero) mcv_destroy(decoder);
            lock (gate) { complete = true; Monitor.PulseAll(gate); }
        }
    }

    // Latest due frame wins; future frames remain queued. Same tie/drop rule as Render().
    public byte[] TakeFrame()
    {
        lock (gate)
        {
            if (stopped || (complete && Elapsed >= Video.DurationNanoseconds)) return null;
            ulong now = Elapsed; int selected = -1;
            for (int i = 0; i < slots.Length; i++)
                if (slots[i] != null && slots[i].start <= now &&
                    (selected < 0 || slots[i].start >= slots[selected].start)) selected = i;
            if (selected < 0) return null;
            var frame = slots[selected];
            for (int i = 0; i < slots.Length; i++)
                if (slots[i] != null && slots[i].start < frame.start) slots[i] = null;
            slots[selected] = null; Monitor.PulseAll(gate); return frame.pixels;
        }
    }

    public void Dispose()
    {
        lock (gate) { stopped = true; Monitor.PulseAll(gate); }
        worker.Join();
        lock (gate) { Array.Clear(slots, 0, slots.Length); clock.Stop(); }
    }
}
