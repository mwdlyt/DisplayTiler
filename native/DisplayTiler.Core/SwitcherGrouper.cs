namespace DisplayTiler.Core;

public static class SwitcherGrouper
{
    public static IReadOnlyList<ApplicationGroup> Group(IEnumerable<WindowRecord> windows) => windows
        .GroupBy(window => window.ApplicationKey, StringComparer.OrdinalIgnoreCase)
        .Select(group =>
        {
            var ordered = group.OrderByDescending(window => window.LastActivatedUnixMilliseconds).ToArray();
            return new ApplicationGroup(
                group.Key,
                ordered[0].ApplicationName,
                ordered,
                ordered[0].LastActivatedUnixMilliseconds);
        })
        .OrderByDescending(group => group.LastActivatedUnixMilliseconds)
        .ThenBy(group => group.Name, StringComparer.OrdinalIgnoreCase)
        .ToArray();
}
