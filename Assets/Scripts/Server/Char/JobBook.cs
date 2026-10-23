// 技能书分组。源端 GameConstants.getSkillBook：只有龙神（2210~2218）有独立技能书，
// 其余职业（含冒险家全系、骑士团、战神）都是第 0 本。characters.sp 是逗号分隔的 10 段。
//
// 放在 Server 层：SP 落库、建号初始化都要用它，不该依赖客户端的职业表。
public static class JobBook
{
    public const int BookCount = 10;

    public static int Of(int jobId)
    {
        if (jobId >= 2210 && jobId <= 2218) return jobId - 2209;
        return 0;
    }
}
