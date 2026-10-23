using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

// 音频中枢：BGM 循环/切歌淡入淡出 + 音效池 + 音量持久化。
//
// 资源不走 Resources/StreamingAssets：音频由 tools/export_sound.py 从源端
// Sound.wz 提取为 MP3，放在原生资源包 Data/Sound 下（索引登记进
// SoundIndex.json，运行时与 IMG 同源直读），加载用
// UnityWebRequestMultimedia file:// 异步解码。
//
// 内存策略：音效全部常驻（合计解码后约十几 MB）；BGM 只保留当前一首
// （一首约 20 MB PCM），切歌时淡入新的、卸载旧的。
public sealed class AudioManager : MonoBehaviour
{
    public static AudioManager I { get; private set; }

    const string PrefBgm = "audio.bgm";
    const string PrefSfx = "audio.sfx";
    const int SfxPool = 10;                 // 并发音效上限（再多也是杂音）
    const float BgmFade = 0.7f;             // 切歌淡入淡出秒数

    public static float BgmVolume
    {
        get => PlayerPrefs.GetFloat(PrefBgm, 0.6f);
        set { PlayerPrefs.SetFloat(PrefBgm, Mathf.Clamp01(value)); if (I != null) I.ApplyBgmVolume(); }
    }
    public static float SfxVolume
    {
        get => PlayerPrefs.GetFloat(PrefSfx, 0.8f);
        set { PlayerPrefs.SetFloat(PrefSfx, Mathf.Clamp01(value)); }
    }

    // key → {相对路径, 时长ms}
    class Entry { public string path; public int ms; }
    static Dictionary<string, Entry> index;
    static bool indexFailed;

    readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
    readonly HashSet<string> loading = new HashSet<string>();
    AudioSource bgmA, bgmB;                  // 交叉淡入淡出用
    AudioSource[] pool;
    int poolCursor;
    string bgmKey;
    Coroutine fadeCo;

    // ------------------------------------------------------------ 生命周期

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (I != null) return;
        var go = new GameObject("[AudioManager]");
        I = go.AddComponent<AudioManager>();
        DontDestroyOnLoad(go);
    }

    void Awake()
    {
        if (I != null && I != this) { Destroy(gameObject); return; }
        I = this;
        bgmA = gameObject.AddComponent<AudioSource>();
        bgmA.loop = true; bgmA.playOnAwake = false; bgmA.priority = 0;
        bgmB = gameObject.AddComponent<AudioSource>();
        bgmB.loop = true; bgmB.playOnAwake = false; bgmB.priority = 0;
        pool = new AudioSource[SfxPool];
        for (int i = 0; i < SfxPool; i++)
        {
            pool[i] = gameObject.AddComponent<AudioSource>();
            pool[i].playOnAwake = false;
        }
        ApplyBgmVolume();
    }

    void ApplyBgmVolume()
    {
        if (bgmA != null) bgmA.volume = BgmVolume;
        if (bgmB != null) bgmB.volume = BgmVolume;
    }

    // ------------------------------------------------------------ 索引

    static Dictionary<string, Entry> Index
    {
        get
        {
            if (index == null && !indexFailed)
            {
                var asset = Resources.Load<TextAsset>("SoundIndex");
                if (asset == null) { indexFailed = true; return index = new Dictionary<string, Entry>(); }
                index = new Dictionary<string, Entry>();
                var raw = (Dictionary<string, object>)MiniJson.Parse(asset.text);
                foreach (var kv in raw)
                {
                    var d = (Dictionary<string, object>)kv.Value;
                    index[kv.Key] = new Entry
                    {
                        path = (string)d["path"],
                        ms = (int)(double)d["ms"],
                    };
                }
            }
            return index;
        }
    }

    public static bool Has(string key) => Index.ContainsKey(key);

    // ------------------------------------------------------------ BGM

    /// 播放/切换到指定 BGM（key 如 "Bgm00/FloralLife"）。同一首重复调用无动作。
    public static void PlayBgm(string key)
    {
        if (I == null || string.IsNullOrEmpty(key) || !Index.TryGetValue(key, out var e)) return;
        if (I.bgmKey == key && (I.bgmA.isPlaying || I.bgmB.isPlaying)) return;
        I.bgmKey = key;
        I.StartCoroutine(I.CrossFadeBgm(key, e));
    }

    public static void StopBgm(float fade = BgmFade)
    {
        if (I == null) return;
        I.bgmKey = null;
        I.StopFade(fade);
    }

    IEnumerator CrossFadeBgm(string key, Entry e)
    {
        var clip = LoadOrFetch(key, e);
        if (clip == null) yield break;            // 加载失败：保持现状
        if (bgmKey != key) { UnloadIfBgm(clip); yield break; }  // 期间又切了

        if (fadeCo != null) StopCoroutine(fadeCo);
        var from = bgmA.isPlaying ? bgmA : (bgmB.isPlaying ? bgmB : null);
        var to = from == bgmA ? bgmB : bgmA;
        to.clip = clip;
        to.volume = 0f;
        to.Play();

        // 卸载除当前两首之外的 BGM，控制内存
        TrimBgmCache(clip);

        fadeCo = StartCoroutine(Fade(from, to, BgmFade));
    }

    void StopFade(float fade)
    {
        if (fadeCo != null) { StopCoroutine(fadeCo); fadeCo = null; }
        if (bgmA.isPlaying || bgmB.isPlaying)
            StartCoroutine(Fade(bgmA.isPlaying ? bgmA : null, null, fade));
    }

    IEnumerator Fade(AudioSource from, AudioSource to, float seconds)
    {
        float t = 0f;
        float v0 = from != null ? from.volume : 0f;
        float v1 = to != null ? BgmVolume : 0f;
        while (t < seconds)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / seconds);
            if (from != null) from.volume = Mathf.Lerp(v0, 0f, k);
            if (to != null) to.volume = Mathf.Lerp(0f, v1, k);
            yield return null;
        }
        if (from != null) { from.Stop(); from.clip = null; }
        if (to != null) to.volume = v1;
        fadeCo = null;
    }

    void TrimBgmCache(AudioClip keep)
    {
        var drop = new List<string>();
        foreach (var kv in clips)
            if (kv.Value != keep && kv.Key.StartsWith("Bgm", StringComparison.Ordinal))
                drop.Add(kv.Key);
        foreach (var k in drop) { Destroy(clips[k]); clips.Remove(k); }
    }

    void UnloadIfBgm(AudioClip clip)
    {
        foreach (var kv in clips)
            if (kv.Value == clip && kv.Key.StartsWith("Bgm", StringComparison.Ordinal))
            { Destroy(clip); clips.Remove(kv.Key); return; }
    }

    // ------------------------------------------------------------ 音效

    /// 播放音效（key 如 "Mob/100100/Damage"、"UI/BtMouseClick"）。vol 为相对音量。
    public static void Play(string key, float vol = 1f, bool pitchVary = true)
    {
        if (I == null || string.IsNullOrEmpty(key)) return;
        if (!Index.TryGetValue(key, out var e)) return;
        if (SfxVolume <= 0.001f) return;
        var clip = I.LoadOrFetch(key, e);
        if (clip == null) return;
        I.Spawn(clip, vol, pitchVary);
    }

    /// 按世界位置衰减播放（x 距离越远越轻），用于怪物受击/死亡等。
    public static void PlayAt(string key, Vector3 worldPos, float vol = 1f, float range = 6f)
    {
        if (I == null) return;
        var cam = Camera.main;
        if (cam == null) { Play(key, vol); return; }
        float d = Mathf.Abs(cam.transform.position.x - worldPos.x);
        float k = Mathf.Clamp01(1f - d / range);
        if (k <= 0.02f) return;
        Play(key, vol * k);
    }

    void Spawn(AudioClip clip, float vol, bool pitchVary)
    {
        // 环形找一个空闲（或最旧的）source；同时播放的音效太多时最旧的被顶掉
        var src = pool[poolCursor];
        for (int i = 0; i < SfxPool; i++)
        {
            var s = pool[(poolCursor + i) % SfxPool];
            if (!s.isPlaying) { src = s; break; }
        }
        src.pitch = pitchVary ? UnityEngine.Random.Range(0.94f, 1.06f) : 1f;
        src.volume = Mathf.Clamp01(vol) * SfxVolume;
        src.clip = clip;
        src.Play();
        poolCursor = (poolCursor + 1) % SfxPool;
    }

    // ------------------------------------------------------------ 加载

    AudioClip LoadOrFetch(string key, Entry e)
    {
        if (clips.TryGetValue(key, out var cached) && cached != null) return cached;
        if (loading.Contains(key)) return null;
        StartCoroutine(Load(key, e));
        return null;
    }

    IEnumerator Load(string key, Entry e)
    {
        loading.Add(key);
        string url = "file://" + Path.Combine(WzAssets.DataRoot, e.path);
        using (var req = UnityWebRequestMultimedia.GetAudioClip(url, AudioType.MPEG))
        {
            yield return req.SendWebRequest();
            bool err = req.result != UnityWebRequest.Result.Success;
            if (err)
            {
                Debug.LogWarning($"[Audio] 加载失败 {key}: {req.error} ({url})");
            }
            else
            {
                clips[key] = DownloadHandlerAudioClip.GetContent(req);
            }
        }
        loading.Remove(key);
    }

    /// 预热：切图/战斗前调用，避免第一次播放时的加载延迟。
    public static void Preload(params string[] keys)
    {
        if (I == null) return;
        foreach (var key in keys)
            if (Index.TryGetValue(key, out var e))
                I.LoadOrFetch(key, e);
    }

    /// 当前 BGM key（供设置界面显示/测试）。
    public static string CurrentBgm => I != null ? I.bgmKey : null;
}
