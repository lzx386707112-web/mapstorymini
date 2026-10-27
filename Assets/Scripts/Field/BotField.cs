using System.Collections.Generic;
using Server.Bot;
using UnityEngine;

// 地图假人纸娃娃：复用原IMG轴心、部件z层和SpriteAnim原帧delay。
public sealed class BotField : MonoBehaviour
{
    sealed class View
    {
        public GameObject root;
        public SpriteAnim animation;
        public string appearance;
        public float previousY;
    }
    readonly Dictionary<int, View> views = new Dictionary<int, View>();
    readonly HashSet<int> present = new HashSet<int>();
    readonly List<int> removed = new List<int>();
    Server.Bot.Social.BotOutput output;

    void OnEnable()
    {
        output = BotTypeManager.Output;
        output.Published += OnOutput;
    }
    void OnOutput(Server.Bot.Social.BotOutput.Message message)
    {
        var actor = BotRegistry.Get(message.actorId);
        var field = GetComponent<FieldDirector>();
        if (actor == null || field?.Map == null || message.mapId != field.Map.id ||
            actor.world != GameSession.World || actor.channel != GameSession.Channel) return;
        if (message.kind == Server.Bot.Social.BotOutput.Kind.Emote) Express(message.actorId, message.value);
    }

    public void Synchronize(int mapId, int world, int channel)
    {
        present.Clear();
        foreach (var actor in BotRegistry.OnMap(mapId))
        {
            if (!actor.online || actor.world != world || actor.channel != channel) continue;
            present.Add(actor.id);
            if (!views.TryGetValue(actor.id, out var view))
            {
                var root = new GameObject("SoloMapling_" + actor.id);
                root.transform.SetParent(transform, false);
                var renderer = root.AddComponent<SpriteRenderer>();
                renderer.sortingOrder = 300;
                view = new View { root = root, animation = root.AddComponent<SpriteAnim>(), previousY = actor.y };
                views.Add(actor.id, view);
            }
            var ids = BotAppearance.CharacterIds(actor);
            string key = "bot_" + string.Join("_", ids);
            if (key != view.appearance)
            {
                WzCharacter.Appearance(key, ids);
                view.animation.Init("chars", key, "stand", view.root.GetComponent<SpriteRenderer>());
                view.animation.Play("stand", false, true);
                view.appearance = key;
            }
            view.root.transform.position = new Vector3(actor.x / Art.PPU, -actor.y / Art.PPU, 0);
            string pose = actor.chair > 0 ? "sit" : Pose(actor.stance);
            view.animation.SetBase(pose);
            view.animation.SetFacing((actor.stance & 1) == 0 ? 1 : -1);
            view.animation.speed = (pose == "rope" || pose == "ladder") && !actor.moving && actor.y == view.previousY ? 0 : 1;
            view.previousY = actor.y;
        }
        removed.Clear();
        foreach (var entry in views) if (!present.Contains(entry.Key)) removed.Add(entry.Key);
        foreach (int id in removed) { RemoveView(views[id]); views.Remove(id); }
    }

    // CharacterStance.java；其余姿势不能凭编号猜测。
    static string Pose(int stance)
    {
        switch (stance & ~1)
        {
            case 2: return "walk";
            case 6: return "jump";
            case 14: return "ladder";
            case 16: return "rope";
            default: return "stand";
        }
    }

    public void Express(int actorId, int expression)
    {
        if (!views.TryGetValue(actorId, out var view)) return;
        if (expression == 0) view.animation.ClearExpression();
        else view.animation.Express(expression);
    }

    public void Clear()
    {
        foreach (var view in views.Values) RemoveView(view);
        views.Clear(); present.Clear(); removed.Clear();
    }
    static void RemoveView(View view)
    {
        if (Application.isPlaying) Destroy(view.root);
        else DestroyImmediate(view.root);
    }
    void OnDisable()
    {
        if (output != null) output.Published -= OnOutput;
        output = null;
        Clear();
    }
}
