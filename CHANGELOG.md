# Changelog

All notable changes to this project are recorded here.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this
project uses [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

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
