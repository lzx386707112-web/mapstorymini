using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

// Full field-target overlay, below hit numbers (900) and UI. Boss is drawn before skill.
public sealed class NativeSkillVideo : MonoBehaviour
{
    public enum Channel { PlayerSkill = 0, BossScene = 1 }
    static readonly NativeSkillVideo[] active = new NativeSkillVideo[2];
    // 282 技能层视频（screen* / special / effect 里的 Canvas#Video）可以同时多条，
    // 所以单独一张表，不占用上面两个单槽通道。
    static readonly List<NativeSkillVideo> layers = new List<NativeSkillVideo>();
    static Dictionary<string, object> skillFiles;
    static string pendingBoss;
    McvPlayback playback;
    SpriteRenderer image;
    Texture2D texture;
    Sprite sprite;
    byte[] flipped, source;
    int map;
    PlayerCtrl caster;
    bool hasCaster, layer, loop;
    float delay;

    public static bool PlaySkill(int skillId, PlayerCtrl player)
    {
        if (skillFiles == null)
        {
            var resource = Resources.Load<TextAsset>("NativeSkillVideos");
            if (resource == null) return false;
            skillFiles = (Dictionary<string, object>)MiniJson.Parse(resource.text);
        }
        return skillFiles.TryGetValue(skillId.ToString(), out var path) &&
            Play(Channel.PlayerSkill, (string)path, player);
    }

    public static bool Play(Channel channel, string relative, PlayerCtrl player = null, int? sortingOrder = null)
    {
        int index = (int)channel;
        if (index < 0 || index >= active.Length) throw new ArgumentOutOfRangeException(nameof(channel));
        Stop(channel); // Original Play replaces its channel even when the new file fails.
        if (Path.IsPathRooted(relative) || relative.Contains("..")) throw new InvalidDataException("Invalid video path");
        var go = new GameObject("MCV_" + channel);
        var fx = go.AddComponent<NativeSkillVideo>();
        if (!fx.Attach(File.ReadAllBytes(Path.Combine(WzAssets.DataRoot, relative)), player,
                sortingOrder ?? (channel == Channel.BossScene ? 800 : 801), false, 0))
        {
            Debug.LogError("[MCV] " + relative);
            Delete(go); return false;
        }
        active[index] = fx;
        return true;
    }

    /// <summary>
    /// 播一条来自 IMG 的视频层（Skill 定义里的 Canvas#Video）。
    /// 载荷有两种形态：完整 MCV0 字节，或 `mcv:<相对路径>`（视频已外置到
    /// `Data/Video/skill/...`，见 tools/split_skill_assets.py）。
    /// sortingOrder 由调用方按层 z 决定：负 z 落在施法者之后，正 z 画在前景。
    /// </summary>
    public static NativeSkillVideo PlayNode(byte[] data, string name, PlayerCtrl player,
        int sortingOrder, bool loop = false, float delay = 0)
    {
        data = Resolve(data);
        if (data == null || data.Length == 0) return null;
        var go = new GameObject("MCV_" + name);
        var fx = go.AddComponent<NativeSkillVideo>();
        if (!fx.Attach(data, player, sortingOrder, loop, delay)) { Delete(go); return null; }
        fx.layer = true;
        layers.RemoveAll(item => item == null);
        layers.Add(fx);
        return fx;
    }

    static readonly Dictionary<string, byte[]> videoFiles = new Dictionary<string, byte[]>();

    // 外置视频：载荷是 ASCII 前缀 "mcv:" + 相对 Data 的路径。
    // 少量缓存（同一技能反复放）避免每次读盘。
    static byte[] Resolve(byte[] data)
    {
        if (data == null || data.Length < 5) return data;
        if (data[0] != 'm' || data[1] != 'c' || data[2] != 'v' || data[3] != ':') return data;
        string relative = Encoding.UTF8.GetString(data, 4, data.Length - 4);
        if (videoFiles.TryGetValue(relative, out var cached)) return cached;
        if (Path.IsPathRooted(relative) || relative.Contains(".."))
        {
            Debug.LogError("[MCV] 非法视频路径 " + relative); return null;
        }
        string full = Path.Combine(WzAssets.DataRoot, relative);
        if (!File.Exists(full)) { Debug.LogError("[MCV] 缺少视频 " + relative); return null; }
        var bytes = File.ReadAllBytes(full);
        if (videoFiles.Count > 8) videoFiles.Clear();
        videoFiles[relative] = bytes;
        return bytes;
    }

    public static void ClearLayers()
    {
        var copy = new List<NativeSkillVideo>(layers);
        layers.Clear();
        foreach (var item in copy) if (item != null) item.DestroyLayer();
    }

    bool Attach(byte[] bytes, PlayerCtrl player, int sortingOrder, bool loop, float delay)
    {
        source = bytes;
        this.loop = loop;
        this.delay = delay;
        try
        {
            playback = new McvPlayback(bytes);
            map = Profile.LastMapId; caster = player; hasCaster = player != null;
            image = gameObject.AddComponent<SpriteRenderer>();
            image.sortingOrder = sortingOrder;
            texture = new Texture2D(playback.Video.Width, playback.Video.Height, TextureFormat.RGBA32, false);
            texture.filterMode = FilterMode.Bilinear; texture.wrapMode = TextureWrapMode.Clamp;
            sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(.5f, .5f), Art.PPU, 0, SpriteMeshType.FullRect);
            flipped = new byte[checked(texture.width * texture.height * 4)];
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError("[MCV] " + e.Message);
            Release(); return false;
        }
    }

    void DestroyLayer()
    {
        layers.Remove(this);
        Release(); gameObject.SetActive(false); Delete(gameObject);
    }

    // 循环段（screen3/loop）在当前实现下按「重开一次解码」续播；片段都很短，代价可接受。
    void Restart()
    {
        playback?.Dispose(); playback = null;
        try { playback = new McvPlayback(source); }
        catch (Exception e) { Debug.LogError("[MCV loop] " + e.Message); DestroyLayer(); }
    }

    // KaringSceneCompat keeps the current scene and one pending scene (first wins).
    public static bool QueueBoss(string relative)
    {
        if (active[(int)Channel.BossScene] == null) return Play(Channel.BossScene, relative);
        if (pendingBoss == null) pendingBoss = relative;
        return true;
    }

    public static void Stop(Channel channel)
    {
        if (channel == Channel.BossScene) pendingBoss = null;
        var previous = active[(int)channel];
        if (previous == null) return;
        active[(int)channel] = null;
        previous.Release(); previous.gameObject.SetActive(false); Delete(previous.gameObject);
    }

    void LateUpdate()
    {
        if (playback == null) return;
        if (delay > 0)
        {
            delay -= Time.deltaTime;
            if (delay > 0) return;
        }
        bool finished = playback.Finished;
        bool broken = Profile.LastMapId != map ||
            (hasCaster && (caster == null || caster.hp <= 0 || !caster.gameObject.activeInHierarchy)) ||
            GameLoop.I == null || GameLoop.I.gameOver || GameLoop.I.state != GameLoop.GameState.Playing;
        if (layer)
        {
            if (broken) { DestroyLayer(); return; }
            if (finished)
            {
                if (loop) Restart();
                else { DestroyLayer(); return; }
            }
        }
        else if (broken || finished)
        {
            if (playback.Error != null) Debug.LogError("[MCV] " + playback.Error.Message);
            bool bossChannel = active[(int)Channel.BossScene] == this;
            string next = bossChannel && finished && Profile.LastMapId == map && GameLoop.I != null &&
                !GameLoop.I.gameOver && GameLoop.I.state == GameLoop.GameState.Playing ? pendingBoss : null;
            if (bossChannel) { active[(int)Channel.BossScene] = null; pendingBoss = null; }
            Release(); Delete(gameObject);
            if (next != null) Play(Channel.BossScene, next);
            return;
        }
        if (playback == null) return;
        byte[] frame = playback.TakeFrame();
        if (frame != null)
        {
            int row = texture.width * 4;
            for (int y = 0; y < texture.height; y++)
                Buffer.BlockCopy(frame, y * row, flipped, (texture.height - 1 - y) * row, row);
            texture.LoadRawTextureData(flipped); texture.Apply(false, false); image.sprite = sprite;
        }
        var camera = Camera.main;
        if (camera == null || !camera.orthographic) return;
        transform.position = new Vector3(camera.transform.position.x, camera.transform.position.y, 0);
        float height = camera.orthographicSize * 2;
        transform.localScale = new Vector3(height * camera.aspect * Art.PPU / texture.width, height * Art.PPU / texture.height, 1);
    }

    static void Delete(UnityEngine.Object value)
    {
        if (Application.isPlaying) Destroy(value); else DestroyImmediate(value);
    }

    void Release()
    {
        playback?.Dispose(); playback = null;
        if (image != null) image.sprite = null;
        if (sprite != null) Delete(sprite);
        if (texture != null) Delete(texture);
        sprite = null; texture = null; flipped = null;
    }
    void OnDisable() { Release(); }
    void OnDestroy() { Release(); }
}
