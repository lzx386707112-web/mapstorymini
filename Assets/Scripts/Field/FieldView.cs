using System.Collections.Generic;
using UnityEngine;

// 原版地图渲染：瓦片/摆件主体（分块贴图）+ 远景视差层 + 前景层 + NPC + 传送门光洞。
// 全部保持 1:1 地图像素；排序沿用项目约定：
//   远景 -800+no / 主体 -400 / NPC 100 / 传送门 200 / 玩家与怪物 300 / 前景 600+no
public sealed class FieldView : MonoBehaviour
{
    FieldMap map;
    Transform root;
    readonly List<SpriteRenderer> body = new List<SpriteRenderer>();
    readonly List<SpriteRenderer> npcs = new List<SpriteRenderer>();
    readonly List<Portal> portals = new List<Portal>();
    readonly List<Back> backs = new List<Back>();

    public bool HasMap => map != null;

    public void Show(FieldMap m)
    {
        Clear();
        map = m;
        if (m == null) return;

        root = new GameObject("Field_" + m.id).transform;
        root.SetParent(transform, false);

        foreach (var chunk in m.chunks)
        {
            var sp = Art.FrameBottom(chunk.file);
            if (sp == null)
            {
                Debug.LogWarning("[Field] 缺少地图贴图 " + chunk.file);
                continue;
            }
            var go = new GameObject("Chunk");
            go.transform.SetParent(root, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sp;
            sr.sortingOrder = -400;
            // 贴图左下角 = 地图像素 (x, y+h) 换算后的世界坐标
            go.transform.position = new Vector3(m.WorldX(chunk.x), m.WorldY(chunk.y + chunk.h), 0f);
            body.Add(sr);
        }

        foreach (var npc in m.npcs)
        {
            if (npc.hide != 0) continue;          // life/hide=1：原客户端不绘制
            if (npc.art == null) continue;
            var rel = Str(npc.art, "file");
            var origin = Vec(npc.art, "origin");
            var sp = Art.FrameAt(rel, origin);
            if (sp == null) continue;
            var go = new GameObject("Npc_" + npc.id);
            go.transform.SetParent(root, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sp;
            sr.sortingOrder = 100;
            go.transform.position = new Vector3(m.WorldX(npc.x), m.WorldY(npc.y), 0f);
            npcs.Add(sr);
        }

        // 传送门（光洞）：原端 pt=2 的门才画 MapHelper 的 pv 动画；进图方式仍是「按 ↑」
        FieldPortalArt.Ensure();
        if (FieldPortalArt.Visible.Count > 0)
            foreach (var p in m.portals)
            {
                if (p.type != 2) continue;
                portals.Add(new Portal(root, m, p));
            }

        foreach (var b in m.backs) backs.Add(new Back(root, m, b));
    }

    public void Clear()
    {
        backs.Clear();
        body.Clear();
        npcs.Clear();
        portals.Clear();
        if (root != null) Destroy(root.gameObject);
        root = null;
        map = null;
    }

    void LateUpdate()
    {
        var cam = Camera.main;
        if (cam != null)
            foreach (var b in backs) b.Update(cam);
        foreach (var p in portals) p.Tick();
    }

    // 光洞：MapHelper.img/portal/game/pv 的循环动画。
    // 只有 pt=2(VISIBLE) 的门有可见贴图；pt=0/1/3/9 在原端都是隐形的。
    sealed class Portal
    {
        readonly SpriteRenderer sr;
        readonly Sprite[] frames;
        readonly float[] delays;
        readonly float cycle;
        readonly float started = Time.time;

        public Portal(Transform parent, FieldMap map, FieldPortal def)
        {
            var defs = FieldPortalArt.Visible;
            frames = new Sprite[defs.Count];
            delays = new float[defs.Count];
            for (int i = 0; i < defs.Count; i++)
            {
                frames[i] = Art.FrameAt(defs[i].file, defs[i].origin);
                delays[i] = defs[i].delay;
                cycle += defs[i].delay;
            }
            var go = new GameObject("Portal_" + def.name);
            go.transform.SetParent(parent, false);
            sr = go.AddComponent<SpriteRenderer>();
            sr.sortingOrder = 200;    // 地图主体(-400)之上、NPC(100)与角色(276+)之下
            go.transform.position = new Vector3(map.WorldX(def.x), map.WorldY(def.y), 0f);
            Tick();
        }

        public void Tick()
        {
            if (cycle <= 0f) return;
            float t = (Time.time - started) % cycle;
            int idx = 0;
            for (int i = 0; i < delays.Length; i++)
            {
                if (t < delays[i]) { idx = i; break; }
                t -= delays[i];
                idx = i;
            }
            if (frames[idx] != null) sr.sprite = frames[idx];
        }
    }

    // CMapLoadable::MakeBack 的定位规则：rx/ry 是相对相机的百分比，
    // type 4/5/6/7 的对应轴上按 5*|r| px/s 自动滚动。
    sealed class Back
    {
        readonly FieldMap map;
        readonly FieldBack def;
        readonly Sprite sprite;
        readonly Transform root;
        readonly List<SpriteRenderer> cells = new List<SpriteRenderer>();
        readonly float started = Time.time;

        public Back(Transform parent, FieldMap map, FieldBack def)
        {
            this.map = map;
            this.def = def;
            root = new GameObject("Back_" + def.no).transform;
            root.SetParent(parent, false);
            sprite = Art.FrameBottom(def.file);
        }

        public void Update(Camera camera)
        {
            if (sprite == null || map == null) return;

            float mapX = camera.transform.position.x * Art.PPU;
            float mapY = -camera.transform.position.y * Art.PPU;
            float halfY = camera.orthographicSize * Art.PPU;
            float halfX = halfY * camera.aspect;

            int type = def.type;
            bool scrollX = type == 4 || type == 6;
            bool scrollY = type == 5 || type == 7;
            bool repeatX = type == 1 || type == 3 || type == 4 || type == 6 || type == 7;
            bool repeatY = type == 2 || type == 3 || type == 5 || type == 6 || type == 7;

            float elapsed = Time.time - started;
            float bx = def.x - def.origin.x + mapX * (1f + (scrollX ? 0f : def.rx / 100f));
            float by = def.y - def.origin.y + mapY * (1f + (scrollY ? 0f : def.ry / 100f));
            if (scrollX) bx -= elapsed * 5f * def.rx;
            if (scrollY) by -= elapsed * 5f * def.ry;

            float stepX = def.cx > 0 ? def.cx : def.size.x;
            float stepY = def.cy > 0 ? def.cy : def.size.y;

            int left = repeatX ? Mathf.FloorToInt((mapX - halfX - bx - def.size.x) / stepX) + 1 : 0;
            int right = repeatX ? Mathf.CeilToInt((mapX + halfX - bx) / stepX) - 1 : 0;
            int top = repeatY ? Mathf.FloorToInt((mapY - halfY - by - def.size.y) / stepY) + 1 : 0;
            int bottom = repeatY ? Mathf.CeilToInt((mapY + halfY - by) / stepY) - 1 : 0;

            int count = 0;
            for (int j = top; j <= bottom; j++)
                for (int i = left; i <= right; i++)
                {
                    if (count == cells.Count)
                    {
                        var go = new GameObject("Cell");
                        go.transform.SetParent(root, false);
                        var sr = go.AddComponent<SpriteRenderer>();
                        sr.sprite = sprite;
                        sr.sortingOrder = (def.front != 0 ? 600 : -800) + def.no;
                        sr.color = new Color(1f, 1f, 1f, def.alpha / 255f);
                        cells.Add(sr);
                    }
                    var cell = cells[count++];
                    cell.enabled = true;
                    cell.transform.position = new Vector3(
                        map.WorldX(bx + i * stepX),
                        map.WorldY(by + j * stepY + def.size.y), 0f);
                }
            for (int i = count; i < cells.Count; i++) cells[i].enabled = false;
        }
    }

    static string Str(Dictionary<string, object> d, string key)
        => d != null && d.ContainsKey(key) && d[key] != null ? d[key].ToString() : "";
    static Vector2 Vec(Dictionary<string, object> d, string key)
    {
        var a = d != null && d.ContainsKey(key) ? d[key] as List<object> : null;
        return a != null && a.Count >= 2 ? new Vector2(FieldCatalog.ToF(a[0]), FieldCatalog.ToF(a[1])) : Vector2.zero;
    }
}
