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

## Status

Early development. See `docs/` for the design.

## Building

Requires the .NET 10 SDK.

```
dotnet build
dotnet test
```

Core and its tests carry no platform dependency and are built and tested on Windows, macOS and Linux in CI.

## License

MIT. See [LICENSE](LICENSE).
