using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

// Full field-target overlay, below hit numbers (900) and UI. Boss is drawn before skill.
public sealed class NativeSkillVideo : MonoBehaviour
{
    public enum Channel { PlayerSkill = 0, BossScene = 1 }
    static readonly NativeSkillVideo[] active = new NativeSkillVideo[2];
    static Dictionary<string, object> skillFiles;
    static string pendingBoss;
    McvPlayback playback;
    SpriteRenderer image;
    Texture2D texture;
    Sprite sprite;
    byte[] flipped;
    int map;
    PlayerCtrl caster;
    bool hasCaster;

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
        try
        {
            fx.playback = new McvPlayback(File.ReadAllBytes(Path.Combine(WzAssets.DataRoot, relative)));
            fx.map = Profile.LastMapId; fx.caster = player; fx.hasCaster = player != null;
            fx.image = go.AddComponent<SpriteRenderer>();
            fx.image.sortingOrder = sortingOrder ?? (channel == Channel.BossScene ? 800 : 801);
            fx.texture = new Texture2D(fx.playback.Video.Width, fx.playback.Video.Height, TextureFormat.RGBA32, false);
            fx.texture.filterMode = FilterMode.Bilinear; fx.texture.wrapMode = TextureWrapMode.Clamp;
            fx.sprite = Sprite.Create(fx.texture, new Rect(0, 0, fx.texture.width, fx.texture.height), new Vector2(.5f, .5f), Art.PPU, 0, SpriteMeshType.FullRect);
            fx.flipped = new byte[checked(fx.texture.width * fx.texture.height * 4)];
            active[index] = fx;
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError("[MCV] " + relative + ": " + e.Message);
            fx.Release(); Delete(go); return false;
        }
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
        bool finished = playback.Finished;
        if (Profile.LastMapId != map || (hasCaster && (caster == null || caster.hp <= 0 || !caster.gameObject.activeInHierarchy)) ||
            GameLoop.I == null || GameLoop.I.gameOver || GameLoop.I.state != GameLoop.GameState.Playing || finished)
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
