using System.Globalization;

namespace CodexTray;

internal sealed record UsageWindow(int UsedPercent, long? WindowDurationMins, DateTimeOffset? ResetsAt)
{
    public int RemainingPercent => Math.Clamp(100 - UsedPercent, 0, 100);

    public string ResetText(DateTimeOffset now)
    {
        if (ResetsAt is null)
        {
            return "reset unknown";
        }

        var remaining = ResetsAt.Value - now;
        if (remaining <= TimeSpan.Zero)
        {
            return "reset due";
        }

        return remaining.TotalDays >= 1
            ? $"resets in {(int)remaining.TotalDays}d {remaining.Hours}h"
            : $"resets in {(int)remaining.TotalHours}h {remaining.Minutes}m";
    }
}

internal sealed record UsageSnapshot(UsageWindow? FiveHour, UsageWindow? Weekly, string? PlanType)
{
    public int? TrayPercent => Weekly?.RemainingPercent ?? FiveHour?.RemainingPercent;

    public string ToDisplayText()
    {
        var now = DateTimeOffset.Now;
        var lines = new List<string>();

        if (!string.IsNullOrWhiteSpace(PlanType))
        {
            lines.Add($"Codex ({CultureInfo.InvariantCulture.TextInfo.ToTitleCase(PlanType)})");
        }
        else
        {
            lines.Add("Codex usage");
        }

        lines.Add(FormatWindow("5-hour", FiveHour, now));
        lines.Add(FormatWindow("Weekly", Weekly, now));
        return string.Join(Environment.NewLine, lines);
    }

    public string ToTooltip()
    {
        var fiveHour = FiveHour is null ? "5h --" : $"5h {FiveHour.RemainingPercent}%";
        var weekly = Weekly is null ? "7d --" : $"7d {Weekly.RemainingPercent}%";
        return $"Codex: {fiveHour} | {weekly} remaining";
    }

    private static string FormatWindow(string label, UsageWindow? window, DateTimeOffset now) =>
        window is null
            ? $"{label}: unavailable"
            : $"{label}: {window.RemainingPercent}% remaining ({window.ResetText(now)})";
}

