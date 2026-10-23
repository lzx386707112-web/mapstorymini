// 角色名规则（源端 Character.canCreateChar）：4~13 位英文数字。
// 源端服务端另有敏感词表（ForbiddenName.img），本地单机不做过滤。
//
// 抽成纯函数：建号界面要边打字边提示，Dao 层不该被 UI 直接依赖。
public static class CharNameRules
{
    public const int MinLength = 4;
    public const int MaxLength = 13;

    public static bool Valid(string name)
    {
        if (string.IsNullOrEmpty(name) || name.Length < MinLength || name.Length > MaxLength) return false;
        foreach (var ch in name)
            if (!((ch >= 'a' && ch <= 'z') || (ch >= 'A' && ch <= 'Z') || (ch >= '0' && ch <= '9'))) return false;
        return true;
    }
}
