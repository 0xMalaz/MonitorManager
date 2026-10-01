using MonitorCenter.Models;

namespace MonitorCenter;

internal static class MonitorLabeler
{
    public static IReadOnlyList<MonitorSnapshot> OrderAndLabel(IEnumerable<MonitorSnapshot> monitors)
    {
        var ordered = monitors
            .OrderBy(monitor => monitor.Bounds.Left)
            .ThenBy(monitor => monitor.Bounds.Top)
            .ThenBy(monitor => monitor.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var totals = ordered
            .GroupBy(monitor => monitor.FriendlyName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

        var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var result = new List<MonitorSnapshot>(ordered.Count);

        foreach (var monitor in ordered)
        {
            seen.TryGetValue(monitor.FriendlyName, out var index);
            index++;
            seen[monitor.FriendlyName] = index;

            var displayName = totals[monitor.FriendlyName] > 1
                ? $"{monitor.FriendlyName} ({index})"
                : monitor.FriendlyName;

            result.Add(monitor with { DisplayName = displayName });
        }

        return result;
    }
}
