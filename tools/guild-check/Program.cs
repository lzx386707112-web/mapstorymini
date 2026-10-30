using System;

// 家族面板断言的 dotnet 直跑入口：不启动 Unity（编辑器常被占用时也能验），
// 跑的是 Assets/Editor/GuildWindowChecks.cs 里的同一份 Core()，只走纯几何/纯规则代码。
//
//   cd tools/guild-check && dotnet run
//
// 退出码 0 = 通过，1 = 有断言失败（失败原因打印在 stderr）。
public static class Program
{
    public static int Main()
    {
        try
        {
            GuildWindowChecks.Core();
            Console.WriteLine("[guild-check] 全部通过");
            return 0;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine("[guild-check] 失败：" + e.Message);
            return 1;
        }
    }
}
