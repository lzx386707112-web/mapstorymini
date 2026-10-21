using UnityEngine;

// 手机端输入桥。
// 键盘和触屏走同一套"意图"接口：PlayerCtrl 只读 MoveH / TakeLaneStep / TakeJump，
// 不再关心是键盘按的、还是手指搓的。这样加摇杆不需要改 PlayerCtrl 的战斗逻辑。
public static class TouchInput
{
    public static float MoveV;      // -1..1，上为正，持续攀爬输入
    public static float MoveH;      // -1..1，左右移动

    static int  _laneStep;          // 一次性：-1 往前，+1 往后
    static bool _jump;              // 一次性：本帧起跳

    // 一次性事件必须"取走即清"，否则摇杆拨一下会连续换好几层
    public static int TakeLaneStep() { int v = _laneStep; _laneStep = 0; return v; }
    public static bool TakeJump()    { bool v = _jump;    _jump = false;  return v; }

    // 重开/切关时调用，防止上一关的手指状态残留
    public static void Reset()
    {
        MoveH = MoveV = 0f; _laneStep = 0; _jump = false;
    }

    public static void PushLaneStep(int d) { _laneStep = d; }
    public static void PushJump()          { _jump = true; }
}

// 左下角浮动摇杆 + 右下角跳跃键。
// 浮动式（按下处即圆心）而不是固定式：横屏握持时拇指落点每次都不一样，
// 固定摇杆会让玩家不断"找摇杆"。
[DefaultExecutionOrder(-100)]
public class TouchPad : MonoBehaviour
{
    bool    _active;
    int     _stickId = -2;       // 握住摇杆的那根手指；和跳跃手指区分开
    Vector2 _origin;
    Vector2 _knob;
    bool    _laneLatched;

    const float R      = 86f;    // 摇杆最大半径（虚拟坐标）
    const float DEAD   = 0.16f;  // 横向死区
    const float LANE_T = 0.55f;  // 上下拨过这个比例才换层

    // 跳跃键几何
    const float JR = 56f;        // 半径

    // 屏幕坐标（origin 左下）转虚拟坐标（GUI 左上）
    Vector2 ToVirtual(Vector2 screen)
        => new Vector2(screen.x / UiKit.Scale, (Screen.height - screen.y) / UiKit.Scale);

    Vector2 JumpCenter() => new Vector2(UiKit.W - 96f, UiKit.H - 112f);

    Texture2D circle;

    void Update()
    {
        UiKit.RefreshMetrics();
        if (GameLoop.I == null || GameLoop.I.player == null ||
            GameLoop.I.state != GameLoop.GameState.Playing || GameLoop.I.gameOver)
        {
            ResetPad();
            return;
        }

        // 输入来源：有触摸走手指（按 fingerId 区分），否则走鼠标
        if (Input.touchCount > 0)
        {
            bool tracked = false;
            foreach (var t in Input.touches)
            {
                Vector2 v = ToVirtual(t.position);
                int id = t.fingerId;
                if (t.phase == TouchPhase.Began)        Press(v, id);
                else if (t.phase == TouchPhase.Moved ||
                         t.phase == TouchPhase.Stationary) Move(v, id);
                else if (t.phase == TouchPhase.Ended ||
                         t.phase == TouchPhase.Canceled)  Release(id);
                if (id == _stickId) tracked = true;
            }
            if (_active && !tracked) Release(_stickId);
        }
        else
        {
            if (_stickId >= 0) Release(_stickId);
            Vector2 v = ToVirtual(Input.mousePosition);
            if (Input.GetMouseButtonDown(0)) Press(v, -1);
            else if (Input.GetMouseButton(0)) Move(v, -1);
            else if (Input.GetMouseButtonUp(0)) Release(-1);
        }

    }

    void OnGUI()
    {
        if (Event.current.type != EventType.Repaint || GameLoop.I == null ||
            GameLoop.I.player == null || GameLoop.I.state != GameLoop.GameState.Playing) return;
        var matrix = GUI.matrix;
        UiKit.Begin();
        DrawStick(JumpCenter(), JR, "跳", true);

        if (_active)
        {
            DrawStick(_origin, R, null, false);
            // 把手：从圆心朝手指方向拉，最多 R
            Vector2 k = _origin + Vector2.ClampMagnitude(_knob - _origin, R);
            FillCircle(k, 34f, new Color(0.96f, 0.98f, 1f, 0.82f));
            FillCircle(k, 26f, new Color(0.55f, 0.78f, 0.66f, 0.9f));
        }
        GUI.matrix = matrix;
    }

    void Press(Vector2 v, int id)
    {
        float vw = UiKit.W, vh = UiKit.H;
        // 技能优先消费自己的命中区域；第二根手指不受摇杆 fingerId 限制。
        if (GameHUD.PressSkill(v)) return;
        // 跳跃键：任何手指按在它的圆内都触发（可和摇杆同时进行）
        if (Vector2.Distance(v, JumpCenter()) <= JR) { TouchInput.PushJump(); return; }

        // 摇杆：只在左半屏、且没被别的手指占用时起手
        if (!_active && v.x < vw * 0.42f && v.y > vh * 0.55f)
        {
            _active = true;
            _stickId = id;
            _origin = v;
            _knob = v;
            _laneLatched = false;
            Move(v, id);
        }
    }

    void Move(Vector2 v, int id)
    {
        if (!_active || id != _stickId) return;   // 只认起手那根手指，跳跃手指不干扰
        _knob = v;
        Vector2 d = Vector2.ClampMagnitude(v - _origin, R);

        // 横向：推到 75% 半径就算满速
        float nx = d.x / (R * 0.75f);
        TouchInput.MoveH = Mathf.Abs(nx) > DEAD ? Mathf.Clamp(nx, -1f, 1f) : 0f;

        float vertical = -d.y / R; // GUI y 向下
        TouchInput.MoveV = Mathf.Abs(vertical) > DEAD ? Mathf.Clamp(vertical,-1f,1f) : 0f;
        // 纵向：拨过阈值立刻换一层，回中后才能再拨
        float ny = PlayerCtrl.Terrain == null ? d.y / R : 0f;
        if (!_laneLatched)
        {
            if (ny < -LANE_T) { TouchInput.PushLaneStep(-1); _laneLatched = true; }
            else if (ny > LANE_T) { TouchInput.PushLaneStep(+1); _laneLatched = true; }
        }
        else if (Mathf.Abs(ny) < 0.22f)
        {
            _laneLatched = false;
        }
    }

    void Release(int id)
    {
        if (_active && id == _stickId)
        {
            _active = false;
            _stickId = -2;
            TouchInput.MoveH = TouchInput.MoveV = 0f;
            _laneLatched = false;
        }
    }

    void DrawStick(Vector2 c, float r, string label, bool jump)
    {
        FillCircle(c, r, new Color(0.1f, 0.13f, 0.18f, jump ? 0.42f : 0.26f));
        FillCircle(c, r - 2f, new Color(0.55f, 0.72f, 0.62f, 0.16f));

        if (label == null) return;

        var st = new GUIStyle();
        st.alignment = TextAnchor.MiddleCenter;
        st.fontSize = 26;
        st.fontStyle = FontStyle.Bold;
        st.normal.textColor = new Color(0.9f, 0.96f, 0.94f, 0.85f);
        GUI.Label(new Rect(c.x - r, c.y - 18f, r * 2f, 36f), label, st);
    }

    void FillCircle(Vector2 c, float r, Color col)
    {
        if (circle == null)
        {
            const int size = 128;
            circle = new Texture2D(size, size, TextureFormat.RGBA32, false);
            circle.wrapMode = TextureWrapMode.Clamp;
            circle.filterMode = FilterMode.Bilinear;
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float distance = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), Vector2.one * (size / 2f));
                    pixels[y * size + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(size / 2f - distance));
                }
            circle.SetPixels(pixels);
            circle.Apply();
        }
        var old = GUI.color;
        GUI.color = col;
        GUI.DrawTexture(new Rect(c.x - r, c.y - r, r * 2f, r * 2f), circle);
        GUI.color = old;
    }

    void ResetPad()
    {
        _active = false;
        _stickId = -2;
        _laneLatched = false;
        TouchInput.Reset();
    }

    void OnApplicationFocus(bool focus) { if (!focus) ResetPad(); }
    void OnApplicationPause(bool paused) { if (paused) ResetPad(); }
    void OnDisable() => ResetPad();
    void OnDestroy() { if (circle != null) Destroy(circle); }
}
