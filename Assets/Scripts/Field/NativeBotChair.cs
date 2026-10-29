using System.Collections.Generic;
using MapleDash.Wz;
using UnityEngine;

// CUser chair effects (93C7C3 / 941417). The bot furniture pool uses pos 0/1.
public sealed class NativeBotChair : MonoBehaviour
{
    sealed class Layer
    {
        public ImgNode node;
        public NativeImgAnimation animation;
    }
    readonly List<Layer> layers = new List<Layer>();
    SpriteAnim avatar;
    int chair;
    string anchorFrame;
    Vector2Int brow;

    public void Set(int id, SpriteAnim character)
    {
        avatar = character;
        if (chair == id) return;
        foreach (var layer in layers) if (layer.animation != null) layer.animation.Remove();
        layers.Clear(); chair = id; anchorFrame = null;
        if (id <= 0) return;
        var root = WzAssets.Node("Item/Install/0301.img/" + id.ToString("D8"));
        foreach (string name in new[] { "effect", "effect2" })
        {
            var node = root?.Get(name);
            if (node == null) continue;
            var animation = NativeImgAnimation.SpawnAt(node, transform, transform.position,
                character.Renderer.sortingOrder + node.Int("z"), repeat:true);
            if (animation != null) layers.Add(new Layer { node = node, animation = animation });
        }
        Refresh();
    }
    public void Refresh()
    {
        if (avatar == null || layers.Count == 0) return;
        if (anchorFrame != avatar.FrameUri)
        {
            anchorFrame = avatar.FrameUri;
            brow = WzCharacter.Attachment(anchorFrame, "brow");
        }
        foreach (var layer in layers)
        {
            bool flip = layer.node.Int("fixed") == 0 && avatar.Renderer.flipX;
            Vector2Int anchor = layer.node.Int("pos") == 1 ? brow : Vector2Int.zero;
            layer.animation.transform.localPosition = new Vector3(
                (avatar.Renderer.flipX ? -anchor.x : anchor.x) / Art.PPU, -anchor.y / Art.PPU, 0);
            layer.animation.Image.flipX = flip;
        }
    }
    void LateUpdate() => Refresh();
}
