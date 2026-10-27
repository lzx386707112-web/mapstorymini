using System.Collections.Generic;
using UnityEngine;

// FIELD_EFFECT resolves an original 7x5 IMG marker; its pixels select the actual DLL table.
public static class NativeBossVideoEffect
{
    static readonly Dictionary<string, int> markers = new Dictionary<string, int>();
    static Dictionary<string, object> videos;
    public static int Marker(string path)
    {
        if (markers.TryGetValue(path, out int code)) return code;
        var root = WzAssets.FileRoot("Map/Effect.img");
        var frame = root.Get(path + "/0");
        if (frame == null) code = -1;
        else
        {
            var canvas = WzAssets.Canvas(frame);
            code = NativeVideoMarker.Decode(WzAssets.Pixels(frame), canvas.Width, canvas.Height);
        }
        markers[path] = code; return code;
    }
    public static bool Handle(string path)
    {
        int code = Marker(path);
        if (code == 33) { NativeSkillVideo.Stop(NativeSkillVideo.Channel.BossScene); return true; }
        if (code < 1) return false;
        if (videos == null)
        {
            var resource = Resources.Load<TextAsset>("NativeBossVideos");
            if (resource == null) return false;
            videos = (Dictionary<string, object>)MiniJson.Parse(resource.text);
        }
        return videos.TryGetValue(code.ToString(), out var relative) && NativeSkillVideo.QueueBoss((string)relative);
    }
}
