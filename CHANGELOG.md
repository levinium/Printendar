# Changelog

All notable changes to this project are recorded here.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this
project uses [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.3.0]

### Removed
- Signing in to Microsoft 365 and Google. Reading a calendar through Graph needs an
  application registration, a registration needs a directory, and every free route to one
  ends at a credit card or an organisation's tenant. That floor is Microsoft's, and it falls
  on everybody: an organisation with strict consent policies needs an administrator's
  approval before anyone there can print, and a personal Outlook.com account has no
  administrator to ask. MSAL, the `Printendar.Sources.Microsoft365` project and the
  four-step setup walkthrough all go with it.

### Added
- Calendar links. Outlook, Google and Apple each publish a calendar as an `.ics` address;
  paste one and Printendar reads it. No account, no consent, nothing revocable.
- A refresh before every print. Both Print and Save as PDF now re-read every calendar,
  then warn, then draw. The request carries `Cache-Control: no-cache` so no proxy answers
  with yesterday's copy.
- Settings, behind a gear in the top right: light, dark, or follow the system. The version
  sits beside it, because the first question on any bug report is which build.
- An application icon, drawn in SkiaSharp rather than traced, and simplified below 32px
  where its inner line lands on less than a pixel.

### Changed
- The manage-calendars window is gone. Everything it offered acts on one calendar in a list
  the sidebar already drew, so it was a second copy of that list you had to open in order to
  act on the first. Renaming, re-reading and removing are now buttons on each card, adding
  is one button under them, and removing asks first.
- One card per calendar. Every source Printendar reads is a single iCalendar, so the list
  nested inside each source only ever held one item, carrying the same name as the heading
  above it: every calendar was drawn twice.

### Fixed
- Every raised surface in the app had been invisible since it was written. Fluent's
  `SystemAltMediumLowColor` and its family are `Color` resources, not brushes, and a `Color`
  handed to a `Background` silently produces nothing at all.
- Adding a feed with an empty address closed the dialog and did nothing, with no message.
  The address is checked on every keystroke, and the check and the conversion are one
  function so they cannot disagree.
- `AADSTS700016` was reported as "an administrator has blocked this application" when it
  means the opposite: no application with that id exists. Moot now, but it was wrong.
- One NUL byte committed into `SettingsStoreTests.cs`, which made git treat a test file as
  binary and its diffs unreadable.

## [0.2.0]

### Added
- Repository scaffolding: solution, central package management, MIT license, CI across
  Windows, macOS and Linux.
- `Printendar.Core`: the print engine. Paper and page spec, text measurement over an
  embedded Noto Sans, greedy word wrap with ellipsis, month grid geometry, the scene graph
  and its validator, the event chip composer, the fit search, Skia rendering, raster output
  and single-page PDF export.
- `Printendar.Cli`: a headless harness. `printendar render --month 2026-03 --demo` writes a
  month to a single-page landscape PDF, with `--png` for a look at it without a viewer.
- Month grid features: several calendars merged with colour coding and a printed legend,
  three weekend modes (full, compressed, weekdays only), Sunday or Monday week start,
  adjacent day handling, compact start times, and "+N more" overflow markers.

### Notes
- The readable floor is derived from the small-type warning threshold rather than written as
  a separate number. They had drifted: a hand-picked floor put event text at 5.4 point, so
  the layout hid events to protect readability and then warned that the result was
  unreadable anyway.
