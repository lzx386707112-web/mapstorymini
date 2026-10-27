using System;
using System.Collections.Generic;
using System.Text;

// SkillBackdropPackets.java (server normalization) and skillback.cpp (wire validation).
public static class NativeSkillBackdropPacket
{
    public const int MaxDuration = 86400000;
    public static bool ValidPath(string path)
    {
        if (string.IsNullOrEmpty(path) || path.Length > 192 || path.Contains("..")) return false;
        foreach (char c in path)
            if (!((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') ||
                c == '_' || c == '.' || c == '/' || c == '-')) return false;
        if (path.EndsWith(".mcv",StringComparison.Ordinal))
        {
            int slash = path.LastIndexOf('/');
            return slash < 0 ? path.Length > 4 : path.StartsWith("Video/",StringComparison.Ordinal) && slash == 5 && path.Length > 10;
        }
        return path.Contains(".img/") && path.Contains("/");
    }
    public static string Normalize(string path)
    {
        path = path?.Trim();
        if (!ValidPath(path)) return null;
        return path.EndsWith(".mcv",StringComparison.Ordinal) && !path.Contains("/") ? "Video/" + path : path;
    }
    // Offset points at the opcode, as in the native registered packet callback.
    // Invalid/truncated input never clears the previous request.
    public static bool TryRead(byte[] packet, int offset, out string path, out int duration)
    {
        path = null; duration = 0;
        if (packet == null || offset < 0 || offset > packet.Length-3) return false;
        int p = offset+2;
        if (packet[p++] == 0) return true;
        if (packet.Length-p < 2) return false;
        int length = packet[p] | packet[p+1] << 8; p += 2;
        if (length > 192 || packet.Length-p < length+4) return false;
        path = Encoding.ASCII.GetString(packet,p,length); p += length;
        if (!ValidPath(path)) { path = null; return false; }
        duration = Math.Max(0,packet[p] | packet[p+1]<<8 | packet[p+2]<<16 | packet[p+3]<<24);
        return true;
    }
}

public sealed class NativeSkillBackdropState
{
    public string Path { get; private set; }
    public int Revision { get; private set; }
    double expires;
    public void Show(string path, int duration, double now)
    {
        Path = NativeSkillBackdropPacket.Normalize(path);
        duration = Math.Max(0,Math.Min(NativeSkillBackdropPacket.MaxDuration,duration));
        expires = Path != null && duration > 0 ? now + duration : 0;
        Revision++;
    }
    public int Remaining(double now)
    {
        if (Path != null && expires > 0 && now >= expires) Show(null,0,now);
        return Path == null || expires == 0 ? 0 : (int)Math.Min(int.MaxValue,Math.Ceiling(expires-now));
    }
}

// MapleMap stores this independently of each visiting character. No DB persistence.
public sealed class NativeSkillBackdropService
{
    readonly Dictionary<int,NativeSkillBackdropState> maps = new Dictionary<int,NativeSkillBackdropState>();
    public NativeSkillBackdropState Map(int id)
    {
        if (!maps.TryGetValue(id,out var state)) maps[id] = state = new NativeSkillBackdropState();
        return state;
    }
}
