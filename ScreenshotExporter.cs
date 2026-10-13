using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace CodexTray;

internal static class ScreenshotExporter
{
    private const int CardWidth = 280;
    private const int CardHeight = 92;
    private const int ColumnCount = 3;
    private static readonly Color TaskbarColor = Color.FromArgb(31, 31, 31);

    public static int Run(string outputDirectory)
    {
        try
        {
            Directory.CreateDirectory(outputDirectory);
            var styles = Enum.GetValues<DisplayStyle>();
            var rows = (int)Math.Ceiling(styles.Length / (double)ColumnCount);
            using var gallery = new Bitmap(CardWidth * ColumnCount, CardHeight * rows);
            using var galleryGraphics = Graphics.FromImage(gallery);
            galleryGraphics.Clear(Color.FromArgb(20, 20, 22));
            galleryGraphics.SmoothingMode = SmoothingMode.AntiAlias;

            var targetScreen = Screen.PrimaryScreen ?? Screen.AllScreens.First();
            var now = DateTimeOffset.Now;
            var snapshot = new UsageSnapshot(
                new UsageWindow(32, 300, now.AddHours(2).AddMinutes(18)),
                new UsageWindow(66, 10_080, now.AddDays(3).AddHours(7)),
                "plus");

            RenderGallery(
                outputDirectory,
                styles,
                snapshot,
                LabelContentMode.Static,
                "styles-gallery.png",
                DrawCard,
                exportIndividualStyles: true);
            RenderGallery(
                outputDirectory,
                styles,
                snapshot,
                LabelContentMode.TimeToReset,
                "time-left-gallery.png",
                DrawCountdownCard,
                exportIndividualStyles: false);
            RenderAgentGallery(outputDirectory, snapshot);
            RenderAgentLayoutGallery(outputDirectory, snapshot);
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static void RenderAgentLayoutGallery(string outputDirectory, UsageSnapshot snapshot)
    {
        using var gallery = new Bitmap(1080, 500);
        using var graphics = Graphics.FromImage(gallery);
        graphics.Clear(Color.FromArgb(20, 20, 22));
        using var titleFont = new Font("Segoe UI", 12, FontStyle.Bold);
        using var captionFont = new Font("Segoe UI", 9);
        using var text = new SolidBrush(Color.White);
        graphics.DrawString("Working agents · sizes and display modes", titleFont, text, 18, 12);
        graphics.DrawString("Three agents · icons grow outward from the battery controls", captionFont, text, 18, 40);
        AgentIndicatorSize[] sizes = [AgentIndicatorSize.Small, AgentIndicatorSize.Normal, AgentIndicatorSize.Large];
        for (var row = 0; row < 3; row++)
            for (var column = 0; column < sizes.Length; column++)
            {
                var size = sizes[column];
                var mode = row == 0 ? AgentIndicatorMode.IconAndNumber : AgentIndicatorMode.RepeatedIcons;
                var side = row == 2 ? AgentIndicatorPosition.Left : AgentIndicatorPosition.Right;
                var card = new Rectangle(12 + column * 356, 70 + row * 142, 344, 130);
                using var background = new SolidBrush(TaskbarColor);
                graphics.FillRectangle(background, card);
                var sizeTitle = $"{size} ({AgentIndicatorRenderer.ScaleFor(size):P0})";
                var modeTitle = row == 0 ? "Icon + number" : $"Icons · {side.ToString().ToLowerInvariant()}";
                graphics.DrawString($"{sizeTitle} · {modeTitle}", captionFont, text, card.X + 12, card.Y + 8);
                using var menu = new ContextMenuStrip();
                using var widget = new TaskbarWidgetForm(menu, Screen.PrimaryScreen ?? Screen.AllScreens.First(), loadSettings: false);
                widget.ApplyDisplaySettings(DisplayStyle.Classic, true, true, LabelContentMode.Static, LabelPosition.Right, PercentagePosition.Center);
                widget.ApplyAgentSettings(true, false, LoadingIconStyle.Robot, side, size, mode);
                widget.SetSnapshot(snapshot);
                widget.SetWorkingAgentCount(3);
                using var preview = widget.RenderPreview(TaskbarColor, 1.6f);
                graphics.DrawImageUnscaled(preview, card.X + 10, card.Y + 38);
            }
        gallery.Save(Path.Combine(outputDirectory, "agent-layout-gallery.png"), ImageFormat.Png);
    }

    private static void RenderAgentGallery(string outputDirectory, UsageSnapshot snapshot)
    {
        var styles = Enum.GetValues<LoadingIconStyle>();
        var rows = (styles.Length + 1) / 2;
        using var gallery = new Bitmap(680, 70 + rows * 138 + 12);
        using var graphics = Graphics.FromImage(gallery);
        graphics.Clear(Color.FromArgb(20, 20, 22));
        using var titleFont = new Font("Segoe UI", 12, FontStyle.Bold);
        using var captionFont = new Font("Segoe UI", 9);
        using var titleBrush = new SolidBrush(Color.White);
        using var captionBrush = new SolidBrush(Color.FromArgb(170, 178, 190));
        graphics.DrawString("Working agents · loading icon options", titleFont, titleBrush, 18, 12);
        graphics.DrawString("Right by default · vertically centered · hidden when idle", captionFont, captionBrush, 18, 39);
        for (var index = 0; index < styles.Length; index++)
        {
            var card = new Rectangle(12 + index % 2 * 334, 70 + index / 2 * 138, 322, 126);
            using var brush = new SolidBrush(TaskbarColor);
            graphics.FillRectangle(brush, card);
            graphics.DrawString(AgentIndicatorRenderer.Title(styles[index]), captionFont, titleBrush, card.X + 12, card.Y + 8);
            using var menu = new ContextMenuStrip();
            using var widget = new TaskbarWidgetForm(menu, Screen.PrimaryScreen ?? Screen.AllScreens.First(), loadSettings: false);
            widget.ApplyDisplaySettings(DisplayStyle.Classic, true, true, LabelContentMode.Static, LabelPosition.Right, PercentagePosition.Center);
            widget.ApplyAgentSettings(true, false, styles[index], AgentIndicatorPosition.Right);
            widget.SetSnapshot(snapshot);
            widget.SetWorkingAgentCount(3);
            using var preview = widget.RenderPreview(TaskbarColor);
            graphics.DrawImage(preview, new Rectangle(card.X + 20, card.Y + 30, preview.Width, preview.Height));
            if (styles[index] == LoadingIconStyle.Robot)
            {
                preview.Save(Path.Combine(outputDirectory, "robot-head.png"), ImageFormat.Png);
            }
        }

        gallery.Save(Path.Combine(outputDirectory, "working-agents-gallery.png"), ImageFormat.Png);
    }

    private static void RenderGallery(
        string outputDirectory,
        DisplayStyle[] styles,
        UsageSnapshot snapshot,
        LabelContentMode labelMode,
        string galleryFileName,
        Action<Graphics, Rectangle, string, Bitmap> drawCard,
        bool exportIndividualStyles)
    {
        var rows = (int)Math.Ceiling(styles.Length / (double)ColumnCount);
        using var gallery = new Bitmap(CardWidth * ColumnCount, CardHeight * rows);
        using var galleryGraphics = Graphics.FromImage(gallery);
        galleryGraphics.Clear(Color.FromArgb(20, 20, 22));
        galleryGraphics.SmoothingMode = SmoothingMode.AntiAlias;

        var targetScreen = Screen.PrimaryScreen ?? Screen.AllScreens.First();
        for (var index = 0; index < styles.Length; index++)
        {
            var style = styles[index];
            using var menu = new ContextMenuStrip();
            using var widget = new TaskbarWidgetForm(menu, targetScreen, loadSettings: false);
            widget.ApplyDisplaySettings(
                style,
                showLabels: true,
                showPercentages: true,
                labelMode,
                LabelPosition.Right,
                PercentagePosition.Center);
            widget.SetSnapshot(snapshot);
            using var preview = widget.RenderPreview(TaskbarColor);

            var column = index % ColumnCount;
            var row = index / ColumnCount;
            var card = new Rectangle(column * CardWidth, row * CardHeight, CardWidth, CardHeight);
            drawCard(galleryGraphics, card, StyleTitle(style), preview);

            if (exportIndividualStyles)
            {
                var fileName = $"style-{style.ToString().ToLowerInvariant()}.png";
                preview.Save(Path.Combine(outputDirectory, fileName), ImageFormat.Png);
            }
        }

        gallery.Save(Path.Combine(outputDirectory, galleryFileName), ImageFormat.Png);
    }

    private static string StyleTitle(DisplayStyle style) => style switch
    {
        DisplayStyle.HighContrast => "High contrast",
        DisplayStyle.Pencil => "Pencil sketch",
        _ => style.ToString()
    };

    private static void DrawCard(Graphics graphics, Rectangle card, string title, Bitmap preview)
    {
        using var cardBrush = new SolidBrush(Color.FromArgb(31, 32, 36));
        using var borderPen = new Pen(Color.FromArgb(57, 59, 65));
        graphics.FillRectangle(cardBrush, card);
        graphics.DrawRectangle(borderPen, card.X, card.Y, card.Width - 1, card.Height - 1);

        using var titleFont = new Font("Segoe UI", 11, FontStyle.Bold, GraphicsUnit.Point);
        using var titleBrush = new SolidBrush(Color.FromArgb(235, 235, 238));
        graphics.DrawString(title, titleFont, titleBrush, card.X + 14, card.Y + 10);

        var scale = 0.8f;
        var width = (int)Math.Round(preview.Width * scale);
        var height = (int)Math.Round(preview.Height * scale);
        var destination = new Rectangle(
            card.X + card.Width - width - 14,
            card.Y + (card.Height - height) / 2,
            width,
            height);
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.DrawImage(preview, destination);
    }

    private static void DrawCountdownCard(Graphics graphics, Rectangle card, string title, Bitmap preview)
    {
        using var cardBrush = new SolidBrush(Color.FromArgb(31, 32, 36));
        using var borderPen = new Pen(Color.FromArgb(57, 59, 65));
        graphics.FillRectangle(cardBrush, card);
        graphics.DrawRectangle(borderPen, card.X, card.Y, card.Width - 1, card.Height - 1);

        using var titleFont = new Font("Segoe UI", 11, FontStyle.Bold, GraphicsUnit.Point);
        using var titleBrush = new SolidBrush(Color.FromArgb(235, 235, 238));
        graphics.DrawString(title, titleFont, titleBrush, card.X + 14, card.Y + 7);

        const float scale = 0.7f;
        var width = (int)Math.Round(preview.Width * scale);
        var height = (int)Math.Round(preview.Height * scale);
        var destination = new Rectangle(
            card.X + (card.Width - width) / 2,
            card.Bottom - height - 6,
            width,
            height);
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.DrawImage(preview, destination);
    }
}
