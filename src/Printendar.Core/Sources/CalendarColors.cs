using System.Globalization;
using SkiaSharp;

namespace Printendar.Core.Sources;

/// <summary>
/// The colours calendars are printed in.
/// </summary>
/// <remarks>
/// Assigned by Printendar rather than read from the provider. Graph exposes category names but
/// not the colour values Outlook actually shows, and Google's palette is different again, so
/// reading them would give an inconsistent set that still had to be remapped.
///
/// Chosen to stay distinguishable for the common forms of colour blindness, and to separate
/// into different greys when printed in black and white, which is how most of these end up on
/// a wall. A user may pick something outside this set; these are the ones we hand out.
/// </remarks>
public static class CalendarPalette
{
    public static IReadOnlyList<SKColor> Colors { get; } =
    [
        new(0x1F, 0x77, 0xB4),
        new(0xD6, 0x27, 0x28),
        new(0x2C, 0xA0, 0x2C),
        new(0x94, 0x67, 0xBD),
        new(0xFF, 0x7F, 0x0E),
        new(0x8C, 0x56, 0x4B),
        new(0x17, 0xBE, 0xCF),
        new(0x7F, 0x7F, 0x7F),
    ];

    /// <summary>Assigns a colour by position, wrapping once the palette runs out.</summary>
    public static SKColor At(int index) => Colors[((index % Colors.Count) + Colors.Count) % Colors.Count];

    /// <summary>Writes a colour as #RRGGBB, which is what the settings file holds.</summary>
    public static string ToHex(SKColor color) =>
        string.Create(CultureInfo.InvariantCulture, $"#{color.Red:X2}{color.Green:X2}{color.Blue:X2}");

    /// <summary>
    /// Reads #RRGGBB back, or null when it is not one.
    /// </summary>
    /// <remarks>
    /// Null rather than an exception because the only way a bad value gets here is a hand-edited
    /// settings file, and one mistyped colour should cost that calendar its choice rather than
    /// stop the program opening.
    /// </remarks>
    public static SKColor? FromHex(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var digits = text.Trim().TrimStart('#');

        if (digits.Length != 6 ||
            !uint.TryParse(digits, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
        {
            return null;
        }

        return new SKColor(
            (byte)((value >> 16) & 0xFF),
            (byte)((value >> 8) & 0xFF),
            (byte)(value & 0xFF));
    }
}

/// <summary>
/// Works out what colour each calendar prints in.
/// </summary>
/// <remarks>
/// A calendar's colour belongs to the calendar and stays with it. Colours used to be handed out
/// by position across every source, which meant removing an account, or somebody adding a
/// calendar in Outlook, silently recoloured everything after it: a month printed one week no
/// longer matched the one printed the next, and the wall chart quietly stopped meaning what the
/// legend said.
///
/// So a colour is chosen once, when a calendar is first seen, and remembered from then on. A
/// colour the user picked is stored the same way, which is why choosing one and being given one
/// are the same mechanism rather than two that have to agree.
/// </remarks>
public static class CalendarColorAssignment
{
    /// <param name="calendarIds">The calendars in one source, in the order they are shown.</param>
    /// <param name="remembered">Colours already settled, by calendar id, as #RRGGBB.</param>
    /// <param name="inUseElsewhere">Colours taken by calendars in the other sources.</param>
    /// <returns>A colour for every id given, remembered ones unchanged.</returns>
    public static IReadOnlyDictionary<string, SKColor> Assign(
        IReadOnlyList<string> calendarIds,
        IReadOnlyDictionary<string, string> remembered,
        IReadOnlyCollection<SKColor> inUseElsewhere)
    {
        ArgumentNullException.ThrowIfNull(calendarIds);
        ArgumentNullException.ThrowIfNull(remembered);
        ArgumentNullException.ThrowIfNull(inUseElsewhere);

        var assigned = new Dictionary<string, SKColor>(StringComparer.Ordinal);
        var taken = new HashSet<SKColor>(inUseElsewhere);

        // Settled colours are claimed before anything new is handed out, so a new calendar in
        // the same source cannot take a colour one of its neighbours already keeps.
        foreach (var id in calendarIds)
        {
            if (remembered.TryGetValue(id, out var stored) && CalendarPalette.FromHex(stored) is { } color)
            {
                assigned[id] = color;
                taken.Add(color);
            }
        }

        var fallback = 0;

        foreach (var id in calendarIds)
        {
            if (assigned.ContainsKey(id))
            {
                continue;
            }

            var free = CalendarPalette.Colors.FirstOrDefault(c => !taken.Contains(c));

            // FirstOrDefault on a struct gives a transparent black when everything is taken,
            // which is not a colour anybody wants on paper. Past that point repeats are the
            // honest outcome: better two calendars sharing a colour than one drawn invisibly.
            var color = taken.Count >= CalendarPalette.Colors.Count
                ? CalendarPalette.At(fallback++)
                : free;

            assigned[id] = color;
            taken.Add(color);
        }

        return assigned;
    }
}
