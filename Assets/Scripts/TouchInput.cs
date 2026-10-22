using UnityEngine;

// 输入意图桥。
// 原版是纯键盘操作，现在键盘（InputHub）和屏幕虚拟键盘都往这里写，
// PlayerCtrl 只读 MoveH / MoveV / TakeJump，不关心是哪种设备按的。
// （旧的触屏摇杆 TouchPad 已随「放弃触屏点击」一起移除，文件只剩这层意图接口。）
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
