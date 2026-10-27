// 自检工程的最小桩：Dao.cs 里的 KeymapDao 引用了 CharService.cs 中的 KeymapEntry。
// CharService.cs 依赖整条角色服务链（等级表、技能表、地图…），为了一个 3 字段的
// 类型把整套拖进离线工程不划算，这里按原定义补一份，语义完全一致。
//
// 注意：只有本工程（tools/admin-check）会编译这个文件，Unity 侧用的是 CharService.cs 里的原版。
//
// 依赖 WZ 的后台模块（图鉴/任务浏览/商城/文件树/套装）由 AdminRoutes.RegisterUnityOnly
// 在游戏进程里注册，不进本工程——那会把 ImgFile/WzAssets 整条 Unity 依赖拖进来。
public sealed class KeymapEntry
{
    public int key, type, action;
}
