# Printendar

Print a calendar month onto a single landscape sheet of paper.

[![CI](https://github.com/levinium/Printendar/actions/workflows/ci.yml/badge.svg)](https://github.com/levinium/Printendar/actions/workflows/ci.yml)

## Why this exists

Classic Outlook could do this. File > Print, Monthly Style, Page Setup, landscape, one page.

New Outlook and Outlook on the web cannot. Their print path offers no orientation control and no page-fitting control, so a month with real events on it spills onto two pages no matter what you choose. Microsoft's current answer to people asking for it is to switch back to classic Outlook, and classic Outlook is being retired.

Printendar is the missing tool. It reads your calendars, lays a month out against the actual dimensions of the paper, and gives you one page.

## What it does

- One month, one landscape page, guaranteed. The layout is measured against the page before it is drawn, so "it fits" is a property of the output rather than a hope.
- Several calendars merged onto the same grid, color coded, with a printed legend.
- Sensible handling when a day has more events than will fit: shrink the text to a readable floor, then show "+3 more" rather than silently clipping.
- Weekend handling: a full seven-day grid, Saturday and Sunday compressed into one column, or weekdays only.
- Save as PDF, or send it straight to your system's print dialog, already set to landscape.

Calendar sources today: **Microsoft 365 and Outlook.com**, and **`.ics` calendar files** from
anywhere. Google Calendar and `.ics` subscription URLs are planned; see Status below for what
is and is not built.

## Opening a calendar file

The route that needs nothing at all: no account, no sign-in, no approval from anyone.

Export or download a calendar from wherever it lives, as a `.ics` file, then **Open a calendar
file** in Printendar. Outlook, Google Calendar, Apple Calendar and most other calendar programs
can produce one.

Recurring events, all-day events and exceptions are all handled. The file is re-read each time,
so re-exporting and printing again picks up the changes.

## Connecting Microsoft 365

Sign in once and Printendar remembers you. It asks only for read access to your calendars,
and the token is stored encrypted by your operating system: DPAPI on Windows, Keychain on
macOS, libsecret on Linux. Nothing is sent anywhere except to Microsoft.

Printendar's registration accepts work and school accounts from any organisation as well as
personal Microsoft accounts. It is not tied to any particular organisation.

**If your organisation has switched off user consent for third party applications**, an
administrator there consents once, at:

```
https://login.microsoftonline.com/common/adminconsent?client_id=<the client id>
```

**If your organisation would rather not consent to this application at all**, register your
own and point Printendar at it:

```
scripts/New-PrintendarAppRegistration.ps1     # creates the registration
$env:PRINTENDAR_MS_CLIENT_ID = '<your client id>'
```

The client id is not a secret. Printendar is a public client application: it holds no secret,
and the identity platform treats its client id as public by design.

## Status

Early development, and honest about it.

Working: the layout engine, the desktop window with a live preview, PDF export, reading
calendars from Microsoft 365, and opening `.ics` files.

Not done yet:

- Google Calendar is not built, and neither are `.ics` subscription URLs (only files).
- Printing hands the PDF to your system's own viewer and print dialog rather than driving the
  printer directly.
- Only the month view exists. A blank grid, a week, an agenda and a tri-fold are planned.
- A multi-day event repeats as a chip on each day it covers, rather than drawing as one
  spanning bar.
- The desktop window has only been run on Windows so far. The engine underneath is built and
  tested on all three platforms by CI, but nobody has yet opened the app itself on a Mac.

## Building

Requires the .NET 10 SDK.

```
dotnet build
dotnet test
```

CI builds and runs the whole test suite on Windows, macOS and Linux. The layout tests measure
real glyph advances rather than a stub, and they assert exact geometry, so a page that fits on
one platform is a page that fits on all of them. That is why the font is embedded rather than
taken from the system.

## How it fits a month onto one page

Worth knowing, because it explains what the app does when a month is too busy.

The page is measured before it is drawn. The layout works out how much room each day cell has,
measures every event title against the font it will actually be printed in, and searches for
the largest text size at which the whole month still fits. It stops shrinking at the point the
text stops being readable on paper, and anything still left over becomes a "+3 more" note.

So the guarantee is one page, always, and the trade-off it made is reported to you in words
rather than left for you to discover at the printer.


## License

MIT. See [LICENSE](LICENSE).
