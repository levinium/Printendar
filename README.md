<img src="assets/printendar.svg" width="72" alt="">

# Printendar

**Print a calendar month on a single landscape sheet, the way classic Outlook could.**

A desktop app that lays a month out against the real dimensions of the paper, so it fits on
one page instead of spilling onto two.

[![Latest release](https://img.shields.io/github/v/release/levinium/Printendar?label=download&color=2B5CE6)](https://github.com/levinium/Printendar/releases/latest)
[![Downloads](https://img.shields.io/github/downloads/levinium/Printendar/total?color=2B5CE6)](https://github.com/levinium/Printendar/releases)
![Platform](https://img.shields.io/badge/platform-Windows%20x64-555)
[![CI](https://github.com/levinium/Printendar/actions/workflows/ci.yml/badge.svg)](https://github.com/levinium/Printendar/actions/workflows/ci.yml)

![Printendar](docs/screenshots/app-window.png)

## Download

**[Download Printendar-v0.3.0-win-x64.zip](https://github.com/levinium/Printendar/releases/download/v0.3.0/Printendar-v0.3.0-win-x64.zip)** (61 MB)

Unzip it anywhere and run `Printendar.exe`. That is the whole installation. No setup step, no
admin prompt, no registry entry: the .NET runtime it needs is inside the folder. Delete the
folder and it is gone. You need 64-bit Windows and nothing else.

Windows SmartScreen will warn that the app is unsigned. Choose **More info** then **Run
anyway**, or read the source and build it yourself with `./publish.ps1`.

This is an early release. Read [what works and what does not](#status) first.

## Why this exists

Classic Outlook could do this. File > Print, Monthly Style, Page Setup, landscape, one page.

New Outlook and Outlook on the web cannot. Their print path offers no orientation control and
no page-fitting control, so a month with real events on it spills onto two pages whatever you
choose. Microsoft's current answer to people asking for it is to switch back to classic
Outlook, and classic Outlook is being retired.

## What it does

### It measures the page before it draws

This is the part that makes the guarantee real. Printendar works out how much room each day
cell has, measures every event title in the font it will actually be printed in, and searches
for the largest text size at which the whole month still fits.

It stops shrinking at the point the text stops being readable on paper. Anything still left
over becomes a "+3 more" note rather than being silently clipped. Either way you get one page,
and the trade-off it made is reported to you in words rather than discovered at the printer.

![A month on one landscape page](docs/screenshots/month-letter-landscape.png)

### It merges several calendars onto one grid

Each calendar gets its own colour and a printed legend. Colours are chosen to stay
distinguishable for the common forms of colour blindness, and each one carries a coloured bar
rather than a filled block so the page still reads when printed in black and white.

### It can squeeze the weekend

Saturday and Sunday share one narrower column, stacked, which buys the five days that usually
carry the events about a fifth more width. There is also a weekdays-only mode.

![Weekend squeezed into one column](docs/screenshots/month-compressed-weekend.png)

### It prints a blank month too

A plain grid with the right dates and no events, which is another thing people specifically
complain about losing from new Outlook.

![A blank month grid](docs/screenshots/month-blank.png)

### The preview is the page

The preview is not an approximation. It draws the same laid-out page object the PDF exporter
writes and the printer receives, through the same renderer, so what is on screen is what comes
out. There is no second rendering path to drift.

That is also why Printendar shows its own print dialog on Windows rather than the system one.
Windows 11 substitutes its modern print dialog for classic printing calls and cannot render a
preview for them, so it reports "This app doesn't support print preview" over a dialog that
otherwise works. Printendar has the real page already, and shows it, along with the printer,
the copies, and a warning if your printer's unprintable margin will clip the border.

## Connecting a calendar

Printendar signs in to nothing. There is no account to connect, no password to give it, no
consent screen and nobody to ask for permission. It reads iCalendar, which is the format every
calendar already publishes, and that is the whole of it.

Two ways in, and they are the same thing arriving differently.

### A calendar link

Outlook, Google Calendar and Apple Calendar can each publish a calendar as a link ending in
`.ics`, sometimes beginning `webcal://`. Copy it, press **Calendar link**, paste.

From then on it is automatic. Printendar re-reads every link immediately before it prints, so
the sheet is made from the calendar as it stands at that moment rather than from whatever was
fetched when you opened the app.

**Treat a published link as a password.** It usually contains a long random string, and anyone
holding it can read that calendar without signing in to anything. That is what makes this work
without an account, and it is also the reason not to paste one into a group chat.

Some organisations switch calendar publishing off. If yours has, an administrator can re-enable
it — in Microsoft 365 that lives in the Exchange admin center under **Organization > Sharing**.
Until then, use a file.

### A calendar file

Export a calendar as `.ics` and press **File on this computer**. Recurring events, all-day
events and exceptions are all handled.

A file is a snapshot: it is exactly current at the moment you exported it and never changes
afterwards, so this route trades the automatic refresh for not depending on the publisher at
all. Export again when you want newer.

### Why not sign in to Outlook or Google directly?

It was built, and then removed. Reading a calendar through Microsoft Graph or the Google
Calendar API requires an application registration, which requires a directory or a verified
developer account, which means every person publishing their own build of Printendar needs
one, and every organisation with strict consent policies needs an administrator to approve it
before anyone can print anything.

An `.ics` link needs none of that and cannot be revoked out from under the app. The cost is
that a published link is refreshed on the publisher's schedule rather than instantly, which
for a sheet of paper covering a month is a trade worth making.

## Status

Early, and honest about it.

**Works:** the layout engine, the desktop window with a live preview, printing on Windows,
PDF export, and reading `.ics` files and published `.ics` links. Printing has been confirmed
on real hardware.

The window follows your system's light or dark setting, and the gear in the top right can
pin it to one or the other. That changes the window only: the printed page and the PDF are
always black on white.

**Not built yet:**

- Printing on macOS and Linux. Those still hand the PDF to your system's viewer, because
  Avalonia has no printing of its own, and the viewer's own scale setting then decides the
  result. Windows drives the printer directly.
- Only the month view. A week, an agenda and a tri-fold are planned.
- A multi-day event repeats as a chip on each day it covers, rather than drawing as one
  spanning bar.
- The window has only been run on Windows. The engine underneath is built and tested on
  Windows, macOS and Linux by CI, but nobody has opened the app itself on a Mac.
- Nothing reminds you that a published link has gone stale at the publisher's end. Printendar
  fetches the current copy before every print, but how current the publisher keeps that copy
  is theirs to decide, and typically runs behind the live calendar.

## Before you print a lot of them

Print one and look at it. The layout is measured against the paper, but your printer has its
own unprintable margin near the edges, and only a real sheet tells you whether the default
0.4 inch margin clears it.

## Building from source

Needs the .NET 10 SDK.

```powershell
dotnet test          # 284 tests, all three platforms in CI
./publish.ps1        # builds dist\Printendar-v0.3.0-win-x64.zip
```

`publish.ps1` runs the tests first and refuses to publish if any fail.

There is also a headless harness, useful for looking at the output without the window:

```powershell
dotnet run --project src/Printendar.Cli -- render --month 2026-03 --demo --out march.pdf --png march.png
```

## How it is put together

```
src/Printendar.Core/                 paper, layout, the scene graph, rendering, PDF export
src/Printendar.Sources.Ics/          iCalendar files and published feeds
src/Printendar.App/                  the Avalonia window
src/Printendar.Cli/                  headless harness
tests/                               run on Windows, macOS and Linux
```

### The design that matters

Layout is arithmetic over measured text. Rendering is a switch statement. One class touches a
pixel.

Everything that can fail, meaning "does this fit on one page", happens in pure code a unit test
can interrogate without producing a bitmap. That is why this uses Skia directly rather than
HTML or XAML: asking a general-purpose layout engine not to paginate is asking for a guarantee
it does not offer, and you find out at the printer. Measuring glyph runs and packing them into
a fixed content box turns the requirement into an assertion.

Layout produces an immutable `ScenePage`: fully resolved nodes in page coordinates, every
string a single line that already fits. The preview draws that object, the PDF exporter writes
that object, and the printer gets that object. There is no second layout pass to drift.

### The font is embedded, deliberately

Noto Sans ships inside `Printendar.Core` and is used instead of a system font. Glyph advances
decide where a title wraps and therefore how tall a cell must be, so a system font would make
the same month fit on one machine and spill on another. CI runs the layout tests on Windows,
macOS and Linux and they assert exact geometry, which is what turns that from an intention into
a measured fact.

### Why the tests are shaped the way they are

The invariant suite lays out every month across four paper sizes, both orientations, both week
starts and all three weekend modes, and asserts that no drawn element falls outside the sheet.
There is a deliberately pathological case too: 500 events in one month with 400-character
unbroken subjects at 0.15 inch margins. It still produces one page.

`FitIsMonotoneInScale` walks the whole type-size ladder. The fit search is a binary search, and
that is only valid while shrinking the text cannot increase the number of wrapped lines. It
stops being valid the moment someone expresses a padding in fixed points while the fonts scale,
so this test exists to catch exactly that.

`ArchitectureTests` parses `Printendar.Core.csproj` as XML and asserts its package references
are exactly SkiaSharp. Asserting on `TargetFrameworkAttribute` would be true by construction
and prove nothing, and `GetReferencedAssemblies` can pass vacuously because the CLR omits
references the JIT never needed.

## Third-party components

| Package | Licence | Used for |
| --- | --- | --- |
| SkiaSharp | MIT | measuring text, drawing, PDF export |
| Avalonia | MIT | the desktop window |
| Ical.Net | MIT | reading `.ics` files and expanding recurrence |
| Noto Sans | SIL OFL 1.1 | the embedded layout font |

## Licence

MIT. See [LICENSE](LICENSE).
