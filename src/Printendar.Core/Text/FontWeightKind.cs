namespace Printendar.Core.Text;

/// <summary>
/// The weights the layout uses.
/// </summary>
/// <remarks>
/// Deliberately two, not a full weight axis. A printed calendar needs body text and one
/// emphasis face (day numbers, weekday headers, the month title); anything more would embed
/// another 400 KB per weight for no legibility gain at 7 point.
/// </remarks>
public enum FontWeightKind
{
    Regular,
    SemiBold,
}
