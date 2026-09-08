# Printendar

Print a calendar month onto a single landscape sheet of paper.

## Why this exists

Classic Outlook could do this. File > Print, Monthly Style, Page Setup, landscape, one page.

New Outlook and Outlook on the web cannot. Their print path offers no orientation control and no page-fitting control, so a month with real events on it spills onto two pages no matter what you choose. Microsoft's current answer to people asking for it is to switch back to classic Outlook, and classic Outlook is being retired.

Printendar is the missing tool. It reads your calendars, lays a month out against the actual dimensions of the paper, and gives you one page.

## What it does

- One month, one landscape page, guaranteed. The layout is measured against the page before it is drawn, so "it fits" is a property of the output rather than a hope.
- Several calendars merged onto the same grid, color coded, with a printed legend.
- Sensible handling when a day has more events than will fit: shrink the text to a readable floor, then show "+3 more" rather than silently clipping.
- Weekend handling: a full seven-day grid, Saturday and Sunday compressed into one column, or weekdays only.
- Filters for the noise: hide declined, tentative, cancelled or private events, or filter by keyword or category.
- Save as PDF, or print directly.

Calendar sources: Microsoft 365 and Outlook, Google Calendar, and ICS (a file, or a subscription URL).

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

Working: the layout engine, the desktop window with a live preview, PDF export, and reading
calendars from Microsoft 365.

Not done yet:

- Google Calendar and ICS are not built.
- Printing hands the PDF to your system's own viewer and print dialog rather than driving the
  printer directly.
- Only the month view exists. A blank grid, a week, an agenda and a tri-fold are planned.
- A multi-day event repeats as a chip on each day it covers, rather than drawing as one
  spanning bar.
- Builds are tested on Windows. The code carries no platform dependency and is written to run
  on macOS and Linux, but that has not been verified on real machines yet.

## How it fits a month onto one page

Worth knowing, because it explains what the app does when a month is too busy.

The page is measured before it is drawn. The layout works out how much room each day cell has,
measures every event title against the font it will actually be printed in, and searches for
the largest text size at which the whole month still fits. It stops shrinking at the point the
text stops being readable on paper, and anything still left over becomes a "+3 more" note.

So the guarantee is one page, always, and the trade-off it made is reported to you in words
rather than left for you to discover at the printer.

## Building

Requires the .NET 10 SDK.

```
dotnet build
dotnet test
```

Core and its tests carry no platform dependency and are built and tested on Windows, macOS and Linux in CI.

## License

MIT. See [LICENSE](LICENSE).
