using UnityEngine;

// 底部四格技能栏 + 右上角手感调参面板。
// 这个面板是 M0 阶段的核心工具：顿帧/震屏/击退三根滑条实时生效，
// 让你能在手机上亲手判断"这一下爽不爽"，而不是靠看文档猜。
public class GameHUD : MonoBehaviour
{
    public bool showTuning = false;

    // 截图/录屏时把调参面板收起来（它会挡住右边三分之一的画面）
    public static bool HideTuning;
    public static bool InterfaceHidden;

    Texture2D _white;
    GUIStyle _cell, _cellTxt, _small, _box;
    bool _stylesReady;

    Vector2 _mouseGui;
    static string castFeedback;
    static float feedbackUntil;

    Texture2D White
    {
        get
        {
            if (_white == null)
            {
                _white = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                _white.SetPixels(new[] { Color.white, Color.white, Color.white, Color.white });
                _white.Apply();
            }
            return _white;
        }
    }

    static float levelUpAt;
    static int levelUpLevel;

    void Awake()
    {
        castFeedback = null; feedbackUntil = 0;
        // 静态字段会被域重载清掉，所以用 PlayerPrefs 传（截图脚本会置 1）
        if (PlayerPrefs.GetInt("hide_tuning", 0) == 1) showTuning = false;
        Profile.LeveledUp += OnLeveledUp;
    }

    void OnDestroy() => Profile.LeveledUp -= OnLeveledUp;

    void OnLeveledUp(int level)
    {
        levelUpAt = Time.unscaledTime;
        levelUpLevel = level;
    }

    void EnsureStyles()
    {
        if (_stylesReady) return;
        _stylesReady = true;

        _box = new GUIStyle();
        _box.normal.background = White;
        _box.normal.textColor = new Color(0.92f, 0.94f, 0.98f);
        _box.padding = new RectOffset(12, 12, 10, 10);

        _cell = new GUIStyle();
        _cell.normal.background = White;

        _cellTxt = new GUIStyle();
        _cellTxt.alignment = TextAnchor.MiddleCenter;
        _cellTxt.fontSize = 13;
        _cellTxt.normal.textColor = Color.white;
        _cellTxt.fontStyle = FontStyle.Bold;

        _small = new GUIStyle();
        _small.fontSize = 12;
        _small.normal.textColor = new Color(0.85f, 0.88f, 0.94f);

        GUI.skin.horizontalSlider.fixedHeight = 18f;
        GUI.skin.horizontalSliderThumb.fixedWidth = 18f;
    }

    void Fill(Rect r, Color c)
    {
        var old = GUI.color;
        GUI.color = c;
        GUI.DrawTexture(r, White);
        GUI.color = old;
    }

    void OnGUI()
    {
        if (InterfaceHidden && !KeyConfigWindow.Visible) return;
        GUI.depth = 0;
        EnsureStyles();
        UiKit.Begin();
        _mouseGui = UiKit.MouseGui();
        if (ShopWindow.Visible) {
            GameWindows.TrackPanels();
            NativeCashWindow.Tick();
            NativeCashWindow.Draw();
            DrawNotice();
            return;
        }

        // 原版底部栏：底板 → 左段状态条 → 右侧按钮 + 快捷格（都在 BottomBar 里按顺序画）
        bool enabled = GUI.enabled;
        GUI.enabled = enabled && !NpcSession.Blocking;
        BottomBar.Draw();

        // 浮窗（技能窗/能力值/帮助…）
        GameWindows.DrawAll();
        GUI.enabled = enabled;
        // 技能施放失败等即时提示
        DrawNotice();
        // NPC 对话（最上层）+ 靠近提示
        NpcDialogUi.Draw();
        NpcDialogUi.DrawHint();
        if (PlayerPrefs.GetInt("debug_keyconfig", 0) == 1)
        {
            PlayerPrefs.DeleteKey("debug_keyconfig");
            if (!KeyConfigWindow.Visible) KeyConfigWindow.Toggle();
        }
        KeyConfigWindow.Draw();
        // 悬浮球/快捷菜单由 InputBridge.OnGUI 绘制（仅游戏内显示）
        // 虚拟按键编辑器也由 InputBridge.OnGUI 全局绘制。
        if (showTuning && !HideTuning) DrawTuning();

        if (GameLoop.I != null && GameLoop.I.gameOver)
        {
            Fill(new Rect(0, 0, UiKit.W, UiKit.H), new Color(0f, 0f, 0f, 0.55f));
            var g = new GUIStyle();
            g.alignment = TextAnchor.MiddleCenter;
            g.fontSize = 34;
            g.fontStyle = FontStyle.Bold;
            g.normal.textColor = new Color(1f, 0.4f, 0.42f);
            GUI.Label(new Rect(0, UiKit.H * 0.42f, UiKit.W, 60), "倒下了", g);
            var s = new GUIStyle(_small);
            s.alignment = TextAnchor.MiddleCenter;
            s.fontSize = 15;
            GUI.Label(new Rect(0, UiKit.H * 0.42f + 52, UiKit.W, 24),
                      "击杀 " + (GameLoop.I != null ? GameLoop.I.killCount : 0) + "  ·  即将重来", s);
        }
    }

    // TouchPad.Update 统一接收每根手指的 Began；OnGUI 只绘制，避免一次点击多次施法。
    // 技能格现在靠右下角（原版快捷栏的位置），并且必须避开左下角的状态栏：
    // 横排放不下时整排抬到状态栏上方，绝不压在血条上。
    const float SkillCell = 88f, SkillGap = 12f;

    public static void SkillBarOrigin(out float x0, out float y0)
    {
        float totalW = SkillCell * 4 + SkillGap * 3;
        x0 = UiKit.W - totalW - 12f;
        y0 = UiKit.H - SkillCell - 12f;

        float panelRight = StatusBar.Panel.width;
        float panelTop = UiKit.H - StatusBar.Panel.height;
        if (x0 < panelRight + 8f)
        {
            x0 = Mathf.Max(12f, UiKit.W - totalW - 12f);
            y0 = Mathf.Max(8f, panelTop - SkillCell - 8f);
        }
    }

    public static Rect SkillButtonRect(int slot)
    {
        float x0, y0;
        SkillBarOrigin(out x0, out y0);
        return new Rect(x0 + slot * (SkillCell + SkillGap), y0, SkillCell, SkillCell);
    }
    public static bool PressSkill(Vector2 position)
    {
        for (int slot=0;slot<4;slot++)
        {
            if (!SkillButtonRect(slot).Contains(position)) continue;
            var player = GameLoop.I != null ? GameLoop.I.player : null;
            var sys = player != null ? player.GetComponent<SkillSystem>() : null;
            if (sys != null)
            {
                bool cast = sys.TryCast(slot);
                castFeedback = cast ? null : sys.LastFailure;
                feedbackUntil = Time.unscaledTime + 1.5f;
            }
            return true;
        }
        return false;
    }

    // 技能窗/快捷格的即时提示（魔法不足、冷却中、绑定成功…）
    void DrawNotice()
    {
        var p = GameLoop.I != null ? GameLoop.I.player : null;
        var sys = p != null ? p.GetComponent<SkillSystem>() : null;
        string text = sys != null ? sys.ActiveNotice : null;
        if (string.IsNullOrEmpty(text)) text = BottomBar.ActiveToast;
        if (string.IsNullOrEmpty(text)) return;
        var st = new GUIStyle(_small);
        st.alignment = TextAnchor.MiddleCenter;
        st.fontSize = 15;
        st.normal.textColor = new Color(1f, 0.94f, 0.72f);
        st.wordWrap = true;
        float width = Mathf.Min(600f, UiKit.W - 40f);
        float height = st.CalcHeight(new GUIContent(text), width);
        float bottom = Mathf.Min(UiKit.H - BottomBar.Height - 18f, BottomBar.QuickPanelRect().y - 12f);
        var r = new Rect((UiKit.W - width) * .5f, bottom - height, width, height);
        // 原版这类系统提示是「白字 + 黑描边」直接压在画面上，没有自绘黑框。
        // 之前这里铺了一层 0.65 alpha 的黑色 Fill，属于自绘成分，已去掉。
        var old = st.normal.textColor;
        st.normal.textColor = new Color(0f, 0f, 0f, 0.9f);
        float d = Mathf.Max(0.5f, BottomBar.S * 0.5f);
        GUI.Label(new Rect(r.x - d, r.y, r.width, r.height), text, st);
        GUI.Label(new Rect(r.x + d, r.y, r.width, r.height), text, st);
        GUI.Label(new Rect(r.x, r.y - d, r.width, r.height), text, st);
        GUI.Label(new Rect(r.x, r.y + d, r.width, r.height), text, st);
        st.normal.textColor = old;
        GUI.Label(r, text, st);
    }

    // —————————— 左上角状态 ——————————
    // HP/MP/EXP 已全部交给左下角的原版状态栏（StatusBar.cs），
    // 这里只留地图、金币、掉落提示和返回按钮，避免同一份数值画两遍。
    void DrawTopLeft()
    {
        if (GameLoop.I == null || GameLoop.I.player == null) return;
        var p = GameLoop.I.player;

        var field = GameLoop.I.field;
        if (field != null && field.Map != null)
        {
            var mapSt = new GUIStyle(_small);
            mapSt.normal.textColor = new Color(0.85f, 0.93f, 1f);
            GUI.Label(new Rect(18f, 16f, 420f, 20),
                      field.Map.name + "  (" + field.Map.id + ")   怪物 " + field.AliveCount +
                      "   击杀 " + GameLoop.I.killCount, mapSt);

            if (!string.IsNullOrEmpty(GroundItem.LastPickup))
            {
                var toast = new GUIStyle(_small);
                toast.normal.textColor = new Color(1f, 0.95f, 0.6f);
                GUI.Label(new Rect(18f, 92f, 300f, 20), "拾取 " + GroundItem.LastPickup, toast);
            }

            // 传送门必须按上才进（原端 pt=2/10 的语义），这里给个明确提示
            if (field.NearPortal != null)
            {
                var tip = new GUIStyle(_small);
                tip.normal.textColor = new Color(0.7f, 1f, 0.8f);
                GUI.Label(new Rect(18f, 112f, 320f, 20), "按 ↑ / W 进入传送门", tip);
            }
        }
        else
        {
            GUI.Label(new Rect(18f, 16f, 260f, 20), "击杀 " + GameLoop.I.killCount + "   深度层 " + (p.lane + 1) + "/5", _small);
        }

        // 金币 + 武器（职业名在状态栏里，这里不重复）
        var gold = new GUIStyle(_small);
        gold.normal.textColor = new Color(0.98f, 0.82f, 0.35f);
        GUI.Label(new Rect(18f, 38f, 320f, 20),
                  "金币 " + Profile.gold + "   武器：" + WeaponDefs.Get(Profile.equipWeapon).name, gold);

        // 退出到角色选择（原「返回大厅」：大厅与预设角色已删除）
        var br = new Rect(18f, 62f, 96f, 28f);
        Fill(br, new Color(0.16f, 0.2f, 0.3f, 0.92f));
        Fill(new Rect(br.x, br.y, br.width, 2f), new Color(0.5f, 0.8f, 1f));
        var bs = new GUIStyle(_small);
        bs.alignment = TextAnchor.MiddleCenter;
        bs.normal.textColor = Color.white;
        GUI.Label(br, "更换角色", bs);
        if (!VirtualControlsEditor.Visible && !VirtualControls.BlockingPointer && Pointer.Pressed && br.Contains(_mouseGui))
        {
            if (GameLoop.I != null) GameLoop.I.ExitToCharSelect();
        }
    }

    // —————————— 关卡信息 / 字幕 / Boss 血条 ——————————
    void DrawStage()
    {
        var st = GameLoop.I != null ? GameLoop.I.stage : null;
        if (st == null) return;

        var lv = st.CurLevel;

        // 关卡标题（中上）
        if (lv != null)
        {
            var ti = new GUIStyle(_small);
            ti.alignment = TextAnchor.UpperCenter;
            ti.fontSize = 16;
            ti.fontStyle = FontStyle.Bold;
            ti.normal.textColor = new Color(0.94f, 0.96f, 1f, 0.92f);
            GUI.Label(new Rect(0, 10, UiKit.W, 22), lv.id + "   " + lv.name, ti);

            var sub = new GUIStyle(_small);
            sub.alignment = TextAnchor.UpperCenter;
            sub.fontSize = 12;
            sub.normal.textColor = new Color(0.72f, 0.78f, 0.87f, 0.8f);
            GUI.Label(new Rect(0, 30, UiKit.W, 20),
                      "目标：" + lv.goal + "    推进 " + (st.segIndex + 1) + "/" + lv.segments.Count, sub);
        }

        // Boss 血条（顶部中央，带阶段分隔线 —— 让玩家知道"还有几段"）
        var b = st.boss;
        if (b != null && !b.dead && b.def != null)
        {
            float bw = UiKit.W * 0.56f;
            float bx = (UiKit.W - bw) * 0.5f;
            float by = 56f, bh = 14f;

            Fill(new Rect(bx - 3, by - 3, bw + 6, bh + 6), new Color(0f, 0f, 0f, 0.75f));
            Fill(new Rect(bx, by, bw, bh), new Color(0.24f, 0.1f, 0.12f));
            Fill(new Rect(bx, by, bw * b.HpRatio, bh), new Color(0.86f, 0.26f, 0.3f));

            // 阶段线
            if (b.def.phases != null)
                foreach (var p in b.def.phases)
                {
                    if (p.at >= 0.999f) continue;
                    Fill(new Rect(bx + bw * p.at, by, 2f, bh), new Color(0f, 0f, 0f, 0.55f));
                }

            var bn = new GUIStyle(_small);
            bn.alignment = TextAnchor.MiddleCenter;
            bn.fontSize = 13;
            bn.fontStyle = FontStyle.Bold;
            bn.normal.textColor = Color.white;
            GUI.Label(new Rect(bx, by - 2, bw, bh), b.def.name + "   " +
                      Mathf.CeilToInt(b.hp) + " / " + Mathf.CeilToInt(b.hpMax), bn);
        }

        // 字幕（技能栏上方）：一次只出一条，不打断操作
        if (!string.IsNullOrEmpty(st.hint) && st.hintT > 0f)
        {
            var hs = new GUIStyle(_small);
            hs.alignment = TextAnchor.MiddleCenter;
            hs.fontSize = 15;
            hs.wordWrap = true;
            hs.normal.textColor = new Color(1f, 0.96f, 0.9f, 0.96f);

            float w = Mathf.Min(UiKit.W * 0.78f, 620f);
            float x = (UiKit.W - w) * 0.5f;
            float y = UiKit.H - 186f;      // 压在技能栏（H-122）上方，别盖住图标
            var rect = new Rect(x, y, w, 44f);

            Fill(new Rect(rect.x, rect.y, rect.width, rect.height), new Color(0.06f, 0.07f, 0.1f, 0.72f));
            Fill(new Rect(rect.x, rect.y, 3f, rect.height), new Color(0.95f, 0.78f, 0.4f, 0.9f));
            GUI.Label(new Rect(rect.x + 10, rect.y + 4, rect.width - 20, rect.height - 8), st.hint, hs);
        }

        // 收尾
        if (st.allClear)
        {
            Fill(new Rect(0, 0, UiKit.W, UiKit.H), new Color(0f, 0f, 0f, 0.5f));
            var g = new GUIStyle();
            g.alignment = TextAnchor.MiddleCenter;
            g.fontSize = 34;
            g.fontStyle = FontStyle.Bold;
            g.normal.textColor = new Color(0.95f, 0.85f, 0.45f);
            GUI.Label(new Rect(0, UiKit.H * 0.40f, UiKit.W, 50), "第一章 · 通关", g);
            var s = new GUIStyle(_small);
            s.alignment = TextAnchor.MiddleCenter;
            s.fontSize = 15;
            GUI.Label(new Rect(0, UiKit.H * 0.40f + 50, UiKit.W, 24),
                      "击杀 " + GameLoop.I.killCount + " 只", s);
        }
    }

    // —————————— 升级提示 ——————————
    // 原版的升级是 Effect/BasicEff.img/LevelUp 动画 + 一声音效；
    // 本阶段先把「升级了」这件事用文字确认掉，动画留到特效批次统一接。
    void DrawLevelUp()
    {
        float age = Time.unscaledTime - levelUpAt;
        if (age < 0f || age > 2.4f) return;

        float k = age < 1.8f ? 1f : 1f - (age - 1.8f) / 0.6f;
        var st = new GUIStyle(_small);
        st.alignment = TextAnchor.MiddleCenter;
        st.fontSize = Mathf.RoundToInt(30f + 6f * Mathf.Clamp01(1f - age * 2f));
        st.fontStyle = FontStyle.Bold;
        st.normal.textColor = new Color(1f, 0.9f, 0.45f, k);

        float y = UiKit.H * 0.3f;
        var shadow = new GUIStyle(st);
        shadow.normal.textColor = new Color(0f, 0f, 0f, k * 0.7f);
        GUI.Label(new Rect(0, y + 2, UiKit.W, 44), "LEVEL  UP", shadow);
        GUI.Label(new Rect(0, y, UiKit.W, 44), "LEVEL  UP", st);

        var sub = new GUIStyle(_small);
        sub.alignment = TextAnchor.MiddleCenter;
        sub.fontSize = 15;
        sub.normal.textColor = new Color(0.95f, 0.97f, 1f, k);
        GUI.Label(new Rect(0, y + 46, UiKit.W, 24), "Lv. " + levelUpLevel + "   HP/MP 已回满", sub);
    }

    // —————————— 手感调参面板 ——————————
    void DrawTuning()
    {
        float w = 268f;
        var panel = new Rect(UiKit.W - w - 16f, 16f, w, 232f);

        Fill(panel, new Color(0.1f, 0.11f, 0.14f, 0.93f));
        Fill(new Rect(panel.x, panel.y, panel.width, 2f), new Color(0.45f, 0.8f, 0.62f));

        GUI.Label(new Rect(panel.x + 12, panel.y + 8, w - 24, 22), "手感调参（实时生效）", _small);

        float y = panel.y + 34f;
        HitFeel.StopMs    = Slider(panel.x + 12, y, w - 24, "顿帧", HitFeel.StopMs, 0f, 140f, "ms"); y += 40f;
        HitFeel.ShakeAmp  = Slider(panel.x + 12, y, w - 24, "震屏", HitFeel.ShakeAmp, 0f, 0.5f, ""); y += 40f;
        HitFeel.Knockback = Slider(panel.x + 12, y, w - 24, "击退", HitFeel.Knockback, 0f, 2.5f, ""); y += 40f;
        HitFeel.FlashDur  = Slider(panel.x + 12, y, w - 24, "闪白", HitFeel.FlashDur, 0f, 0.2f, "s"); y += 40f;

        // 预设：省去从零调的时间，三个起点都来自成熟动作游戏的经验值
        if (GUI.Button(new Rect(panel.x + 12, y, 78, 24), "轻快")) ApplyPreset(35f, 0.10f, 0.55f);
        if (GUI.Button(new Rect(panel.x + 98, y, 78, 24), "标准")) ApplyPreset(60f, 0.18f, 0.90f);
        if (GUI.Button(new Rect(panel.x + 184, y, 72, 24), "厚重")) ApplyPreset(95f, 0.26f, 1.40f);
    }

    void ApplyPreset(float stop, float shake, float kb)
    {
        HitFeel.StopMs = stop;
        HitFeel.ShakeAmp = shake;
        HitFeel.Knockback = kb;
    }

    float Slider(float x, float y, float w, string label, float v, float min, float max, string unit)
    {
        GUI.Label(new Rect(x, y, 60, 18), label, _small);
        var valSt = new GUIStyle(_small);
        valSt.alignment = TextAnchor.MiddleRight;
        GUI.Label(new Rect(x + w - 80, y, 80, 18), v.ToString("0.00") + unit, valSt);
        return GUI.HorizontalSlider(new Rect(x, y + 19, w, 16), v, min, max);
    }
}
