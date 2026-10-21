using System.Collections.Generic;
using UnityEngine;

// 大厅：装扮 / 角色 / 武器 三模块。OnGUI 即时模式绘制，和游戏内 HUD 同风格。
// state==Lobby 时显示；点「出战」调用 GameLoop.StartGame 进入战斗。
public class Lobby : MonoBehaviour
{
    public enum Tab { Dress, Class, Shop }
    static Tab tab = Tab.Dress;

    // 供自动化截图切换标签用
    public static void JumpTo(Tab t) { tab = t; }

    GUIStyle _small, _label, _title, _sw;
    Vector2 _mouse;
    int shopIndex, shopPage;

    void OnGUI()
    {
        var gl = GameLoop.I;
        if (gl == null || gl.state != GameLoop.GameState.Lobby) return;

        UiKit.Begin();
        _mouse = UiKit.MouseGui();
        EnsureStyles();

        // 背景
        UiKit.Fill(new Rect(0, 0, UiKit.W, UiKit.H), new Color(0.07f, 0.09f, 0.13f));
        UiKit.Fill(new Rect(0, 0, UiKit.W, 4f), new Color(0.5f, 0.8f, 1f));

        DrawHeader();
        DrawPreview();
        DrawTabs();

        float cx = 360f, cw = UiKit.W - 360f - 24f;
        if (tab == Tab.Dress) DrawDress(cx, cw);
        else if (tab == Tab.Class) DrawClass(cx, cw);
        else DrawShop(cx, cw);

        DrawLaunch();
    }

    void EnsureStyles()
    {
        if (_small != null) return;
        _small = new GUIStyle { fontSize = 14, normal = { textColor = new Color(0.85f, 0.88f, 0.94f) } };
        _label = new GUIStyle { fontSize = 15, normal = { textColor = Color.white } };
        _title = new GUIStyle { fontSize = 22, fontStyle = FontStyle.Bold, normal = { textColor = new Color(0.95f, 0.97f, 1f) } };
        _sw = new GUIStyle { fontSize = 12, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
    }

    // —————————— 顶部 ——————————
    void DrawHeader()
    {
        GUI.Label(new Rect(28f, 20f, 360f, 30), "冒险岛 · 大厅", _title);
        var g = new GUIStyle(_small) { alignment = TextAnchor.UpperRight, normal = { textColor = new Color(0.98f, 0.82f, 0.35f) }, fontSize = 18 };
        GUI.Label(new Rect(UiKit.W - 260f, 22f, 240f, 28), "金币  " + Profile.gold, g);
    }

    // —————————— 角色预览（左栏）——————————
    void DrawPreview()
    {
        float x = 28f, w = 300f, y = 70f, h = UiKit.H - 70f - 90f;
        UiKit.Fill(new Rect(x, y, w, h), new Color(0.10f, 0.12f, 0.17f, 0.85f));
        UiKit.Fill(new Rect(x, y, w, 2f), new Color(0.5f, 0.8f, 1f));

        float cx = x + w * 0.5f;
        var attrs = Profile.Attributes;
        GUI.Label(new Rect(x+12,y+38,w-24,22), "Lv."+attrs.level+"  STR "+attrs.str+"  DEX "+attrs.dex, _small);
        GUI.Label(new Rect(x+12,y+62,w-24,22), "INT "+attrs.intelligence+"  LUK "+attrs.luk+"  HP "+attrs.maxHp+"  MP "+attrs.maxMp, _small);
        float footY = y + h - 40f;
        DrawCharacter(cx, footY, 300f, Profile.cls, Profile.hair, Profile.outfit, Profile.equipWeapon);

        GUI.Label(new Rect(x, y + 6f, w, 22),
                  ClassDefs.Get(Profile.cls).name + "   武器：" + WeaponDefs.Get(Profile.equipWeapon).name,
                  new GUIStyle(_small) { alignment = TextAnchor.MiddleCenter });
    }

    // 在 GUI 里画「站立帧（含真实发型）+ 服饰染色 + 手持武器」。
    // 角色帧已经是从客户端抽出来的真实发型，直接铺在 body Rect 上即可；
    // 只有服饰是叠加染色层。
    void DrawCharacter(float cx, float footY, float hpx, string cls, string hairKey, string outfit, string weapon)
    {
        GUI.color = Color.white;

        string artKey = cls + "_" + (HairStyles.Contains(hairKey) ? hairKey : "h0");
        var clip = Art.GetClip("chars", artKey, "stand");
        Sprite frame = null;
        if (clip != null && clip.frames.Length > 0) frame = Art.Frame(clip.frames[0]);

        float texH = 120f, texW = 80f;
        if (frame != null && frame.texture != null) { texH = frame.texture.height; texW = frame.texture.width; }
        float visH = texH / Art.PPU;            // 角色身高（世界单位）
        float k = hpx / texH;                   // 贴图像素 → 屏幕像素

        var body = new Rect(cx - texW * k * 0.5f, footY - hpx, texW * k, hpx);

        // 服饰染色叠在角色之上（头发已是角色帧里的真实发型，无需再染）
        var coat = CosmeticFactory.DyeCoat(frame, CosmeticFactory.OutfitColor(outfit));
        if (coat != null && outfit != "none")
            GUI.DrawTexture(body, coat.texture, ScaleMode.StretchToFill);

        if (frame != null && frame.texture != null)
            GUI.DrawTexture(body, frame.texture, ScaleMode.StretchToFill);
        else
            UiKit.Fill(body, new Color(0.6f, 0.6f, 0.6f));

        // 注：不再单独画武器层——原版武器已合成进角色帧，独立画只会在身前叠出静止色块。
    }

    // —————————— 标签栏 ——————————
    void DrawTabs()
    {
        float x = 360f, w = UiKit.W - 360f - 24f, y = 70f, h = 40f;
        string[] names = { "装扮", "角色", "武器" };
        float bw = w / 3f;
        for (int i = 0; i < 3; i++)
        {
            var r = new Rect(x + i * bw, y, bw - 6f, h);
            bool on = (int)tab == i;
            UiKit.Fill(r, on ? new Color(0.22f, 0.42f, 0.62f) : new Color(0.14f, 0.17f, 0.22f));
            GUI.Label(r, names[i], new GUIStyle(_label) { alignment = TextAnchor.MiddleCenter });
            if (Clicked(r)) tab = (Tab)i;
        }
    }

    // —————————— 装扮 ——————————
    void DrawDress(float x, float w)
    {
        float y = 124f;
        SectionTitle(x, y, "头发（真实发型）"); y += 30f;
        y = HairRow(x, y, Profile.hair, id => { Profile.SetHair(id); });

        y += 14f; SectionTitle(x, y, "服饰"); y += 30f;
        y = SwatchRow(x, y, new List<string>(CosmeticFactory.OutfitColors.Keys), Profile.outfit,
                      id => CosmeticFactory.OutfitColor(id), id => { Profile.SetOutfit(id); });

        y += 14f; SectionTitle(x, y, "武器外观（已拥有）"); y += 30f;
        // 已拥有武器：点选即装备（改变手持外观）
        float colW = 150f;
        int col = 0;
        foreach (var oid in Profile.OwnedWeapons)
        {
            float bx = x + (col % 2) * (colW + 10f);
            float by = y + (col / 2) * 38f;
            var r = new Rect(bx, by, colW, 32f);
            bool on = Profile.equipWeapon == oid;
            UiKit.Fill(r, on ? new Color(0.22f, 0.42f, 0.62f) : new Color(0.14f, 0.17f, 0.22f));
            GUI.Label(r, WeaponDefs.Get(oid).name, new GUIStyle(_sw) { alignment = TextAnchor.MiddleCenter });
            if (Clicked(r)) { Profile.EquipWeapon(oid); }
            col++;
        }
    }

    // —————————— 角色 ——————————
    void DrawClass(float x, float w)
    {
        float y = 124f;
        float cardW = (w - 16f) / 2f, cardH = 64f;
        for (int i = 0; i < ClassDefs.Count; i++)
        {
            var d = ClassDefs.At(i);
            float bx = x + (i % 2) * (cardW + 16f);
            float by = y + (i / 2) * (cardH + 12f);
            var r = new Rect(bx, by, cardW, cardH);
            bool on = Profile.cls == d.id;
            UiKit.Fill(r, on ? new Color(0.22f, 0.42f, 0.62f) : new Color(0.14f, 0.17f, 0.22f));
            UiKit.Fill(new Rect(bx, by, 4f, cardH), d.accent);
            GUI.Label(new Rect(bx + 12f, by + 8f, cardW - 20f, 24), d.name,
                      new GUIStyle(_label) { fontStyle = FontStyle.Bold });
            GUI.Label(new Rect(bx + 12f, by + 36f, cardW - 20f, 20), "4 技能 · 点击切换",
                      new GUIStyle(_small) { fontSize = 12 });
            if (Clicked(r)) { Profile.SetClass(d.id); }
        }

        // 当前职业技能列表
        float ly = y + ((ClassDefs.Count + 1) / 2) * (cardH + 12f) + 8f;
        SectionTitle(x, ly, "当前技能"); ly += 28f;
        var cd = ClassDefs.Get(Profile.cls);
        for (int i = 0; i < cd.skills.Length; i++)
        {
            var s = cd.skills[i];
            var native = NativeSkillCatalog.Get(s.art);
            UiKit.Fill(new Rect(x, ly, w, 26f), new Color(0.12f, 0.14f, 0.19f));
            GUI.Label(new Rect(x + 8f, ly + 3f, w - 16f, 22),
                      (i + 1) + ". " + s.name + "   CD " + native.Cooldown + "s   Lv.1 / MP " + native.MpCost, _small);
            ly += 30f;
        }
    }

    // —————————— 武器商店 ——————————
    void DrawShop(float x, float w)
    {
        float y = 160f;
        var shops = WeaponDefs.Shops;
        var shop = shops[shopIndex];
        if (GUI.Button(new Rect(x,120,w * .32f,28), "商店：" + shop.name + " →"))
        { shopIndex = (shopIndex+1)%shops.Count; shopPage = 0; }
        int pages = Mathf.Max(1,(shop.items.Count+3)/4);
        if (GUI.Button(new Rect(x+w*.34f,120,w*.25f,28), "上一页")) shopPage = (shopPage+pages-1)%pages;
        if (GUI.Button(new Rect(x+w*.61f,120,w*.39f,28), (shopPage+1) + "/" + pages + " 下一页")) shopPage = (shopPage+1)%pages;
        if (!Profile.LastSaveSucceeded) GUI.Label(new Rect(x, 94f, w, 26f), "存档保存失败，请检查设备可用空间", _small);
        for (int i = shopPage*4; i < Mathf.Min(shop.items.Count,shopPage*4+4); i++)
        {
            var d = shop.items[i];
            float rh = 58f;
            UiKit.Fill(new Rect(x, y, w, rh), new Color(0.12f, 0.14f, 0.19f, 0.9f));
            UiKit.Fill(new Rect(x, y, 4f, rh), d.color);

            GUI.Label(new Rect(x + 12f, y + 6f, w - 240f, 22), d.name,
                      new GUIStyle(_label) { fontStyle = FontStyle.Bold });
            string stat = "攻击 " + NativeCombatData.Int(d.stats,"incPAD") + " 魔攻 " + NativeCombatData.Int(d.stats,"incMAD") +
                " 需求 Lv." + NativeCombatData.Int(d.stats,"reqLevel") + " STR " + NativeCombatData.Int(d.stats,"reqSTR") +
                (WeaponDefs.CanEquip(d,Profile.Attributes,Profile.cls) ? "" : "（未满足装备条件）");
            GUI.Label(new Rect(x + 12f, y + 30f, w - 240f, 20), stat,
                      new GUIStyle(_small) { fontSize = 12 });

            bool owned = Profile.OwnsWeapon(d.id);
            var equip = new Rect(x + w - 220f, y + 12f, 72f, 34f);
            bool eq = Profile.equipWeapon == d.id;
            if (owned)
            {
                UiKit.Fill(equip, eq ? new Color(0.25f, 0.55f, 0.35f) : new Color(0.2f, 0.3f, 0.4f));
                GUI.Label(equip, eq ? "已装备" : "装备", new GUIStyle(_sw) { alignment = TextAnchor.MiddleCenter });
                if (!eq && Clicked(equip)) Profile.EquipWeapon(d.id);
            }
            var buy = new Rect(x + w - 140f, y + 12f, 128f, 34f);
            bool can = Profile.gold >= d.price;
            UiKit.Fill(buy, can ? new Color(0.5f, 0.4f, 0.15f) : new Color(0.22f, 0.18f, 0.15f));
            GUI.Label(buy, "购买 " + d.price, new GUIStyle(_sw) { alignment = TextAnchor.MiddleCenter });
            if (can && Clicked(buy)) Profile.TryBuyWeapon(shop.id,i,d.id);
            y += rh + 10f;
        }
    }

    // —————————— 出战 ——————————
    void DrawLaunch()
    {
        float bw = 260f, bh = 52f;
        var r = new Rect((UiKit.W - bw) * 0.5f, UiKit.H - 76f, bw, bh);
        UiKit.Fill(r, new Color(0.85f, 0.45f, 0.2f));
        GUI.Label(r, "出  战", new GUIStyle(_title) { alignment = TextAnchor.MiddleCenter, fontSize = 24 });
        if (Clicked(r))
        {
            if (GameLoop.I != null) GameLoop.I.StartGame();
        }
    }

    // —————————— 小工具 ——————————
    void SectionTitle(float x, float y, string t)
    {
        var left = new GUIStyle(_label) { fontStyle = FontStyle.Bold, normal = { textColor = new Color(0.6f, 0.85f, 1f) } };
        GUI.Label(new Rect(x, y, 300f, 24), t, left);
    }

    // 一排真实发型按钮：点击切换
    float HairRow(float x, float y, string cur, System.Action<string> onPick)
    {
        float bw = 96f, bh = 34f, gap = 8f;
        int cols = 3;
        for (int i = 0; i < HairStyles.Count; i++)
        {
            float bx = x + (i % cols) * (bw + gap);
            float by = y + (i / cols) * (bh + gap);
            var r = new Rect(bx, by, bw, bh);
            bool on = cur == HairStyles.Keys[i];
            UiKit.Fill(r, on ? new Color(0.22f, 0.42f, 0.62f) : new Color(0.14f, 0.17f, 0.22f));
            GUI.Label(r, HairStyles.Names[i], new GUIStyle(_sw) { alignment = TextAnchor.MiddleCenter });
            if (Clicked(r)) onPick(HairStyles.Keys[i]);
        }
        int rows = (HairStyles.Count + cols - 1) / cols;
        return y + rows * (bh + gap) + 6f;
    }

    // 一排色板：点击切换
    float SwatchRow(float x, float y, List<string> keys, string cur, System.Func<string, Color> colorOf, System.Action<string> onPick)
    {
        float sz = 46f, gap = 10f;
        for (int i = 0; i < keys.Count; i++)
        {
            float bx = x + i * (sz + gap);
            var r = new Rect(bx, y, sz, sz);
            bool on = cur == keys[i];
            UiKit.Fill(r, colorOf(keys[i]));
            if (on)   // 选中描边
            {
                UiKit.Fill(new Rect(bx - 2f, y - 2f, sz + 4f, 2f), Color.white);
                UiKit.Fill(new Rect(bx - 2f, y + sz, sz + 4f, 2f), Color.white);
                UiKit.Fill(new Rect(bx - 2f, y - 2f, 2f, sz + 4f), Color.white);
                UiKit.Fill(new Rect(bx + sz, y - 2f, 2f, sz + 4f), Color.white);
            }
            GUI.Label(new Rect(bx, y + sz - 14f, sz, 14), keys[i],
                      new GUIStyle(_sw) { fontSize = 10, alignment = TextAnchor.MiddleCenter });
            if (Clicked(r)) onPick(keys[i]);
        }
        return y + sz + 6f;
    }

    bool Clicked(Rect r) => Input.GetMouseButtonDown(0) && r.Contains(_mouse);
}
