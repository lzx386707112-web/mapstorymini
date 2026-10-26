using MapleDash.Js;

// NPCScriptManager.nextLevel 的纯 C# 翻译，所有自定义脚本共用同一路由。
public enum NativeScriptRouteType { None, Next, Previous, PreviousNext, Ok, Select, NextSelect, Number, Text, AcceptDecline, YesNo }
public sealed class NativeScriptRoute
{
    public NativeScriptRouteType type;
    public string previous, next, prefix;
    public void Set(NativeScriptRouteType kind, string before = null, string after = null, string selectionPrefix = null)
    { type = kind; previous = before; next = after; prefix = selectionPrefix; }
    public bool Resolve(int mode, int selection, string text, out string function, out JsValue[] arguments)
    {
        function = null; arguments = System.Array.Empty<JsValue>();
        if (mode == -1) return false;
        switch (type)
        {
            case NativeScriptRouteType.Select:
                if (mode == 0) return false;
                function = "level" + prefix + selection; break;
            case NativeScriptRouteType.NextSelect: case NativeScriptRouteType.Number:
                if (mode == 0) return false;
                function = "level" + next; arguments = new JsValue[] { new JsNumber(selection) }; break;
            case NativeScriptRouteType.Text:
                if (mode == 0) return false;
                function = "level" + next; arguments = new JsValue[] { new JsString(text ?? "") }; break;
            case NativeScriptRouteType.Next: case NativeScriptRouteType.Previous: case NativeScriptRouteType.PreviousNext:
            case NativeScriptRouteType.Ok: case NativeScriptRouteType.AcceptDecline: case NativeScriptRouteType.YesNo:
                // Java 字符串拼接 null 得 "null"，不能默默回退 action()。
                function = "level" + ((mode == 0 ? previous : next) ?? "null"); break;
            default: throw new JsRuntimeException("Unsupported nextLevel route: " + type);
        }
        return true;
    }
}
