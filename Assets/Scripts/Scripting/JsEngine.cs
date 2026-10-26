using System;
using System.Collections.Generic;

namespace MapleDash.Js
{
    // 一次脚本实例：保留自己的作用域（源端脚本把 status 等状态放在模块级变量里，
    // 每次 action 调用必须看到同一个值）。
    public sealed class JsEngine
    {
        public readonly JsInterpreter Interp;
        public readonly JsScope Scope;
        public readonly JsProgram Program;
        public readonly string Name;

        public JsEngine(string source, string name, JsInterpreter shared = null)
        {
            Name = name ?? "script";
            Interp = shared ?? new JsInterpreter();
            Scope = new JsScope(Interp.Global);
            Program = new JsParser(source ?? "").ParseProgram();
        }

        public bool HasFunction(string name)
        {
            return Scope.TryGet(name, out var v) && v is JsFunction;
        }

        public void Run(Action<JsScope> bindGlobals = null)
        {
            Interp.ResetSteps();
            bindGlobals?.Invoke(Scope);
            Program.Run(Interp, Scope);
        }

        // 直接调用脚本顶层函数（start/action/end），异常交给调用方决定如何收尾。
        public JsValue Call(string fn, params JsValue[] args)
        {
            Interp.ResetSteps();
            var value = Scope.Get(fn);
            if (!(value is JsFunction))
                throw new JsRuntimeException("Script " + Name + " has no function " + fn + "()");
            return Interp.Call(value, JsUndefined.Value, args);
        }

        // 宿主注入：暴露给脚本的全局对象（cm / qm / player …）
        public void Bind(string name, JsValue value) => Scope.Declare(name, value);
    }
}
