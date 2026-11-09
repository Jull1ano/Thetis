# Jull1ano fork of Thetis — branch `smooth-pan-drag`

Based on Thetis v2.10.3.15 (ramdor/Thetis). Goal: AltSDR-style spectrum handling.

## What changed

**Drag anywhere to pan** (RX1, CTUN on, click tune mode off) — `Console/SmoothPan.cs`
- Left-press on the spectrum or the waterfall outside the RX passband and move 3 px or more: the
  spectrum follows the mouse 1:1, the VFO keeps its frequency. The top drag strip does the same.
- Released before 3 px: a click, tunes the VFO to that point (as before).
- Inside the passband: drags the VFO (as before). Shift + passband: moves the filter (as before).
- The view first moves inside the DDC span (display only: no radio change, no audio change). When its
  edge leaves the span, the DDC is re-centred on the view, at most every 25 ms, exact final value on release.
- Inertia after release.

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
