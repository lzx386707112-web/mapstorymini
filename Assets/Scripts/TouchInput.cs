using UnityEngine;

// 手机端输入桥。
// 键盘和触屏走同一套"意图"接口：PlayerCtrl 只读 MoveH / TakeLaneStep / TakeJump，
// 不再关心是键盘按的、还是手指搓的。这样加摇杆不需要改 PlayerCtrl 的战斗逻辑。
public static class TouchInput
{
    public static float MoveH;      // -1..1，左右移动

    static int  _laneStep;          // 一次性：-1 往前，+1 往后
    static bool _jump;              // 一次性：本帧起跳

    // 一次性事件必须"取走即清"，否则摇杆拨一下会连续换好几层
    public static int TakeLaneStep() { int v = _laneStep; _laneStep = 0; return v; }
    public static bool TakeJump()    { bool v = _jump;    _jump = false;  return v; }

    // 重开/切关时调用，防止上一关的手指状态残留
    public static void Reset()
    {
        MoveH = 0f; _laneStep = 0; _jump = false;
    }

    public static void PushLaneStep(int d) { _laneStep = d; }
    public static void PushJump()          { _jump = true; }
}

// 左下角浮动摇杆 + 右下角跳跃键。
// 浮动式（按下处即圆心）而不是固定式：横屏握持时拇指落点每次都不一样，
// 固定摇杆会让玩家不断"找摇杆"。
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

    void OnGUI()
    {
        // OnGUI 每帧会被 Layout/Repaint 调多次，输入只处理一次，否则一次点击算两次
        if (Event.current.type != EventType.Repaint) return;

        if (GameLoop.I == null || GameLoop.I.player == null) return;

        UiKit.Begin();

        // 输入来源：有触摸走手指（按 fingerId 区分），否则走鼠标
        if (Input.touchCount > 0)
        {
            foreach (var t in Input.touches)
            {
                Vector2 v = ToVirtual(t.position);
                int id = t.fingerId;
                if (t.phase == TouchPhase.Began)        Press(v, id);
                else if (t.phase == TouchPhase.Moved ||
                         t.phase == TouchPhase.Stationary) Move(v, id);
                else if (t.phase == TouchPhase.Ended ||
                         t.phase == TouchPhase.Canceled)  Release(id);
            }
        }
        else
        {
            Vector2 v = UiKit.MouseGui();
            if (Input.GetMouseButtonDown(0)) Press(v, -1);
            else if (Input.GetMouseButton(0)) Move(v, -1);
            else if (Input.GetMouseButtonUp(0)) Release(-1);
        }

        DrawStick(JumpCenter(), JR, "跳", true);

        if (_active)
        {
            DrawStick(_origin, R, null, false);
            // 把手：从圆心朝手指方向拉，最多 R
            Vector2 k = _origin + Vector2.ClampMagnitude(_knob - _origin, R);
            FillCircle(k, 34f, new Color(0.96f, 0.98f, 1f, 0.82f));
            FillCircle(k, 26f, new Color(0.55f, 0.78f, 0.66f, 0.9f));
        }
    }

    void Press(Vector2 v, int id)
    {
        float vw = UiKit.W, vh = UiKit.H;
        // 跳跃键：任何手指按在它的圆内都触发（可和摇杆同时进行）
        if (Vector2.Distance(v, JumpCenter()) <= JR) { TouchInput.PushJump(); return; }

        // 摇杆：只在左半屏、且没被别的手指占用时起手
        if (!_active && v.x < vw * 0.42f && v.y > vh * 0.22f)
        {
            _active = true;
            _stickId = id;
            _origin = v;
            _knob = v;
            _laneLatched = false;
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

        // 纵向：拨过阈值立刻换一层，回中后才能再拨
        float ny = d.y / R;
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
            TouchInput.MoveH = 0f;
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
        // 没有圆形贴图，用四角各切掉一点的矩形近似 —— 便宜且看不清接缝
        float cut = r * 0.29f;
        var old = GUI.color;
        GUI.color = col;

        GUI.DrawTexture(new Rect(c.x - r + cut, c.y - r, (r - cut) * 2f, r * 2f), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(c.x - r, c.y - r + cut, r * 2f, (r - cut) * 2f), Texture2D.whiteTexture);

        GUI.color = old;
    }
}
