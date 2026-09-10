using Printendar.Core.Paper;
using Printendar.Core.Printing;
using Printendar.Printing.Cups;

namespace Printendar.Core.Tests.Printing;

/// <summary>
/// Talking to CUPS, which is how macOS and Linux print.
/// </summary>
/// <remarks>
/// Neither the command nor the parser touches a printer, so both can be pinned exactly. That
/// matters more here than usual: nobody working on this can watch a sheet come out of a Linux
/// printer, so the only thing standing between a change and a wrong page is whether the
/// arguments are still the ones that were reasoned about.
/// </remarks>
public class CupsCommandTests
{
    private static readonly PrintJob Job = new(
        PrinterName: "Office_Laser",
        Copies: 1,
        Title: "September 2026",
        Paper: PaperSizes.Letter,
        Orientation: Orientation.Landscape,
        PdfPath: "/tmp/september.pdf");

    [Fact]
    public void The_printer_the_user_chose_is_the_one_it_prints_to()
    {
        Assert.Equal(["-d", "Office_Laser"], CupsCommand.Arguments(Job).Take(2));
    }

    [Fact]
    public void Copies_are_passed_through()
    {
        Assert.Contains("-n", CupsCommand.Arguments(Job with { Copies = 3 }));
        Assert.Contains("3", CupsCommand.Arguments(Job with { Copies = 3 }));
    }

    [Fact]
    public void The_job_is_named_so_the_print_queue_is_readable()
    {
        var args = CupsCommand.Arguments(Job);
        var title = args.SkipWhile(a => a != "-t").Skip(1).FirstOrDefault();

        Assert.Equal("September 2026", title);
    }

    [Fact]
    public void The_file_is_the_last_argument()
    {
        // lp takes the file positionally after its options. Anywhere else and it reads as the
        // value of whichever option came before it.
        Assert.Equal("/tmp/september.pdf", CupsCommand.Arguments(Job)[^1]);
    }

    [Theory]
    [InlineData("letter", "Letter")]
    [InlineData("legal", "Legal")]
    [InlineData("tabloid", "Tabloid")]
    [InlineData("a4", "A4")]
    [InlineData("a3", "A3")]
    public void Every_paper_size_Printendar_offers_has_a_name_CUPS_knows(string id, string expected)
    {
        Assert.True(PaperSizes.TryGetById(id, out var paper));
        Assert.Equal(expected, CupsCommand.MediaName(paper));

        var args = CupsCommand.Arguments(Job with { Paper = paper });

        Assert.Contains($"media={expected}", args);
    }

    [Fact]
    public void It_never_asks_CUPS_to_rotate_the_page()
    {
        // The single most dangerous option here. The PDF is already landscape: its page is
        // wider than it is tall. Passing "landscape" as well turns it a second time, and the
        // month comes out sideways on a portrait sheet with the edges cut off.
        var args = CupsCommand.Arguments(Job with { Orientation = Orientation.Landscape });

        Assert.DoesNotContain(args, a => a.Contains("landscape", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(args, a => a.Contains("orientation", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void It_never_lets_CUPS_scale_the_page()
    {
        // The whole product is a layout measured against the paper. Anything that scales it
        // destroys the one guarantee Printendar makes, and some cups-filters builds fit to the
        // page unless told not to.
        var args = CupsCommand.Arguments(Job);

        Assert.Contains("fit-to-page=false", args);
        Assert.Contains("scaling=100", args);
    }

    [Fact]
    public void A_printer_name_with_a_space_stays_one_argument()
    {
        // Arguments are passed as a list, never as a command line, so nothing here needs
        // quoting and a name with a space cannot split into two.
        var args = CupsCommand.Arguments(Job with { PrinterName = "Front Desk HP" });

        Assert.Contains("Front Desk HP", args);
    }
}

/// <summary>
/// Reading the printer list out of lpstat.
/// </summary>
/// <remarks>
/// Fixture text rather than a live call, so this runs on the Windows machine most of the work
/// happens on, and describes what the code does with each answer rather than what one machine
/// happened to have installed.
/// </remarks>
public class CupsPrinterListTests
{
    private const string Typical = """
        printer Office_Laser is idle.  enabled since Mon 08 Sep 2026 09:14:02 AM EDT
        printer Front_Desk is idle.  enabled since Mon 08 Sep 2026 09:14:02 AM EDT
        printer Warehouse_Dot_Matrix disabled since Tue 09 Sep 2026 11:02:41 AM EDT
        """;

    [Fact]
    public void Every_printer_is_listed()
    {
        var printers = CupsPrinters.Parse(Typical, defaultPrinter: "Front_Desk");

        Assert.Equal(
            ["Front_Desk", "Office_Laser", "Warehouse_Dot_Matrix"],
            printers.Select(p => p.Name));
    }

    [Fact]
    public void The_default_printer_comes_first_and_says_so()
    {
        var printers = CupsPrinters.Parse(Typical, defaultPrinter: "Front_Desk");

        Assert.True(printers[0].IsDefault);
        Assert.Equal("Front_Desk", printers[0].Name);
        Assert.DoesNotContain(printers.Skip(1), p => p.IsDefault);
    }

    [Fact]
    public void A_disabled_printer_is_still_offered()
    {
        // Disabled usually means out of paper or paused, and the job queues until somebody
        // deals with it. Hiding the printer would leave them wondering where it went.
        var printers = CupsPrinters.Parse(Typical, defaultPrinter: null);

        Assert.Contains(printers, p => p.Name == "Warehouse_Dot_Matrix");
    }

    [Fact]
    public void With_no_default_set_nothing_is_marked_as_one()
    {
        var printers = CupsPrinters.Parse(Typical, defaultPrinter: null);

        Assert.DoesNotContain(printers, p => p.IsDefault);
    }

    [Fact]
    public void A_default_that_is_not_in_the_list_does_not_invent_a_printer()
    {
        var printers = CupsPrinters.Parse(Typical, defaultPrinter: "Gone_Away");

        Assert.Equal(3, printers.Count);
        Assert.DoesNotContain(printers, p => p.Name == "Gone_Away");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("lpstat: No destinations added.")]
    public void No_printers_reads_back_as_no_printers(string output)
    {
        Assert.Empty(CupsPrinters.Parse(output, defaultPrinter: null));
    }
}
