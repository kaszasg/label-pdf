# labelpdf — Development Summary

Everything needed to continue development of this project.

## Purpose

.NET 10 CLI tool that reads a text file (one caption per line) and generates an
A4 PDF containing 5 × 2 cut-out labels of 10.5 × 6 cm, per the requirements in
`PROMPT.md`.

## Repository layout

The project lives flat in the repository root (git-initialized):

```
C:\Project\@PERSONAL\LABEL
├── LabelPdf.csproj          net10.0, PDFsharp 6.2.4, publish settings
├── Program.cs               CLI: argument parsing, file IO, exit codes
├── LabelSheetGenerator.cs   Layout math + PDF rendering
├── PROMPT.md                Original requirements (Hungarian)
├── labels1.txt              Test input (10 Hungarian captions, UTF-8)
├── output.pdf               Reference output generated from labels1.txt (git-ignored)
├── MANUAL.md                End-user manual
├── SUMMARY.md               This file
├── LICENSE                  MIT
└── .gitignore               bin/obj/publish, *.pdf, *.txt (except labels1.txt)
```

## Build / run / publish

```powershell
dotnet build                                    # debug build
dotnet run -- -i labels1.txt -o out.pdf         # run from source
dotnet publish -c Release -r win-x64            # single-file exe
# → bin\Release\net10.0\win-x64\publish\labelpdf.exe (~11.8 MB)
```

The Release configuration in the csproj enables: `PublishSingleFile`,
`SelfContained`, `PublishTrimmed` (`TrimMode=partial`),
`EnableCompressionInSingleFile`, `InvariantGlobalization`, plus feature switches
(`DebuggerSupport=false`, …) for size. `UseSystemResourceKeys` is deliberately
**not** enabled: it would strip framework exception message texts, so I/O errors
(locked file, missing directory) would print raw resource keys instead of
English sentences. The published exe was pixel-compared against the untrimmed
debug output — identical.

## Architecture

Two files, no external state:

- **`Program.cs`** — hand-rolled argument loop (`-i/--input`, `-o/--output`,
  `-f/--font-size`, `-m/--frame-inset`, `-h/--help`), reads the input as UTF-8,
  trims lines, skips empty ones, then calls the generator. Exit codes: 0
  success, 1 usage error, 2 runtime error. All messages are English.
- **`LabelSheetGenerator.cs`** — static
  `Generate(captions, outputPath, fontSize?, frameInsetMm)`:
  1. Uppercases captions with `ToUpperInvariant()` (works under
     `InvariantGlobalization` — .NET does full Unicode simple case mapping).
  2. Creates ⌈n/10⌉ A4 pages; grid is 2 × 5 cells of 10.5 × 6 cm.
  3. Measures every caption at a reference size of 100 pt with
     `XGraphics.MeasureString` and scales linearly (glyph metrics are linear in
     the em size).
  4. Per caption computes the max fitting size for a single line and for the
     best two-line split (the split minimizing the widest line, tried at every
     word boundary); the caption's max = the larger of the two.
  5. Global font size = min over captions (floored to 0.01 pt, clamped to a
     minimum of 0.01 pt), unless the user forced `--font-size`. Captions that do
     not fit at the final size are reported as stderr warnings.
  6. Renders: thin frame rectangle (0.4 pt) inset by the configurable frame
     inset (default 7 mm) into each cell, then the caption centered. A caption
     renders on one line if it fits at the global size, otherwise on its best
     two-line split.

### Key design decisions

- **PDFsharp 6.2.4 (Core build)** — pure managed, no native dependencies, so
  single-file trimmed publish works. `GlobalFontSettings.UseWindowsFontsUnderWindows
  = true` is required (set in `Generate`); the Core build otherwise has no font
  resolver. Fonts are embedded as subsets → Hungarian Ő/Ű render correctly.
- **Font: Arial Bold** — legible on labels, present on every Windows machine.
- **Vertical grid bleed** — 5 × 6 cm = 30 cm > 29.7 cm (A4 height). The grid is
  vertically centered: cell outer edges bleed 1.5 mm off the top/bottom page
  edges. The cutting frames (default 7 mm inside cells) stay fully on the
  page, and cutting is done along the frames (PROMPT.md line 4), so exact
  label dimensions are preserved.
- **Configurable frame inset** — `-m/--frame-inset <mm>`, default 7 mm,
  validated to 1.5–25 mm: below 1.5 mm the top/bottom row frames would fall
  off the page (because of the grid bleed above); above 25 mm no usable text
  area would remain.
- **Optical vertical centering** — text is drawn baseline-positioned, centering
  the cap-height block (captions are all-uppercase, so descender space would
  otherwise push text visually upward). Accents (Ő, Ú…) extend ~0.9 mm above
  cap height; the pixel bounding box therefore sits ~0.9 mm above geometric
  center — intended typographic behavior.
- **Text area** = frame minus 2.5 mm padding on each side (8.6 × 4.1 cm at the
  default 7 mm inset).
- **Layout constants** live at the top of `LabelSheetGenerator.cs` (points,
  converted via `XUnit.FromCentimeter`).

## Testing status

- `labels1.txt` → 1 page, 10 labels, auto font size **22.16 pt** at the default
  7 mm frame inset; rendered and visually + programmatically verified
  (pypdfium2/pypdf): grid geometry, frame insets, centering (≤ 0.91 mm optical
  offset), uppercase accented glyphs.
- Frame-inset feature verified by content-stream measurement at 5, 7 and
  12.5 mm (frames exactly 95×50, 91×46 and 80×35 mm, in-cell offsets exact,
  all on page, 0.4 pt stroke); `-m 5` exactly reproduces the original 5 mm
  behavior (23.19 pt auto size — regression match). Range validation tested
  (`-m 1`, `-m abc` → usage error; `-m 12,5` comma decimal accepted).
- The multi-agent pass below ran against the earlier 5 mm-fixed build; its
  geometry numbers (9.5 × 5 cm frames) reflect that default. The layout code
  path is unchanged apart from the inset becoming a parameter.
- A multi-agent verification pass (17 agents; every finding confirmed by three
  independent re-verifications) covered: spec compliance (all PROMPT.md
  requirements pass), code review, CLI black-box tests (exit codes, defaults,
  multi-page 11/25/100 captions, forced sizes, invalid args, decimal comma),
  PDF geometry measurement (frames exactly 9.5 × 5 cm at 5 mm insets, 0.4 pt
  stroke, subset-embedded Arial Bold, no glyph outside its frame), and
  robustness fuzzing (28 probes: long words, BOM, CRLF, Latin-2 mojibake,
  CJK/emoji tofu fallback, locked/overwritten outputs — no crashes, correct
  exit-code semantics).
- Defects found by that pass, all fixed and re-verified afterwards:
  1. `-f NaN` bypassed validation → now rejected via `double.IsFinite` check.
  2. Auto font size could floor to exactly 0 pt for a ~50k-char unbreakable
     caption (invisible text) → clamped to 0.01 pt minimum + overflow warning.
  3. `UseSystemResourceKeys=true` made the trimmed exe print raw resource keys
     (`IO_SharingViolation_File`, …) for I/O errors → property removed
     (~0.2 MB size cost), messages are proper English again.
  - Also: overflow warning wording made size-explicit, font-size usage error
    wording corrected, `Console.OutputEncoding = UTF8` set for deterministic
    accented console output.
- Helper scripts used during testing (PDF → PNG rendering, centering
  measurement) live in the session scratchpad, not in the repo; recreate with
  `pypdfium2`/`pillow` if needed.

## Known limitations / possible future work

- Windows-only (Arial is resolved from the Windows font directory). A custom
  `IFontResolver` with a bundled font would enable Linux/macOS.
- Captions never wrap to more than 2 lines (per spec).
- A caption consisting of a single unbreakable word can force a very small
  global font size for the whole sheet (spec: uniform size); the size is
  clamped to 0.01 pt minimum and non-fitting captions produce warnings.
- Characters missing from Arial (emoji, CJK) fail with a graceful error or
  fallback glyph rather than substitution to another font.
