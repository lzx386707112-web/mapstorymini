// 角色名规则（源端 Character.canCreateChar）：2~12 位中文、英文或数字。
// 源端服务端另有敏感词表（ForbiddenName.img），本地单机不做过滤。
//
// 抽成纯函数：建号界面要边打字边提示，Dao 层不该被 UI 直接依赖。
public static class CharNameRules
{
    public const int MinLength = 2;
    public const int MaxLength = 12;

    public static bool Valid(string name)
    {
        if (string.IsNullOrEmpty(name) || name.Length < MinLength || name.Length > MaxLength) return false;
        foreach (var ch in name)
            if (!((ch >= 'a' && ch <= 'z') || (ch >= 'A' && ch <= 'Z') || (ch >= '0' && ch <= '9') || (ch >= '\u4e00' && ch <= '\u9fa5'))) return false;
        return true;
    }
}
