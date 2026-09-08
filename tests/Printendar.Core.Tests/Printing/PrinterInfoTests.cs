using Printendar.Core.Printing;

namespace Printendar.Core.Tests.Printing;

/// <summary>
/// How a printer is named in the print dialog.
/// </summary>
/// <remarks>
/// A record's generated ToString prints its type and every field, which is fine in a debugger
/// and unreadable in a dropdown. A control with no item template falls back to exactly that,
/// so the label is a real property rather than something the UI is trusted to format.
/// </remarks>
public class PrinterInfoTests
{
    [Fact]
    public void The_default_printer_says_so()
    {
        var printer = new PrinterInfo("HP LaserJet Pro M402", IsDefault: true);

        Assert.Equal("HP LaserJet Pro M402 (Default)", printer.DisplayName);
    }

    [Fact]
    public void Any_other_printer_is_just_its_name()
    {
        var printer = new PrinterInfo("Microsoft Print to PDF", IsDefault: false);

        Assert.Equal("Microsoft Print to PDF", printer.DisplayName);
    }

    [Fact]
    public void The_label_never_leaks_the_type_name_or_field_syntax()
    {
        // The actual bug this exists for: the dropdown read
        // "PrinterInfo { Name = HP LaserJet Pro M402, IsDefault = True }".
        var printer = new PrinterInfo("HP LaserJet Pro M402", IsDefault: true);

        Assert.DoesNotContain("PrinterInfo", printer.DisplayName, StringComparison.Ordinal);
        Assert.DoesNotContain("{", printer.DisplayName, StringComparison.Ordinal);
        Assert.DoesNotContain("IsDefault", printer.DisplayName, StringComparison.Ordinal);
    }
}
