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

## Flatten (Setup > Appearance > RX Display > Noise floor (fork), default 50 %)

AetherSDR's Flatten (`AnanPanView.h` `flattenNoiseFloor()`, fdb4c60e): the noise floor's grass drawn shorter without
smoothing across frequency. Every point keeps its frequency; points near the floor are pulled towards it vertically
(50 % = half as tall), smoothly less so further out, and points beyond about three knees (signals) are left exactly
as they are. The floor is the lower quartile over a 5-block window (block = 1/48 of the width) moved up to the
noise's middle; the noise spread is the median over the blocks, followed over frames per receiver. After FFT Smooth
when both are on, into its own buffer (never in place). Display only: the noise floor estimate reads the raw copy,
the waterfall is not changed. `build-tools/test-fft-flatten.ps1` calls it on synthetic spectra: grass 0.82 dB ->
0.41 (50 %), 0.25 (70 %), 0.17 (80 %); carrier, SSB and AM levels unchanged; the few neighbour-order flips are
between points within 0.01 dB of each other (80 %). Saved in `hidpi_fft_flatten.txt`.

## Sharp QuickRecall arrows (high DPI)

The QuickRecall buttons (< V > next to Save / Restore) take 20 x 20 px skin pictures, which came out blurry when
stretched to their 200 % buttons. At high DPI each picture is redrawn at the button's own size (`HighDpi.ArrowShape`,
`HighDpi.CrispArrowImage`, called from `Skin.SetupQuickRecallImages`): the face's rounded rectangle (its sides and
corner radius measured where the face mask crosses half way, median per side) with a one-screen-pixel edge, the
skin's own face shading scaled smoothly inside it, and the triangle as a vector shape in the glyph's colour. The
triangle's place and size come from the glyph's coverage moments (area kept); the old glyph is filled in from the
face along its gradient first. Shape and outline are measured once per button from its first picture, so nothing
moves when it is pressed. A picture without a light glyph keeps the old stretched look; 1x is unchanged.

The pad itself (`ucQuickRecall`) is a font-scaled control that sizes itself to its buttons: at 200 % they came
out 43 px wide (the font ratio 2.167) instead of 40, and the pad ran 4 px past the right border of the VFO
group box. `HighDpi.normalizeNestedFontControls` puts its buttons back on the designer grid at the DPI scale
(`snapToDpiGrid`, edges rounded) and the pad fits itself around them again (`ForkFitToButtons`): 121 x 41 at
354,124 in the 480 x 176 group, 5 px clear of the border (log line `pad ...` with THETIS_HIDPI_LOG).

## QuickRecall list: 30 entries, as tall as its entries

The list (V button) keeps 30 frequencies instead of 16 (`m_nMAXENTRIES`, as AetherSDR's copy of it). Its popup is
as tall as its rows and as wide as its text (`frmQuickRecallPopupList.ForkFit`); it scrolls only when it would
pass the bottom of the screen, and then it is widened by the scroll bar. Before, its height was a fixed
20 + rows x (row + 1) px, so at high DPI it scrolled after 8 rows, and the MHz digits were measured with GDI and
drawn with GDI+ 8 px back, which left a gap at 200 % ("0 .864.000"); now both measure and draw the same way and the
MHz column is at least "88" wide. Under the list, after a line, "Clear history" (as in AetherSDR) empties it, and
a frequency still waiting for its 4 s too; with nothing to clear it is greyed. Test hook:
`THETIS_HIDPI_OPEN=quickrecall:<n>[:<button>[:clear]]` (n entries, list open, the hover on that button, then Clear
history clicked and the list opened again), `THETIS_QR_ROOM=<px>` pretends the screen ends that far below the button.

## Button hover (10 % white)

Every button (Button, or a check / radio box drawn as a button; not FlatStyle.System) gets 10 % white over it while
the mouse is on it and it is enabled, on top of what the skin draws (OE3IDE-TheBlue only adds a 1 px light edge).
`ForkHover` in HighDpi.cs finds the button from WM_MOUSEMOVE (an application message filter), so every window and
buttons made later get it; it draws in the button's Paint event. The QuickRecall arrows at high DPI get it inside
their rounded face only (`HighDpi.ArrowFacePath`). On at 1x too. First 20 %; the user chose 10 % (`ForkHover.ALPHA`).

## Memory window: back / play / next (as AltSDR's MEMORY tab)

A bar right under the window's top (`Memory/MemoryForm.Fork.cs`), sized and drawn as AltSDR's: back, play, next
(24 x 17 px at 100 %, 3 px apart, at the grid's left edge; a 1 px border that lights under the mouse, a dark ring, a
dark bevelled face, play green, stop red), "n / N" (current row / rows) 8 px after them, and SLOW.

- Back / next: the previous / next row with a frequency becomes the current one and is tuned, as a click on the
  row's header (without "Close after selection"); round at the ends; the grid's order as shown (a column sort
  reorders the list itself).
- Play: a slow seek. From the row after the current one each memory with Scan ticked is tuned in turn (the grid's
  current row following) until one has a signal; it stays there with play lit, and play again goes on to the next
  one with a signal. While it steps play is a red stop, which stops on the memory it is on; play then goes on from
  that memory. While a seek is on, back is its BACK (the previous memory, stopped there) and next its FORWARD (on
  to the next memory; when stopped, the next memory, still stopped). Round at the end. A hand tune, a memory
  picked, TX or closing the window ends the seek; with the radio off it does not start.
- A signal: with SQL on (not FM) the RX1 meter at or over the squelch level; otherwise the passband SNR (the info
  bar's PB SNR: meter against the display's noise floor) 6 dB or more (`Console.ForkSignalMargin`). Each memory:
  400 ms to settle, then readings 60 ms apart for 240 ms (about 0.65 s a memory), all averaged; with no noise
  floor reading in 2.5 s more it is passed over.
- SLOW (saved in `fork_memory_bar.txt` in the data folder, off by default): readings for 1 s (about 1.4 s a
  memory), so a weak signal is averaged longer too; it applies to a running seek at once.

A memory or QuickRecall jump now lands exactly on its frequency (`Console.ForkJumpVfoA`). With CTUN on and Setup's
"Click Tune Drag" + "limit to the spectral area" on, a jump to a frequency outside the view but less than 0.5 MHz
past its edges was left at the view's edge (memory 0.531 MHz -> VFO 0.483844); now the view re-centres on it, as
a CAT jump does.

Test hook: `THETIS_HIDPI_OPEN=memnav:<row>:<action>,<action>...` (the window open, that row tuned, then one action
a second: next / back / play / stop / slow / vfo (1 kHz up, as a hand tune) / w, the state logged);
`THETIS_SEEK_FAKE=<row>,<row>` makes those rows the ones with a signal; `THETIS_SEEK_MARGIN=<dB>` sets the margin.

## Skin "AetherSDR Style"

A Thetis skin (no code) in AetherSDR's look, made by `Skins/make-aether-skin.py` from AetherSDR's own styles
(BandBar.cpp, the applet buttons, default-dark theme): mode buttons teal (#a8e8d0 text, teal when on), ham bands
and filters grey-blue (blue when on), SWL bands amber (#ffcc66, amber when on), NR / ANF / NB and the other DSP
buttons green when on, MOX / TUNE amber edged (red when on), the rest dark navy with a blue "on"; hover and
disabled states; AetherSDR's sliders (4 px groove, round light handle); a dark navy window; text colours, flat dark
combo boxes and number boxes from the skin's xml. Pictures are drawn at each control's 200 % size (crisp at 4K,
scaled down at 100 %), opaque (Thetis paints selected buttons' back colour yellow behind them).

    python -I Skins/make-aether-skin.py "%APPDATA%\OpenHPSDR\Skins\AetherSDR Style" "Project Files/Source/Console" "%APPDATA%\OpenHPSDR\Skins"

then Setup > Appearance > General > Skins > AetherSDR Style. Not reachable by a skin: the VFO digits, the band
text, the spectrum grid and labels, the status bar and the meters (Setup > Appearance colours), the arrow box of a
combo and the spin buttons of a number box (drawn by Windows).

## Band-aware tuning (Setup > Appearance > General > Receive (fork), on by default)

As AltSDR: tuning by hand (spectrum click / drag, mouse wheel, keys, typing a frequency) into another band
selects the mode last used in that band, the first time AM on LW/MW, the SW broadcast bands and WWV, LSB on
160/80/40 m, USB on the other ham bands (general coverage keeps the mode); and the tune step last set in that
band for that mode, the first time 9 kHz on MW AM (10 kHz in the US region) and 5 kHz on SW broadcast AM.
Band buttons, band stacks, memories, spots and CAT keep their own mode; never during TX. Saved in
`fork_band_memory.txt` in the settings folder. Test hook: `THETIS_HIDPI_OPEN=usertune:7.1:7.3:0.999` tunes
those frequencies as if by hand and logs band, mode and step.

Keep CTUN on (same Setup group, on by default): band buttons and band stack entries saved without CTUN no
longer turn it off (drag to pan needs CTUN); the CTUN button still turns it off until the next band change.

NR2 on by default (same group, on by default): Thetis turns NR off itself in FM and, with the digital mode
settings, in DIGU / DIGL, and never back on. In every other mode NR2 comes on when NR is off, after each mode
change and at start, on both receivers; the NR button still turns it off until the next mode change.

NR buttons (as NereusSDR): each NR on its own button, a click switches it on, a second click off, no cycling.
NR1 is the old NR button; NR2 / NR3 / NR4 sit in the free row above NR / ANF / the display mode list, with
the skin's NR button images, and NNR after NR4, up to the end of the display mode list. Where that row has
no room (a narrow window) it hides and NR cycles as before (NR, NR2, NR3, NR4, NNR, off). NR2 on by default
still applies.

Collapse then Expand put the Zoom slider in the middle of the strip, over the NR row (Expand placed it left of
ZTB before ZTB, anchored to the display panel, had moved back), so the row hid: Zoom goes left of ZTB again once
the layout has settled. Test hook `layouttest` (Collapse, Expand, positions logged); `THETIS_NO_AUTOSTART=1` keeps
a test run from powering the radio on.

Gradients (Setup > Appearance > RX Display > Gradients (fork), off by default): "Two-colour fill" and
"Two-colour line", each on its own with its own top / bottom colours, over the panadapter height (the fill
with the Data Fill alpha, the line with the Data Line alpha: strong signals reach the top colour). The
Gradients tab's own gradient wins when it is on. Saved in `fork_display.txt`.

Memory: freq + mode (Receive (fork) group, on by default): a memory recalls its frequency and mode only, as if
tuned there by hand (the step from the band memory); its filter, AGC, power, split, repeater and schedule
settings stay in the memory (every column of the Memory window still shows them) but are not applied. New
memories store no step. A memory's step is never learned as the band's step (the old recall taught 500 Hz
everywhere).

Memory window: rows get their height from the row template before the memories are bound (setting it row
by row re-laid the grid out for each row: 3 s for 76 rows); it opens in about 0.5 s (the original 0.3 s).
The row header is twice the original's width.

Memory window: one left click on a row's header (the small field left of the row) recalls that memory, as
Select or a double click do; every recall from the window centres the view on the VFO (as a right click on
CTUN). Test hooks: `click:<control>:...`, `memrow:<n>`.

## Keys

M mutes / unmutes RX1 (as MUT), Shift+M RX2; it comes before the user-defined keys, so M no longer is Band Up
(Thetis' default; give Band Up another key in Setup > Keyboard if wanted). Keypad * still mutes RX1, Alt+M (memory)
and Ctrl+M (MOX) are unchanged. Test hook `keys:M:Shift+M:...` presses keys through the console's KeyDown.

F9 collapses / expands the window (the menu's Collapse / Expand); Ctrl+F9 stays CWX memory 9.

## NNR, neural noise reduction (WDSP 2.11, as in AetherSDR)

Warren Pratt's NNR from WDSP 2.11 (`nnr.c`, `nnet.c`, `nnio.c`, `nnrbypass.c`, the built-in models
`nnr_model_0.c` Standard and `nnr_model_1.c` Premium; `NNET_NSLOTS` 2, as AetherSDR offers), wired into this
WDSP 1.29's receive chain as 2.11 does it: in SSB / CW / DIG a bypass takes the I/Q ahead of the notched
filter, filters it to the model's training band and hands it to the network before AGC, skipping the
demodulator chain up to the pre-AGC noise reductions (so the RX EQ, the notches and a pre-AGC ANF are not
applied while NNR is on); the receive filter (bp1) runs once, right after the network. In AM / SAM / DSB / FM
the network works on the audio after AGC. The network runs at 16 kHz inside, so the DSP rate must be a
multiple of 16 kHz (48 kHz is). `wdsp.dll` grows from 5.6 to 12.4 MB (the two models).

NR value 5 is NNR: the NNR button in the NR row, DSP > NNR (RX1) and the RX2 menu's NNR, the old NR button's
cycle. Setup > DSP > AetherRX > NNR: Strength 0..100 (mask floor -10 dB at 0 to -50 dB at 100; 38 = WDSP's own -25 dB,
AetherSDR's default), Model Standard / Premium, and WDSP's tuning values (alpha, knee, tau, max gain, attack,
release; each starts at WDSP's own), Defaults button. A right click on the NNR button opens that tab. Saved in
`fork_nnr.txt`, the same for every receiver; the tuning values survive a DSP rate / buffer size change (WDSP
puts them back after rebuilding the networks).

Test hook: `THETIS_HIDPI_OPEN=nnrtest:14.340:USB:6.005:AM` tunes each frequency and logs, for NR off, NNR
Standard / Premium, strength 0 / 100, NR2 and off again, the mean post-AGC level (AGC_AV), AGC gain, S meter
and the process CPU over 3 s (the settings file is not written).

## NR4 as in AetherSDR, and the Final Output trim

NR4 runs on the libspecbleach AetherSDR vendors (`wdsp/libspecbleach`, compiled into wdsp.dll with MSVC in
C11 mode; patched for MSVC: two VLAs, `SB_WARN_UNUSED_RESULT`, the `_Static_assert` mapping only outside
C11), replacing the old prebuilt `specbleach.dll` (no longer linked; wdsp.dll now needs `libfftw3f-3.dll`,
which Thetis already ships). `sbnr.c` works as AetherSDR's SpecbleachFilter: 40 ms frames, parameters loaded
only when they change, after NR4 comes on (or Reset profile) the input passes through for 1 s while the
noise profile is learned. Two differences from AetherSDR:

- libspecbleach divides `suppression_strength` by 100 (it takes percent; its header says 0..1). AetherSDR
  passes 0..1, so its Suppression slider moves the over-subtraction by at most 1 %. Here the 0..1 control is
  sent as 0..100 %.
- A slow level normaliser around the library: libspecbleach works on absolute levels (silence below about
  -100 dB power, 0 dBFS = 96 dB SPL for its masking model) and AetherSDR feeds it audio after AGC; WDSP's
  samples before AGC are far quieter. The input is brought to a -20 dBFS mean square (1 s tracking) and
  the same per-sample gain, delayed by the library's latency, is taken off the output.

Setup > DSP > AetherRX > NR4 (AetherSDR's controls and defaults): noise estimation MMSE / Brandt / Martin,
Reset profile, Defaults, Adaptive noise estimation (on), Reduction 0..40 dB (10), Smoothing (0 %),
Whitening (0 %), Masking depth (0.50), Suppression (0.50). The same for RX1 and RX2 (`fork_nr4.txt`); they
replace the NR/ANF tab's NR4 RX1 / RX2 groups (hidden, a link in their place; their reduction / smoothing /
whitening handlers no longer set NR4). Brandt costs about twice the CPU of MMSE or Martin. With Adaptive off
nothing is learned, so nothing is reduced (as in AetherSDR). A right click on the NR4 button opens the page.

Final Output (Setup > DSP > AetherRX > Final Output): AetherSDR's RX trim as a slider, -12..+12 dB in 0.1 dB
steps, 0 dB button; it multiplies each receiver's output gain (WDSP panel gain, after the noise reductions,
AGC and filters, before the volume). Saved in `fork_output.txt`.

Test hook: `THETIS_HIDPI_OPEN=nr4test:14.340:USB` measures NR4 (suppression, masking, before and after
AGC), NNR and NR2 like `nnrtest`.

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
