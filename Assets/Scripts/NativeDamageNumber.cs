using System;
using System.Collections.Generic;
using System.Globalization;
using MapleDash.Wz;
using UnityEngine;

// BeiDou.exe Effect_Hit 0x437D0F. Group 1 is the leading digit, group 0
// supplies subsequent digits; these are not two layers of the same digit.
public sealed class NativeDamageNumber : MonoBehaviour
{
    SpriteRenderer image;
    Texture2D texture;
    Sprite sprite;
    Vector3 anchor;
    double started;
    static ImgNode selectedRoot;
    static int loadedSkin, failedSkin = -1;
    public static void ResetSkinFailure() => failedSkin = -1;
    static ImgNode Root(string color)
    {
        var basic = WzAssets.FileRoot("Effect/BasicEff.img");
        if (color == "blue" || color == "violet") return basic;
        if (selectedRoot == null) { selectedRoot = basic; loadedSkin = 0; }
        int skin = NativeDamageSkinCatalog.Selected;
        if (skin == loadedSkin || skin == failedSkin) return selectedRoot;
        try
        {
            var candidate = skin == 0 ? basic : WzAssets.FileRoot("Effect/DamageSkin/" + skin + ".img");
            foreach (string group in new[] { "NoRed0", "NoRed1", "NoCri0", "NoCri1" })
                if (candidate.Get(group)?.Resolve() == null) throw new InvalidOperationException("Missing " + group);
            selectedRoot = candidate; loadedSkin = skin;
        }
        catch (Exception e)
        {
            failedSkin = skin;
            Debug.LogWarning("[DamageSkin] Retaining current skin: " + e.Message);
        }
        return selectedRoot;
    }

    // Effect_Miss 0x438A21 loads the case-sensitive NoRed0/Miss or NoViolet0/Miss.
    // 0x43DE28 centers by Canvas width, places its bottom at the caller's y,
    // then uses the same 400ms hold / 600ms fade / 30px rise as numeric hits.
    public static void Miss(Vector3 position, string color, float scale)
    {
        if (color == "blue") return; // Native Effect_Miss only accepts types 0 and 2.
        var node = Root(color).Get((color == "violet" ? "NoViolet0" : "NoRed0") + "/Miss")?.Resolve();
        if (node == null) return;
        var canvas = WzAssets.Canvas(node);
        var go = new GameObject("NativeDamage_Miss");
        var number = go.AddComponent<NativeDamageNumber>();
        number.texture = WzAssets.Upload(WzAssets.Pixels(node),canvas.Width,canvas.Height,go.name);
        number.sprite = Sprite.Create(number.texture,new Rect(0,0,canvas.Width,canvas.Height),new Vector2(0,1),Art.PPU);
        number.image = go.AddComponent<SpriteRenderer>();
        number.image.sprite = number.sprite; number.image.sortingOrder = 900;
        go.transform.localScale = new Vector3(scale,scale,1);
        number.anchor = position + new Vector3(-(canvas.Width/2)*scale/Art.PPU,canvas.Height*scale/Art.PPU,0);
        go.transform.position = number.anchor;
        number.started = Time.realtimeSinceStartupAsDouble;
    }

    public static void Show(Vector3 position, int amount, string color, float scale)
    {
        string group = color == "cri" ? "NoCri" : color == "blue" ? "NoBlue" : color == "violet" ? "NoViolet" : "NoRed";
        var root = Root(color);
        string text = amount.ToString(CultureInfo.InvariantCulture);
        var digits = new List<ImgNode>();
        var positions = new List<int>();
        int width = 0, overlap = 0;
        for (int i = 0; i < text.Length; i++)
        {
            var node = root.Get(group + (i == 0 ? "1/" : "0/") + text[i])?.Resolve();
            if (node == null) throw new InvalidOperationException("Missing damage glyph: " + group + "/" + text[i]);
            var canvas = WzAssets.Canvas(node);
            int cx = (int)WzAssets.Origin(node).x;
            int right = canvas.Width - cx;
            int x = width + cx - overlap;
            positions.Add(x - cx);
            width = x + right;
            overlap = right * 3 / 5;
            digits.Add(node);
        }
        const int height = 57;
        var rgba = new byte[checked(width * height * 4)];
        if (color == "cri")
        {
            var effect = root.Get("NoCri1/effect");
            if (effect != null) Copy(rgba, width, height, effect, 0, 0);
        }
        for (int i = 0; i < digits.Count; i++)
        {
            int h = WzAssets.Canvas(digits[i]).Height;
            int y = height - (i % 2 == 0 ? 4 : 1) - UnityEngine.Random.Range(0, 2) - h;
            Copy(rgba, width, height, digits[i], positions[i], y);
        }
        var go = new GameObject("NativeDamage_" + text);
        var number = go.AddComponent<NativeDamageNumber>();
        number.texture = WzAssets.Upload(rgba, width, height, go.name);
        number.sprite = Sprite.Create(number.texture, new Rect(0, 0, width, height), new Vector2(0, 1), Art.PPU);
        number.image = go.AddComponent<SpriteRenderer>();
        number.image.sprite = number.sprite;
        number.image.sortingOrder = 900;
        // Caller position is the existing Android hit anchor. Scale is retained
        // as its explicit presentation parameter, not claimed as an EXE value.
        go.transform.localScale = new Vector3(scale, scale, 1);
        number.anchor = position + new Vector3(-(width / 2) * scale / Art.PPU, 47 * scale / Art.PPU, 0);
        go.transform.position = number.anchor;
        number.started = Time.realtimeSinceStartupAsDouble;
    }

    // Original Copy inserts raw Canvas pixels, clipped to the 57px row. Canvas
    // origin.y does not participate in this bottom-aligned digit placement.
    static void Copy(byte[] target, int width, int height, ImgNode node, int x, int y)
    {
        var canvas = WzAssets.Canvas(node);
        byte[] source = WzAssets.Pixels(node);
        for (int sy = Math.Max(0, -y); sy < canvas.Height && sy + y < height; sy++)
        for (int sx = Math.Max(0, -x); sx < canvas.Width && sx + x < width; sx++)
        {
            int src = (sy * canvas.Width + sx) * 4;
            int dst = ((sy + y) * width + sx + x) * 4;
            int a = source[src + 3];
            if (a == 0) continue;
            int background = target[dst + 3] * (255 - a);
            int outAlpha = a * 255 + background;
            for (int c = 0; c < 3; c++)
                target[dst + c] = (byte)((source[src + c] * a * 255 + target[dst + c] * background) / outAlpha);
            target[dst + 3] = (byte)(outAlpha / 255);
        }
    }

    void Update()
    {
        double age = Time.realtimeSinceStartupAsDouble - started;
        if (age >= 1) { Destroy(gameObject); return; }
        transform.position = anchor + Vector3.up * (float)(30 * age / Art.PPU);
        image.color = new Color(1, 1, 1, age <= .4 ? 1 : (float)((1 - age) / .6));
    }

    void OnDestroy()
    {
        if (sprite != null) { if (Application.isPlaying) Destroy(sprite); else DestroyImmediate(sprite); }
        if (texture != null) { if (Application.isPlaying) Destroy(texture); else DestroyImmediate(texture); }
    }
}
