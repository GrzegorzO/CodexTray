namespace CodexTray;

internal static class TokenReportPresentationTests
{
    internal static void Run()
    {
        var counts = new TokenCounts(700000, 400000, 12000, 80000, 23000);
        var slices = TokenBreakdown.Create(counts);
        Check(slices.Sum(s => s.Count) == counts.Total, "donut slices partition total tokens");
        Check(slices[0].Count == 300000 && slices[1].Count == 400000 && slices[2].Count == 57000 && slices[3].Count == 23000,
            "cache writes and reasoning are not counted twice");
        Check(TokenBreakdown.Create(TokenCounts.Zero).All(s => s.Count == 0), "empty breakdown");
        Check(TokenBreakdown.Create(new TokenCounts(10, 50, 100, 5, 20)).Sum(s => s.Count) == 15, "inconsistent subsets are capped for chart geometry");
        var project = new ProjectUsage("project:synthetic", "Synthetic project", Color.FromArgb(60, 200, 110), ProjectMarker.Folder, counts, 42);
        foreach (var theme in new[] { ReportTheme.Light, ReportTheme.Night })
        {
            Check(Contrast(theme.Text, theme.Surface) >= 4.5 && Contrast(theme.Muted, theme.Surface) >= 4.5, "readable body and secondary text");
            foreach (var dpi in new[] { 96, 144, 192 })
            {
                var scale = dpi / 96f;
                using var chart = new ProjectTokenChart { Theme = theme };
                chart.SetProjects([project, project with { Id = "project:second", Name = "Another project", Counts = new TokenCounts(300000, 0, 0, 0, 0) },
                    project with { Id = "project:third", Name = "Third project", Counts = new TokenCounts(100000, 0, 0, 0, 0) }], project.Id);
                using var bars = new Bitmap((int)(750 * scale), (int)(430 * scale));
                bars.SetResolution(dpi, dpi);
                using (var graphics = Graphics.FromImage(bars))
                {
                    graphics.Clear(theme.Surface);
                    chart.DrawChart(graphics, new Rectangle(Point.Empty, bars.Size));
                }
                var bar = bars.GetPixel((int)(180 * scale), (int)(200 * scale));
                Check(bar.G > bar.R && bar.G > bar.B, "project bar keeps its color at supported preview DPI");
                Check(chart.HitTest(new Point((int)(180 * scale), (int)(385 * scale))) == 0, "bar label hit test scales with DPI");
                Check(chart.HitTest(new Point((int)(400 * scale), (int)(385 * scale))) == 1, "project slots stay distinct");
                using var details = new TokenDetailChart { Theme = theme };
                details.SetProject(project, counts.Total * 2);
                using var pie = new Bitmap((int)(350 * scale), (int)(550 * scale));
                pie.SetResolution(dpi, dpi);
                using (var graphics = Graphics.FromImage(pie))
                {
                    graphics.Clear(theme.Surface);
                    details.DrawDetails(graphics, new Rectangle(Point.Empty, pie.Size));
                }
                Check(details.SliceAt(new Point((int)(100 * scale), (int)(147 * scale))) == 1, "cached-input wedge hit test scales with DPI");
                Check(details.SliceAt(new Point((int)(40 * scale), (int)(396 * scale))) == 3, "legend exposes reasoning slice");
                Check(details.SliceAt(new Point((int)(175 * scale), (int)(147 * scale))) == -1, "donut center is not a slice");
                Check(details.AccessibleDescription!.Contains("Cache writes 12", StringComparison.Ordinal), "exact cache-write counts are accessible");
            }
        }
        TokenReportForm.VerifyPresentation();
        TokenReportForm.VerifyCustomDates();
        TokenReportForm.VerifyResponsiveLoading();
    }

    private static double Contrast(Color a, Color b)
    {
        var l1 = Luminance(a);
        var l2 = Luminance(b);
        return (Math.Max(l1, l2) + .05) / (Math.Min(l1, l2) + .05);
    }
    private static double Luminance(Color color) => .2126 * Linear(color.R) + .7152 * Linear(color.G) + .0722 * Linear(color.B);
    private static double Linear(byte component)
    {
        var value = component / 255d;
        return value <= .04045 ? value / 12.92 : Math.Pow((value + .055) / 1.055, 2.4);
    }
    private static void Check(bool condition, string name)
    {
        if (!condition) { throw new InvalidOperationException("Report presentation self-test failed: " + name); }
    }
}
