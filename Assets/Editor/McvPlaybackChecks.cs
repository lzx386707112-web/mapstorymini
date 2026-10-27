#if UNITY_EDITOR
using System;
using System.IO;
using System.Threading;
using UnityEngine;

public static class McvPlaybackChecks
{
    public static void Run()
    {
        var bytes = File.ReadAllBytes(Path.Combine(WzAssets.DataRoot, "Video/galaxy-star-burst.mcv"));
        using (var playback = new McvPlayback(bytes))
        {
            byte[] pixels = null;
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (pixels == null && DateTime.UtcNow < deadline && playback.Error == null)
            { pixels = playback.TakeFrame(); Thread.Sleep(2); }
            if (playback.Error != null) throw playback.Error;
            if (pixels == null) throw new Exception("Unity VPX plugin did not decode");
        }
        string[] names = { "dragonP1", "dragonP2", "laserRain", "phantomBarrage", "rush", "fury", "butterflyBurst", "bomb", "stainedGlass", "stainedGlass1", "stainedGlass2", "stainedGlass3", "stainedGlass4", "stainedGlass5", "flowerExplosion", "flowerExplosion1", "flowerExplosion2", "flowerExplosion3", "furyStop" };
        for (int i = 0; i < names.Length; i++)
            if (NativeBossVideoEffect.Marker("customSkill/lucid/" + names[i] + "VideoLayer") != 15+i)
                throw new Exception("Original Lucid IMG marker mismatch: " + names[i]);
        if (!NativeSkillVideo.Play(NativeSkillVideo.Channel.BossScene, "Video/galaxy-star-burst.mcv")) throw new Exception("Boss channel failed");
        if (!NativeSkillVideo.PlaySkill(11121005, null)) throw new Exception("Skill mapping failed");
        if (UnityEngine.Object.FindObjectsOfType<NativeSkillVideo>().Length != 2) throw new Exception("Channels failed to coexist");
        if (!NativeSkillVideo.PlaySkill(11121005, null)) throw new Exception("Replacement failed");
        if (UnityEngine.Object.FindObjectsOfType<NativeSkillVideo>().Length != 2) throw new Exception("Replacement left an active previous channel");
        if (!NativeBossVideoEffect.Handle("customSkill/lucid/bombVideoLayer")) throw new Exception("Boss marker routing failed");
        if (!NativeBossVideoEffect.Handle("customSkill/lucid/furyStopVideoLayer")) throw new Exception("Boss stop marker failed");
        if (UnityEngine.Object.FindObjectsOfType<NativeSkillVideo>().Length != 1) throw new Exception("Boss stop affected skill channel or failed to stop");
        NativeSkillVideo.Stop(NativeSkillVideo.Channel.PlayerSkill);
        NativeSkillVideo.Stop(NativeSkillVideo.Channel.BossScene);
        Debug.Log("MCV_UNITY_PASS decoder channels replacement cleanup IMG_markers=19 boss_stop");
    }
}
#endif
