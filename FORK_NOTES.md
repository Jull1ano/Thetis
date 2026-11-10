# Jull1ano fork of Thetis

Based on Thetis v2.10.3.15 (ramdor/Thetis). Branches:
- `smooth-pan-drag`: AltSDR-style spectrum handling (pan, zoom, retunes).
- `native-scaling`: `smooth-pan-drag` + native high-DPI (crisp at 200 % on a 4K monitor).

## What changed

**Drag anywhere to pan** (RX1, CTUN on, click tune mode off) — `Console/SmoothPan.cs`
- Left-press on the spectrum or the waterfall outside the RX passband and move 3 px or more: the
  spectrum follows the mouse 1:1, the VFO keeps its frequency. The top drag strip does the same.
- Released before 3 px: a click, tunes the VFO to that point (as before).
- Inside the passband: drags the VFO (as before). Shift + passband: moves the filter (as before).
- The view first moves inside the DDC span (display only: no radio change, no audio change). When its
  edge leaves the span, the DDC is re-centred on the view, at most every 25 ms, exact final value on release.
- No inertia: the view stops where the mouse lets go.

**Ctrl + mouse wheel zoom** around the cursor: the frequency under the cursor stays put; one notch
changes the span by 2^(1/3) (about 26 %). Display only, never retunes.

**Spectrum keeps its shape** — WDSP `analyzer.c` (smooth pan / retune)
- Pan and zoom no longer restart the analyzer (before: one full FFT refill per step, about 0.7 s at
  262144 points / 384 kHz, so the display froze while dragging).
- The averaged trace is kept in absolute frequency: pans, zooms and DDC retunes shift or rescale it
  instead of smearing old data at the new position. Areas without data show the noise floor until
  real data arrives.
- Each DDC retune is marked in the sample stream; samples taken before the retune inside an FFT window
  are frequency-rotated onto the new LO, so frames stay valid while the DDC moves.
- Waterfall rows carry the view centre, so old rows move with a pan too.

**Audio during a pan retune** — WDSP `shift.c`
- Before: the WDSP shift changed at once, the DDC a few ms later, so each retune played a few ms at the
  wrong frequency (a click / chirp per mouse move).
- Now the shift changes at the input sample where the new DDC frequency is expected
  (`SetRXAShiftFreqAt`), with a short soft mute (default ±1.5 ms + 1.5 ms ramps) around it to cover the
  network timing uncertainty.

## Native high-DPI (branch `native-scaling`)

Upstream Thetis is DPI-unaware: at 200 % Windows draws it at 1920x1080 and stretches the bitmap, so
text is soft and the spectrum line is a blurred staircase. The fork runs system DPI aware when Windows
scaling is above 100 % (`Console/HighDpi.cs`, entry point `Console/Program.cs`):

- WinForms scales the layout; the console's own resize code, the skins (positions in 96 DPI units),
  status bar widths, mouse distances, the VFO digit boxes and Setup's window size were fixed for it.
- Direct2D spectrum / waterfall: fonts scaled; hairlines (grid, cursor) are one device pixel; wider lines
  keep their designed weight; the waterfall adds 2 pixel rows per update at 200 % so it covers the same
  time.
- Smooth spectrum line (Setup > Appearance > RX Display > Smooth, next to Line Width; on by default):
  one anti-aliased polyline through the column centres at sub-pixel heights with round joins, the way
  AltSDR, NereusSDR and AetherSDR draw it. Line Width is then in screen points (1.0 = 2 device pixels
  at 200 %, the same as AltSDR's 1 px stroke; 0.1 steps in between look right too, since the edge is
  anti-aliased). Upstream drew each segment on whole pixel rows, so 1 looked thin and jagged and 2
  heavy. It follows its own anti-aliasing, not Setup > Display > Anti-aliased. Off: the upstream line
  (width in device pixels). `THETIS_SMOOTH_TRACE=0/1` overrides the saved choice. No measurable CPU
  cost (about 66 % of one core either way at 4K, 60 fps). Measured on screenshots, the line is as wide
  as AltSDR's at 1; Thetis' trace looks bolder only because its noise zig-zags taller.
- FFT Smooth (Setup > Appearance > RX Display, in the row above Active Peak; 0 = off, 0.1 steps up to 16),
  after AetherSDR's: each drawn point becomes a mean of its neighbours within that many screen points on
  each side, in linear power, then back to dB (so the noise floor stays where it is). Unlike AetherSDR's
  flat window it uses a triangular one (a wider flat window is not always smoother: 1.0 came out rougher
  than 0.8), and up to 0.5 it blends the raw trace with the 0.5 result in dB, since linear averaging fills
  the deep noise dips at once. So the effect grows steadily from 0: on test noise the roughness goes 6.0,
  5.0, 4.1, 3.2, 2.4, 1.9 dB for 0 ... 0.5 at 200 % and keeps falling. Display only (trace, fill, peak
  markers): the noise floor estimate and the waterfall are unchanged. Saved as `hidpi_fft_smooth.txt`.
- Slider handles (Pan, Zoom, AF, AGC Gain, Drive ...): the skin's handle images are 96 DPI pixels; drawn
  scaled like the original shows them (9 x 20 -> 18 x 40 at 200 %, nearest pixel, so crisp).
- Base text size: the original at 200 % is drawn at 96 DPI and stretched by Windows; the stretch blurs the
  text, which then looks heavier and about 2 px taller than the same font drawn sharp (measured: 18 vs
  16 px, 16-20 % more ink). Default here is 0.75 pt larger on the 8.25 pt base (`HighDpi.TEXT_BASE_BOOST`,
  `THETIS_TEXT_BOOST=<pt>` to compare); +1 / +2 add to it. Text in a fixed control still only grows as far as
  it fits.
- Windows WinForms does not scale (no AutoScaleMode: Equalizer, XVTR, Filter, CW edit, input box, progress,
  splash; or the .NET 1.x AutoScaleBaseSize: Diversity, CAT tester, CWX) are scaled right after
  InitializeComponent, before a saved size is restored. Diversity used to open bigger on every reopen: its
  load-time scaling came after the restore (the original grows too, by 1.2 per reopen at 100 %).
- Font-scaled windows (AutoScaleMode.Font, most dialogs) lay out at their font's height ratio, about 2.17
  wide at 200 % instead of 2: 8 % bigger than the original, with the text relatively smaller in them. When
  such a window opens its layout is brought to exactly the DPI scale (bounds only), and the text pass no
  longer rescales a window with its text. Windows are prepared from a WH_CBT hook at activation, before
  their first paint (the Application.Idle watcher stays as a fallback). Measured on the sweep: Memory,
  PureSignal, About, Finder, Path Illustrator open within 6 px of the original's size.
- Saved window sizes are restored and saved in the window's own scale (`HighDpi.FormScale`), and never
  larger than the screen.
- A fixed-size label whose text changes later ("AGC Gain: 120") is fitted again on every change.
- The base text boost (+0.75 pt) is for the small UI text only: full at 8.25 pt, none from 11 pt (the
  Finder's 12 pt list is the original's size again). Finder opens at least 400 x 520 units.
- Edit fields get an inner margin on the side their text sits (4 units right for numbers, 3 left otherwise)
  where the text shown has room, re-checked on every change; a spin box too narrow for its number plus the
  margin ("2800" in TX filter High) grows up to 4 units to the left, where nothing else sits;
  drop-down lists draw their text with the same left padding. Equalizer: the window fits its contents,
  the same margin below as on the left.
- VFO band text ("60M General RX") keeps at least 4.5:1 contrast: the out-of-band grey is darkened to
  half, the dimmed VFO B text lightened just enough (green on grey was 4.0:1 and 1.1:1, also in the
  original).
- Grids (Memory ...): fixed column widths, row heights and header heights are scaled once when the window
  opens (WinForms leaves them at 96 DPI here).
- Meter containers: position and size are stored in 96 DPI units (they were used as pixels: half size); a
  new container gets the default 400 x 200 at this DPI.
- PureSignal (fixed client sizes), Path Illustrator (diagram and label positions drawn in 96 DPI units),
  Wideband (frequency scale strip height), Band Stack, colour palette: scaled.
- Save (memory quick save) is centred like Restore (upstream had TopCenter for Save only).
- Setup sweep (all 70 pages, original vs 4K, plus a list of overlapping sibling controls in both modes:
  148 overlaps only in 4K before, 20 after, none over 2 units): auto-sized check boxes, radio buttons and
  labels were laid out as if their text were twice as large and covered their neighbours ("RX1 Sub" ->
  ":1 Sub"); they now get the size their text needs. Setup's own placements in 96 DPI pixels (45, per
  radio model: Alex HPF/LPF, the Hardware radio list ...) scaled. Colour buttons (swatch and arrow), the
  gradient editor (markers, labels, mouse) and its preset picker, info icons and list columns scaled.
  `THETIS_HIDPI_OPEN=setupsweep` + `THETIS_HIDPI_SWEEPDIR=<dir>` captures every page and writes the overlap
  list; `THETIS_HIDPI_DUMP=<control>` logs a control's bounds tree.
- Window positions and sizes are saved in 96 DPI units, so one settings database works in both modes.
- Off: command line `-nohidpi` or `THETIS_HIDPI=0`. At 100 % scaling nothing changes.
- Text size: Setup > Appearance > General > High DPI (fork): Default, +1, +2 (one point each on the
  8.25 pt base font, used at the next start; saved as `hidpi_text_size.txt` in the settings folder).
  Every form gets it when it opens. Text in a fixed-size button or label takes the largest size that
  still fits on as many lines as before; an auto-sized check box or label stops before it would cover
  a neighbour; group box titles stay on one line; edit boxes, combos and lists grow one step at most.
  Font-scaled user controls inside the console and Setup (the info bar ...) keep their size; a dialog
  that scales by font (Finder ...) grows as a whole. The VFO digits, the meter text and the info bar's
  Feedback / Pure Signal labels keep their own sizes.
- Dialogs fixed for 200 %: CWX (an old AutoScaleBaseSize form, and its hand-drawn key area), Finder
  rows, owner-drawn list rows (Setup > Appearance > Meters/Gadgets), the colour buttons' drop-down
  palette (hand-drawn in pixels), Band Stack 2 (fixed 256 / 512 px widths; the form scales by its font,
  about 2.17 at 200 %, so its width now comes from its laid-out controls).
- `THETIS_HIDPI_LOG=<file>` logs the DPI work; with `THETIS_HIDPI_LOGFIT=1` also every text fit.
  `THETIS_HIDPI_OPEN=setup:<tab>:<tab>[:<button>]` / `cwx` / `finder:<text>` / `bandstack` /
  `menu:<menu item name>` opens that window after start, a button name clicks it (a colour button opens its
  palette); used for captures (build-tools: capture-thetis.ps1 -AlsoTitle "<other>" grabs the window).

## Band-aware tuning (Setup > Appearance > General > Tuning (fork), on by default)

As AltSDR: tuning by hand (spectrum click / drag, mouse wheel, keys, typing a frequency) into another band
selects the mode last used in that band, the first time AM on LW/MW, the SW broadcast bands and WWV, LSB on
160/80/40 m, USB on the other ham bands (general coverage keeps the mode); and the tune step last set in that
band for that mode, the first time 9 kHz on MW AM (10 kHz in the US region) and 5 kHz on SW broadcast AM.
Band buttons, band stacks, memories, spots and CAT keep their own mode; never during TX. Saved in
`fork_band_memory.txt` in the settings folder. Test hook: `THETIS_HIDPI_OPEN=usertune:7.1:7.3:0.999` tunes
those frequencies as if by hand and logs band, mode and step.

Keep CTUN on (same Setup group, on by default): band buttons and band stack entries saved without CTUN no
longer turn it off (drag to pan needs CTUN); the CTUN button still turns it off until the next band change.

## Test settings (no rebuild needed)

Environment variables read when the first pan starts:

| Variable | Default | Meaning |
|---|---|---|
| `THETIS_RETUNE_LATENCY_MS` | 2.5 | time from sending a DDC frequency to its first sample at the DSP input |
| `THETIS_RETUNE_GUARD_MS` | 1.5 | muted time on each side of the audio switch |
| `THETIS_RETUNE_RAMP_MS` | 1.5 | fade time outside the muted part |

A wrong latency shows as a faint ghost of strong carriers while panning at full span, and as clicks.

## Building with Visual Studio 2022

The solution asks for the VS 2026 toolset (v145). With VS 2022:

```
MSBuild.exe "Project Files\Source\Thetis_VS2026.sln" -t:portaudio,cmASIO,wdsp,ChannelMaster,RawInput,Midi2Cat,Thetis ^
  -p:Configuration=Release -p:Platform=x64 -p:PlatformToolset=v143 -p:RestorePackagesConfig=true -restore -m
```

If the .NET Framework 4.8 Developer Pack is not installed, add
`-p:TargetFrameworkRootPath=<folder>\build` where `<folder>` is the unzipped NuGet package
`Microsoft.NETFramework.ReferenceAssemblies.net48`.

Output: `Project Files\bin\x64\Release\Thetis.exe`.

## Not done yet

- RX2: drag-to-pan is RX1 only (Thetis has one pan/zoom slider for RX1, RX2 and TX together; an RX1
  pan moves the RX2 view too, as the slider always did).
- The retune latency is a fixed estimate until measured on the radio.
- No Setup UI for these options yet.
- High DPI: checked on the main window, Setup (Hardware, Display, DSP AGC/ALC, Appearance General and
  Meters/Gadgets), CWX and Finder at 200 % with text size Default, +1 and +2; other dialogs, meter
  containers, RX2 and collapsed layouts not checked yet. Setup's Discover button still covers the start
  of the radio list header. System DPI aware: on a second monitor with
  a different scaling Windows stretches the window as before.
