using System;
using System.IO;
using System.Reflection;
using UnityEngine;

public static class NativeDllPortChecks
{
    static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }

    public static void Run()
    {
        int[] ids = NativeDamageSkinCatalog.Ids();
        Require(ids.Length == 850, "Source catalog changed; re-export and review");
        Require(!NativeDamageSkinCatalog.Contains(-1) && !NativeDamageSkinCatalog.Contains(115), "Invalid catalog ids accepted");
        foreach (int id in ids)
            Require(WzAssets.HasFile("Effect/DamageSkin/" + id + ".img"), "Missing skin " + id);
        Require(!GameSession.InGame, "Run with no active game session");
        NativeDamageNumber.Show(Vector3.zero, 5, "red", 1);
        var go = GameObject.Find("NativeDamage_5");
        try
        {
            var texture = go.GetComponent<SpriteRenderer>().sprite.texture;
            var glyph = WzAssets.Node("Effect/BasicEff.img/NoRed1/5");
            var canvas = WzAssets.Canvas(glyph);
            Require(texture.width == canvas.Width && texture.height == 57, "Leading-digit group or row canvas is incorrect");
            byte[] pixels = texture.GetRawTextureData();
            byte[] original = WzAssets.Pixels(glyph);
            bool match = false;
            for (int jitter = 0; jitter <= 1; jitter++)
            {
                var expected = new byte[texture.width * 57 * 4];
                int top = 57 - 4 - jitter - canvas.Height;
                for (int y = 0; y < canvas.Height; y++)
                    Buffer.BlockCopy(original, y * canvas.Width * 4, expected,
                        (56 - top - y) * canvas.Width * 4, canvas.Width * 4);
                bool same = true;
                // Fully transparent texels may carry irrelevant source RGB.
                for (int i = 0; i < pixels.Length; i += 4)
                {
                    if (pixels[i + 3] != expected[i + 3]) { same = false; break; }
                    if (pixels[i + 3] == 0) continue;
                    for (int c = 0; c < 3; c++) if (pixels[i + c] != expected[i + c]) same = false;
                }
                match |= same;
            }
            Require(match, "Numeric row differs from original leading-digit Canvas pixels");
            var component = go.GetComponent<NativeDamageNumber>();
            const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
            typeof(NativeDamageNumber).GetField("started", flags).SetValue(component, Time.realtimeSinceStartupAsDouble - .7);
            typeof(NativeDamageNumber).GetMethod("Update", flags).Invoke(component, null);
            Require(Math.Abs(go.GetComponent<SpriteRenderer>().color.a - .5f) < .02f, "400ms hold / 600ms fade");
            Require(Math.Abs(go.transform.position.y - 68f / Art.PPU) < .02f, "47px initial offset / 30px rise");
        }
        finally
        {
            if (go != null)
            {
                var sprite = go.GetComponent<SpriteRenderer>().sprite;
                var texture = sprite.texture;
                UnityEngine.Object.DestroyImmediate(go);
                UnityEngine.Object.DestroyImmediate(sprite);
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }
        Debug.Log("NATIVE_DLL_PORT_PASS: 850 skin files, exact source leading-digit pixels, numeric timing; no player DB modified");
    }
}
