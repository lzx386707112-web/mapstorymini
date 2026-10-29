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
    const float BgmLoadTimeout = 15f;       // 首次解码最长等待（超时就不播，避免卡住切歌队列）

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
    readonly HashSet<string> failedLoads = new HashSet<string>();
    AudioSource bgmA, bgmB;                  // 交叉淡入淡出用
    AudioSource[] pool;
    int poolCursor;
    string bgmKey;
    Coroutine fadeCo;
    Coroutine bgmCo;                        // 当前切歌流程（含首次解码等待）

    // ------------------------------------------------------------ 生命周期

    // BeforeSceneLoad：进图（FieldDirector.Enter → PlayBgm）可能早于 AfterSceneLoad 的
    // 场景回调，晚建实例会让第一次进图的 BGM 调用直接落空。
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
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

    // Ambient effects own their looping voice; the original SFX cache still owns clips.
    public static AudioClip EffectClip(string key)
        => I != null && Index.TryGetValue(key, out var entry) ? I.LoadOrFetch(key, entry) : null;
    public static bool EffectFailed(string key) => I != null && I.failedLoads.Contains(key);

    // ------------------------------------------------------------ BGM

    /// 播放/切换到指定 BGM（key 如 "Bgm00/FloralLife"）。同一首重复调用无动作。
    public static void PlayBgm(string key)
    {
        if (I == null) { Debug.LogWarning("[Audio] PlayBgm 早于 AudioManager 初始化：" + key); return; }
        if (string.IsNullOrEmpty(key)) return;
        if (!Index.TryGetValue(key, out var e))
        {
            Debug.LogWarning("[Audio] BGM 未在 SoundIndex 登记：" + key);
            return;
        }
        if (I.bgmKey == key && (I.bgmA.isPlaying || I.bgmB.isPlaying)) return;
        I.bgmKey = key;
        if (I.bgmCo != null) { I.StopCoroutine(I.bgmCo); I.bgmCo = null; }
        I.bgmCo = I.StartCoroutine(I.CrossFadeBgm(key, e));
    }

    public static void StopBgm(float fade = BgmFade)
    {
        if (I == null) return;
        I.bgmKey = null;
        if (I.bgmCo != null) { I.StopCoroutine(I.bgmCo); I.bgmCo = null; }
        I.StopFade(fade);
    }

    IEnumerator CrossFadeBgm(string key, Entry e)
    {
        // 一首歌第一次进图时还没解码完，必须等 Load 协程跑完再播。
        // 旧实现在「加载中返回 null」时直接 yield break，于是每首歌第一次进图都是静音，
        // 只有再次进入同一首 BGM 的地图才会出声——这就是「BGM 没生效」的根因。
        var clip = Cached(key);
        if (clip == null)
        {
            failedLoads.Remove(key);              // 换图重新给一次机会（上次可能是 Data 还没同步完）
            if (!loading.Contains(key)) StartCoroutine(Load(key, e));
            float waited = 0f;
            while (loading.Contains(key) && waited < BgmLoadTimeout)
            {
                if (bgmKey != key) { bgmCo = null; yield break; }        // 期间又切了
                waited += Time.unscaledDeltaTime;
                yield return null;
            }
            if (bgmKey != key) { bgmCo = null; yield break; }
            clip = Cached(key);
            if (clip == null)
            {
                bgmKey = null;
                bgmCo = null;
                Debug.LogWarning("[Audio] BGM 无法播放 " + key + "（" +
                    (failedLoads.Contains(key) ? "加载失败" : "加载超时") + "）");
                yield break;                  // 保持现状：不打断正在播的曲目
            }
        }

        if (fadeCo != null) StopCoroutine(fadeCo);
        var from = bgmA.isPlaying ? bgmA : (bgmB.isPlaying ? bgmB : null);
        var to = from == bgmA ? bgmB : bgmA;
        to.clip = clip;
        to.volume = 0f;
        to.Play();

        // 卸载除当前两首之外的 BGM，控制内存
        TrimBgmCache(clip);

        Debug.Log("[Audio] BGM " + key + "（音量 " + BgmVolume.ToString("0.00") + "）");
        bgmCo = null;
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

    AudioClip Cached(string key)
        => clips.TryGetValue(key, out var clip) && clip != null ? clip : null;

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
                failedLoads.Add(key);
                Debug.LogWarning($"[Audio] 加载失败 {key}: {req.error} ({url})");
            }
            else
            {
                clips[key] = DownloadHandlerAudioClip.GetContent(req);
                if(clips[key]==null)failedLoads.Add(key);else failedLoads.Remove(key);
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
