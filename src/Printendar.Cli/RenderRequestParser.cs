using System.Globalization;

namespace Printendar.Cli;

/// <summary>Turns command line arguments into <see cref="RenderOptions"/>.</summary>
internal static class RenderRequestParser
{
    public static RenderRequest Parse(ReadOnlySpan<string> args)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);

        var month = new DateOnly(today.Year, today.Month, 1);
        var paper = "letter";
        var landscape = true;
        var marginInches = 0.4f;
        var weekStart = DayOfWeek.Sunday;
        var weekend = "full";
        var adjacent = "muted";
        var culture = CultureInfo.CurrentCulture;
        string? outputPath = null;
        string? pngPath = null;
        var demo = false;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--month":
                    month = ParseMonth(Value(args, ref i, "--month"));
                    break;
                case "--paper":
                    paper = Value(args, ref i, "--paper");
                    break;
                case "--landscape":
                    landscape = true;
                    break;
                case "--portrait":
                    landscape = false;
                    break;
                case "--margin":
                    marginInches = ParseMargin(Value(args, ref i, "--margin"));
                    break;
                case "--week-start":
                    weekStart = ParseWeekStart(Value(args, ref i, "--week-start"));
                    break;
                case "--weekend":
                    weekend = Value(args, ref i, "--weekend");
                    break;
                case "--adjacent":
                    adjacent = Value(args, ref i, "--adjacent");
                    break;
                case "--culture":
                    culture = ParseCulture(Value(args, ref i, "--culture"));
                    break;
                case "--out":
                    outputPath = Value(args, ref i, "--out");
                    break;
                case "--demo":
                    demo = true;
                    break;
                case "--png":
                    pngPath = Value(args, ref i, "--png");
                    break;
                default:
                    throw new ArgumentException($"Unknown option '{args[i]}'.");
            }
        }

        return new RenderRequest(
            month,
            paper,
            landscape,
            marginInches,
            weekStart,
            weekend,
            adjacent,
            culture,
            outputPath ?? $"{month:yyyy-MM}.pdf",
            pngPath,
            demo);
    }

    private static string Value(ReadOnlySpan<string> args, ref int index, string option)
    {
        if (index + 1 >= args.Length)
        {
            throw new ArgumentException($"{option} needs a value.");
        }

        return args[++index];
    }

    private static DateOnly ParseMonth(string value)
    {
        if (DateOnly.TryParseExact(value + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var month))
        {
            return month;
        }

        throw new ArgumentException($"Could not read '{value}' as a month. Expected the form yyyy-MM, for example 2026-03.");
    }

    private static float ParseMargin(string value)
    {
        if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var inches) || inches < 0f)
        {
            throw new ArgumentException($"Could not read '{value}' as a margin in inches.");
        }

        return inches;
    }

    private static DayOfWeek ParseWeekStart(string value) => value.ToLowerInvariant() switch
    {
        "sunday" or "sun" => DayOfWeek.Sunday,
        "monday" or "mon" => DayOfWeek.Monday,
        _ => throw new ArgumentException($"The week must start on sunday or monday, but '{value}' was given."),
    };

    private static CultureInfo ParseCulture(string value)
    {
        try
        {
            return CultureInfo.GetCultureInfo(value);
        }
        catch (CultureNotFoundException)
        {
            throw new ArgumentException($"'{value}' is not a culture this machine knows about.");
        }
    }
}
