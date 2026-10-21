using UnityEngine;

// 底部四格技能栏 + 右上角手感调参面板。
// 这个面板是 M0 阶段的核心工具：顿帧/震屏/击退三根滑条实时生效，
// 让你能在手机上亲手判断"这一下爽不爽"，而不是靠看文档猜。
public class GameHUD : MonoBehaviour
{
    public bool showTuning = false;

    // 截图/录屏时把调参面板收起来（它会挡住右边三分之一的画面）
    public static bool HideTuning;

    Texture2D _white;
    GUIStyle _cell, _cellTxt, _small, _box;
    bool _stylesReady;

    Vector2 _mouseGui;

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

    void Awake()
    {
        // 静态字段会被域重载清掉，所以用 PlayerPrefs 传（截图脚本会置 1）
        if (PlayerPrefs.GetInt("hide_tuning", 0) == 1) showTuning = false;
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
        EnsureStyles();
        UiKit.Begin();
        _mouseGui = UiKit.MouseGui();

        DrawSkillBar();
        DrawTopLeft();
        DrawStage();
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

    // —————————— 四格技能栏 ——————————
    //
    // 图标直接用客户端 Skill.wz 的原版 32x32 图标（Art.Tex，Point 过滤），
    // 放大到格子里是整数倍最近邻，和游戏里的技能栏观感一致。
    void DrawSkillBar()
    {
        var sys = GameLoop.I != null && GameLoop.I.player != null
            ? GameLoop.I.player.GetComponent<SkillSystem>()
            : null;
        if (sys == null) return;

        float cell = 88f, gap = 12f;
        float totalW = cell * 4 + gap * 3;
        float x0 = (UiKit.W - totalW) * 0.5f + UiKit.W * 0.06f;
        float y0 = UiKit.H - cell - 34f;

        for (int i = 0; i < 4; i++)
        {
            var s = sys.skills[i];
            var r = new Rect(x0 + i * (cell + gap), y0, cell, cell);

            bool ready = s.left <= 0f;
            var icon = Art.SkillIcon(s.icon);

            // 外框 + 底色（图标透明的地方露出这层）
            Fill(new Rect(r.x - 3, r.y - 3, r.width + 6, r.height + 6), new Color(0.04f, 0.05f, 0.07f, 0.85f));
            Color bg = ready ? new Color(0.16f, 0.18f, 0.24f) : new Color(0.11f, 0.12f, 0.16f);
            Fill(r, bg);

            if (icon != null)
            {
                var ir = new Rect(r.x + 4, r.y + 4, r.width - 8, r.height - 8);
                var old = GUI.color;
                GUI.color = ready ? Color.white : new Color(0.55f, 0.58f, 0.64f);
                GUI.DrawTexture(ir, icon, ScaleMode.StretchToFill);
                GUI.color = old;
            }

            // CD 遮罩：从下往上退，比数字更直观
            if (!ready)
            {
                float k = s.left / s.cd;
                Fill(new Rect(r.x, r.y, r.width, r.height * k), new Color(0.02f, 0.03f, 0.05f, 0.72f));
                GUI.Label(new Rect(r.x, r.y + r.height * 0.5f - 12, r.width, 24),
                          Mathf.CeilToInt(s.left).ToString(), new GUIStyle(_cellTxt) { fontSize = 20 });
            }

            // 左上角序号
            GUI.Label(new Rect(r.x + 4, r.y - 1, 20, 16), (i + 1).ToString(),
                      new GUIStyle(_cellTxt) { fontSize = 11, alignment = TextAnchor.UpperLeft });

            // 名字压在格子下沿（图标是原版画，名字只做辅助）
            Fill(new Rect(r.x - 12, r.y + r.height + 2, r.width + 24, 17), new Color(0f, 0f, 0f, 0.55f));
            var nm = new GUIStyle(_cellTxt) { fontSize = 11 };
            nm.normal.textColor = ready ? new Color(0.96f, 0.97f, 1f) : new Color(0.62f, 0.66f, 0.72f);
            GUI.Label(new Rect(r.x - 12, r.y + r.height + 2, r.width + 24, 17), s.name, nm);

            // 点击释放（触屏与鼠标共用这条路径）
            bool pressed = Input.GetMouseButtonDown(0) && r.Contains(_mouseGui);
            if (pressed || Input.GetKeyDown(KeyCode.Alpha1 + i))
                sys.TryCast(i);
        }
    }

    // —————————— 左上角状态 ——————————
    void DrawTopLeft()
    {
        if (GameLoop.I == null || GameLoop.I.player == null) return;
        var p = GameLoop.I.player;

        float w = 210f, h = 16f;
        var back = new Rect(18f, 18f, w, h);
        Fill(new Rect(back.x - 2, back.y - 2, back.width + 4, back.height + 4), new Color(0f, 0f, 0f, 0.65f));
        Fill(back, new Color(0.22f, 0.1f, 0.12f));
        Fill(new Rect(back.x, back.y, back.width * Mathf.Clamp01(p.hp / p.hpMax), back.height),
             new Color(0.9f, 0.28f, 0.32f));

        var st = new GUIStyle(_small);
        st.normal.textColor = Color.white;
        GUI.Label(new Rect(22f, 16f, 200f, 20), "HP  " + Mathf.CeilToInt(p.hp) + " / " + Mathf.CeilToInt(p.hpMax), st);
        GUI.Label(new Rect(18f, 40f, 260f, 20), "击杀 " + GameLoop.I.killCount + "   深度层 " + (p.lane + 1) + "/5", _small);

        // 金币 + 职业 + 武器
        var gold = new GUIStyle(_small);
        gold.normal.textColor = new Color(0.98f, 0.82f, 0.35f);
        GUI.Label(new Rect(18f, 60f, 300f, 20),
                  "金币 " + Profile.gold + "    " + ClassDefs.Get(Profile.cls).name +
                  "   武器：" + WeaponDefs.Get(Profile.equipWeapon).name, gold);

        // 返回大厅按钮
        var br = new Rect(18f, 84f, 96f, 28f);
        Fill(br, new Color(0.16f, 0.2f, 0.3f, 0.92f));
        Fill(new Rect(br.x, br.y, br.width, 2f), new Color(0.5f, 0.8f, 1f));
        var bs = new GUIStyle(_small);
        bs.alignment = TextAnchor.MiddleCenter;
        bs.normal.textColor = Color.white;
        GUI.Label(br, "返回大厅", bs);
        if (Input.GetMouseButtonDown(0) && br.Contains(_mouseGui))
        {
            Profile.Save();
            if (GameLoop.I != null) GameLoop.I.EnterLobby();
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
