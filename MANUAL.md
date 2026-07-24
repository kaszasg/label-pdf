# labelpdf — User Manual

`labelpdf` is a command-line tool that turns a plain text file into a printable
A4 PDF sheet of cut-out labels.

## What it produces

- One or more **A4 portrait** pages.
- Each page holds **10 labels** in a **2 × 5 grid**; every label is **10.5 × 6 cm**.
- Each label has a **thin cutting frame drawn 7 mm inside the label edges**
  (the cut-out piece is therefore 9.1 × 4.6 cm). Cut along the frames. The
  frame distance is adjustable with `-m/--frame-inset`.
- Every caption is printed **in uppercase, bold, centered**, using the
  **largest font size at which every caption still fits** — all labels share
  the same font size, so the sheet looks uniform.
- A caption is automatically **wrapped to two lines** when that allows a larger
  common font size (wrapping happens at word boundaries only).

Note: 5 rows × 6 cm is 3 mm taller than an A4 page, so the outermost label
edges extend 1.5 mm past the top and bottom of the page. The cutting frames are
always fully on the page; this does not affect cutting.

## Requirements

- Windows x64. The executable is self-contained — **no .NET installation is
  required**.
- The **Arial** font must be installed (it is part of every standard Windows
  installation; the font is embedded into the PDF as a subset).

## Usage

```
labelpdf [options]

Options:
  -i, --input <file>       Input text file, one caption per line (default: input.txt)
  -o, --output <file>      Output PDF file (default: output.pdf)
  -f, --font-size <pts>    Fixed font size in points (default: automatic)
  -m, --frame-inset <mm>   Distance of the cutting frame from the label edges
                           in millimeters, 1.5 to 25 (default: 7)
  -h, --help               Show help
```

### Input file format

- Plain text, **UTF-8** encoding (with or without BOM). Accented characters —
  including Hungarian Ő and Ű — are fully supported.
- **One caption per line.** Leading/trailing whitespace is trimmed; empty lines
  are ignored.
- Captions are converted to uppercase automatically — you may write them in
  any case.
- If the file contains more than 10 captions, additional A4 pages are added
  (10 labels per page).

### Examples

Generate `output.pdf` from `input.txt` in the current directory:

```
labelpdf
```

Explicit input and output files:

```
labelpdf -i labels1.txt -o valves.pdf
```

Force a fixed font size of 30 points (instead of automatic sizing):

```
labelpdf -i labels1.txt -o valves.pdf -f 30
```

If a forced font size is too large for some caption, the PDF is still created
and a warning is printed for each caption that does not fit.

Move the cutting frames closer to the label edges (5 mm instead of the default
7 mm), leaving more room for the text:

```
labelpdf -i labels1.txt -o valves.pdf -m 5
```

A larger frame inset shrinks the text area, so the automatic font size gets
smaller; a smaller inset leaves less tolerance for printer misalignment when
cutting. The minimum is 1.5 mm — below that the frames of the top and bottom
label rows would fall off the page.

### Output messages

On success the tool prints a one-line summary, for example:

```
Created 'output.pdf': 10 label(s) on 1 page(s), font size 23.19 pt.
```

### Exit codes

| Code | Meaning                                                        |
|------|----------------------------------------------------------------|
| 0    | Success (possibly with overflow warnings on stderr).           |
| 1    | Usage error — invalid or unknown command-line argument.        |
| 2    | Runtime error — input file missing/unreadable, empty input, or the PDF could not be written. |

## Printing and cutting

1. Print the PDF at **100% scale** (“Actual size” — do **not** use
   “Fit to page”, it would shrink the labels).
2. Cut along the thin frame lines. The zone between the frame and the label
   edge (7 mm by default) is a tolerance margin, so small printer
   misalignments do not matter.

## Troubleshooting

| Symptom | Cause / solution |
|---------|------------------|
| `Error: input file not found: input.txt` | Run the tool in the directory that contains your input file, or pass `-i <file>`. |
| `Error: failed to create PDF ...` | The output file is probably open in a PDF viewer — close it and run again. Also check that the output directory exists. |
| Accented letters look wrong | Save the input file as UTF-8. |
| Labels print at the wrong size | Set the print dialog to 100% / “Actual size”. |
| `Warning: caption does not fit at font size ...` | You forced a too-large `--font-size` — lower it or omit the option for automatic sizing. (In automatic mode this can only happen with an extremely long unbreakable caption.) |
