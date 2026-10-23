namespace AppUsage.Core;

/// <summary>「应用明细」表的排序。抽成静态方法是为了能单测。</summary>
internal static class AppSorter
{
    /// <summary>
    /// 按指定键排序。
    /// <para>
    /// key：<c>name</c>（应用名）/ <c>today</c> / <c>d7</c> / <c>d30</c> / <c>total</c> / <c>last</c>。
    /// desc = true 表示**从大到小**（默认累计时长就是降序）。
    /// </para>
    /// </summary>
    public static List<AppAggregate> Sort(IEnumerable<AppAggregate> items, string key, bool desc)
    {
        var list = items.ToList();
        list = key switch
        {
            "name" => list.OrderBy(a => a.Name, StringComparer.CurrentCulture).ToList(),
            "today" => list.OrderBy(a => a.Today).ToList(),
            "d7" => list.OrderBy(a => a.Week).ToList(),
            "d30" => list.OrderBy(a => a.Month).ToList(),
            "last" => list.OrderBy(a => a.LastSeen).ToList(),
            _ => list.OrderBy(a => a.Total).ToList(),
        };
        if (desc) list.Reverse();
        return list;
    }
}
