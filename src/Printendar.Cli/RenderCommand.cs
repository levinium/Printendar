using Printendar.Core.Export;
using Printendar.Core.Layout;
using Printendar.Core.Layout.Month;
using Printendar.Core.Paper;
using Printendar.Core.Render;
using Printendar.Core.Model;
using Printendar.Core.Samples;
using Printendar.Core.Text;
using Printendar.Sources.Ics;
using SkiaSharp;

namespace Printendar.Cli;

/// <summary>Lays out a month and writes it as a single-page PDF.</summary>
internal static class RenderCommand
{
    public static int Execute(RenderRequest request, TextWriter output)
    {
        if (!PaperSizes.TryGetById(request.Paper, out var paper))
        {
            throw new ArgumentException(
                $"Unknown paper size '{request.Paper}'. Known sizes: " +
                string.Join(", ", PaperSizes.All.Select(p => p.Id)) + ".");
        }

        var page = new PageSpec(
            paper,
            request.Landscape ? Orientation.Landscape : Orientation.Portrait,
            Margins.FromInches(request.MarginInches));

        var grid = new MonthGridOptions(
            request.Month.Year,
            request.Month.Month,
            request.WeekStart,
            ParseWeekendMode(request.Weekend),
            ParseAdjacentDayMode(request.AdjacentDays));

        using var measurer = SkiaTextMeasurer.CreateWithEmbeddedFont();

        var layout = new LayoutRequest(page, grid, request.Culture, measurer);

        if (request.IcsPath is { } icsPath)
        {
            // The grid draws days either side of the month, so those are read too, or the
            // last row would come out empty.
            var first = new DateOnly(request.Month.Year, request.Month.Month, 1);
            var last = first.AddMonths(1).AddDays(-1);
            var lead = ((int)first.DayOfWeek - (int)request.WeekStart + 7) % 7;
            var trail = 6 - (((int)last.DayOfWeek - (int)request.WeekStart + 7) % 7);
            var window = new DateSpan(first.AddDays(-lead), last.AddDays(trail));

            var events = IcsCalendarReader.Read(
                File.ReadAllText(icsPath),
                "ics",
                window,
                TimeZoneInfo.Local);

            layout = layout with
            {
                Events = events,
                Calendars = [new CalendarLegendEntry("ics", Path.GetFileNameWithoutExtension(icsPath), new SKColor(0x1F, 0x77, 0xB4))],
            };
        }
        else if (request.Demo)
        {
            layout = layout with
            {
                Events = SampleCalendar.ForMonth(request.Month.Year, request.Month.Month),
                Calendars = SampleCalendar.Calendars,
            };
        }

        var scene = new MonthGridStyle().Layout(layout);

        var title = request.Month.ToDateTime(TimeOnly.MinValue).ToString("MMMM yyyy", request.Culture);
        PdfExporter.ExportToFile(scene, request.OutputPath, PdfMetadata.Default with { Title = title }, measurer);

        if (request.PngPath is { } pngPath)
        {
            // A PDF is the artifact, but it cannot be looked at from a terminal or diffed by a
            // test. The PNG comes from the same scene through the same renderer, so what it
            // shows is what the PDF contains.
            RasterRenderer.WritePngToFile(scene, dpi: 96f, RenderOptions.ForPrint, measurer, pngPath);
            output.WriteLine($"Wrote {new FileInfo(pngPath).FullName}");
        }

        var written = new FileInfo(request.OutputPath);

        output.WriteLine($"Wrote {written.FullName}");
        output.WriteLine(
            $"  {title}, {paper.DisplayName}, {(request.Landscape ? "landscape" : "portrait")}, " +
            $"{page.WidthPt:0.#} x {page.HeightPt:0.#} pt, one page, {written.Length / 1024.0:0.#} KB");
        output.WriteLine(
            $"  {scene.Diagnostics.WeekRowCount} week rows, text scaled to " +
            $"{scene.Diagnostics.EffectiveScale:P0}, {scene.Diagnostics.HiddenEventCount} events hidden");

        foreach (var warning in scene.Diagnostics.Warnings)
        {
            output.WriteLine($"  note: {warning}");
        }

        return 0;
    }

    private static WeekendMode ParseWeekendMode(string value) => value.ToLowerInvariant() switch
    {
        "full" => WeekendMode.FullSevenDay,
        "compressed" => WeekendMode.CompressedWeekendColumn,
        "none" => WeekendMode.WeekdaysOnly,
        _ => throw new ArgumentException($"Unknown weekend mode '{value}'. Expected full, compressed, or none."),
    };

    private static AdjacentDayMode ParseAdjacentDayMode(string value) => value.ToLowerInvariant() switch
    {
        "hidden" => AdjacentDayMode.Hidden,
        "muted" => AdjacentDayMode.Muted,
        "full" => AdjacentDayMode.Full,
        _ => throw new ArgumentException($"Unknown adjacent day mode '{value}'. Expected hidden, muted, or full."),
    };
}
