/*  SmoothPan.cs - Jull1ano fork of Thetis

Drag-anywhere pan and Ctrl + mouse wheel zoom for the RX1 spectrum / waterfall, modelled on AltSDR.

- With CTUN on and click tune mode off: left-press on the spectrum or the waterfall, outside the RX
  passband (the passband still drags the VFO, Shift + passband still moves the filter), and move at
  least 3 px: the spectrum follows the mouse 1:1 and the VFO keeps its frequency.
  Released before 3 px it is a click and tunes the VFO to the press point, as before.
- The view moves inside the DDC span first, display only: no radio change, no audio change. When the
  view's edge leaves the span, the DDC is re-centred on the view (paced, latest wins, the exact final
  value goes out when the pan ends). The VFO is carried along when it would leave the DDC span.
- Inertia after release.
- Ctrl + wheel zooms around the cursor: the frequency under the cursor stays put. Display only.

The display side of this lives in WDSP's analyzer (smooth pan / retune): the averaged trace is kept in
absolute frequency, so pan, zoom and retunes shift it instead of restarting or smearing it.
On a DDC retune the WDSP shift changes at the input sample where the new DDC frequency is expected
(SetRXAShiftFreqAt), so the audio stays on the VFO frequency.

This program is free software; you can redistribute it and/or modify it under the terms of the GNU
General Public License as published by the Free Software Foundation; either version 2 of the License,
or (at your option) any later version.
*/

using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace Thetis
{
    public partial class Console
    {
        private const int PAN_DRAG_THRESHOLD_PX = 3;        // a press that moves less than this is a click
        private const double PAN_RETUNE_PACING_MS = 25.0;   // at most one DDC retune per this time while panning
        private const double PAN_INERTIA_DECAY = 0.92;      // velocity factor per timer tick
        private const double PAN_INERTIA_MIN_PX = 0.05;     // inertia stops below this (px per tick)
        private const int PAN_TIMER_MS = 15;
        private const double WHEEL_ZOOM_STEP = 1.0 / 3.0;   // one wheel notch = span x 2^(-1/3)

        // retune timing: from sending a new DDC frequency to its first sample at the DSP / analyzer input.
        // The switch itself is sample exact; network timing is not, and a wrong switch point is a phase
        // jump (a click), so a short soft mute covers the uncertainty. Overrides for testing: environment
        // variables THETIS_RETUNE_LATENCY_MS, THETIS_RETUNE_GUARD_MS, THETIS_RETUNE_RAMP_MS.
        public static double RetuneLatencyMs = 2.5;
        public static double RetuneGuardMs = 1.5;           // muted on each side of the audio switch
        public static double RetuneRampMs = 1.5;            // fade outside the muted part

        private bool _pan_drag_anywhere = true;
        public bool PanDragAnywhere
        {
            get { return _pan_drag_anywhere; }
            set { _pan_drag_anywhere = value; }
        }

        private bool _pan_pending = false;          // pressed outside the passband, not moved past the threshold yet
        private bool _rx1_pan_drag = false;         // panning with the mouse
        private bool _pan_inertia = false;          // panning on after release
        private bool _pan_applying = false;         // inside a pan's own DDC retune (no keep-VFO-in-view scrolling)
        private bool _align_rx1_osc = false;        // the next RX1 shift change goes through SetRXOscAligned
        private bool _pan_latency_set = false;
        private int _pan_press_x;
        private int _pan_last_x;
        private double _pan_press_freq;             // MHz, what a click tunes to
        private double _pan_view_centre = double.NaN;   // MHz, where the user wants the view centre
        private double _pan_velocity;               // px per timer tick
        private double _pan_last_move_ms;
        private double _pan_last_retune_ms = -1.0e9;
        private bool _pan_retune_pending = false;
        private double _pan_exact = double.NaN;     // exact pan / zoom slider values (the trackbars are coarse)
        private double _zoom_exact = double.NaN;
        private readonly Stopwatch _pan_sw = Stopwatch.StartNew();
        private System.Windows.Forms.Timer _pan_timer;

        // ---------------------------------------------------------------- geometry (same maths as SpecHPSDR.initAnalyzer)

        private struct PanGeometry
        {
            public double bw;           // Hz per fft bin
            public double intervals;    // usable bins - 1
            public double width;        // visible width, intervals
            public double low;          // Hz of the view's low edge relative to the DDC centre
            public double SpanHz { get { return intervals * bw; } }
            public double WidthHz { get { return width * bw; } }
            public double MinLow { get { return -0.5 * intervals * bw; } }
            public double MaxLow { get { return (0.5 * intervals - width) * bw; } }
        }

        private static bool panGeometry(SpecHPSDR spec, double zoom_slider, double pan_slider, out PanGeometry g)
        {
            g = new PanGeometry();
            int fft = spec.FFTSize;
            int sr = spec.SampleRate;
            int stitches = Math.Max(1, spec.Stitches);
            if (fft <= 0 || sr <= 0) return false;
            int clip = (int)Math.Floor(0.04 * fft);
            g.bw = (double)sr / (double)fft;
            int bins = stitches * (fft - 1 - 2 * clip);
            g.intervals = bins - 1;
            double zs = Math.Log10(9.0 * zoom_slider + 1.0);
            g.width = g.intervals * (1.0 - (1.0 - 1.0 / 100.0) * zs);
            double clipL = pan_slider * (g.intervals - g.width);
            g.low = -(g.intervals / 2.0 - clipL) * g.bw;
            return g.intervals > 1 && g.width > 1;
        }

        private static double zoomSliderForWidth(double width, double intervals)
        {
            double zs = (1.0 - width / intervals) / (1.0 - 1.0 / 100.0);
            double z = (Math.Pow(10.0, zs) - 1.0) / 9.0;
            return Math.Max(0.0, Math.Min(1.0, z));
        }

        private static double panSliderForLow(PanGeometry g, double low)
        {
            double room = g.MaxLow - g.MinLow;
            if (room <= 1.0e-9) return 0.5;
            return Math.Max(0.0, Math.Min(1.0, (low - g.MinLow) / room));
        }

        // set the exact pan (and zoom) on the RX1, RX2 and TX analyzers; the trackbars only follow
        private void applyPanZoomExact(double zoom_slider, double pan_slider, bool zoom_changed)
        {
            _pan_exact = pan_slider;
            int pv = (int)Math.Round(pan_slider * 1000.0);
            pv = Math.Max(ptbDisplayPan.Minimum, Math.Min(ptbDisplayPan.Maximum, pv));
            ptbDisplayPan.Value = pv;

            if (zoom_changed)
            {
                _zoom_exact = zoom_slider;
                int zv = (int)Math.Round(zoom_slider * 230.0 + 10.0);
                zv = Math.Max(ptbDisplayZoom.Minimum, Math.Min(ptbDisplayZoom.Maximum, zv));
                // pan first without an analyzer update, the zoom handler then updates once with both
                foreach (SpecHPSDR s in new SpecHPSDR[] { specRX.GetSpecRX(0), specRX.GetSpecRX(1), specRX.GetSpecRX(cmaster.inid(1, 0)) })
                {
                    bool u = s.Update;
                    s.Update = false;
                    s.PanSlider = pan_slider;
                    s.Update = u;
                }
                ptbDisplayZoom.Value = zv;
                bool old_ignore = m_bIgnoreZoomCentre;
                m_bIgnoreZoomCentre = true;     // no CTUN re-centre on the VFO: the zoom keeps the cursor frequency
                ptbDisplayZoom_Scroll(this, EventArgs.Empty);
                m_bIgnoreZoomCentre = old_ignore;
                if (_display_max_bin_enabled[0]) setupDisplayMaxBinDetect(1, false, true);
                if (_display_max_bin_enabled[1]) setupDisplayMaxBinDetect(2, false, true);
            }
            else
            {
                ptbDisplayPan_Scroll(this, EventArgs.Empty);
            }
        }

        // exact slider values survive the trackbar round trip (ptbDisplayPan_Scroll / ptbDisplayZoom_Scroll)
        private double exactPanSlider()
        {
            double v = (double)ptbDisplayPan.Value / 1000.0;
            if (!double.IsNaN(_pan_exact) && Math.Abs(v - _pan_exact) <= 0.5 / 1000.0 + 1e-9) return _pan_exact;
            _pan_exact = double.NaN;
            return v;
        }

        private double exactZoomSlider()
        {
            double v = ((double)ptbDisplayZoom.Value - 10.0) / 230.0;
            if (!double.IsNaN(_zoom_exact) && Math.Abs(v - _zoom_exact) <= 0.5 / 230.0 + 1e-9) return _zoom_exact;
            _zoom_exact = double.NaN;
            return v;
        }

        // ---------------------------------------------------------------- mouse

        // called from pnlDisplay_MouseDown where a CTUN click / VFO drag would start (RX1, click tune mode off)
        // returns true when the press is armed as a click-or-pan (no VFO drag, no tune yet)
        private bool panDragArm(MouseEventArgs e, int low_x, int high_x, double click_freq)
        {
            if (!_pan_drag_anywhere || !_click_tune_display || _mox) return false;
            if (e.X > low_x && e.X < high_x) return false;      // passband: drag the VFO as before
            armPan(e, click_freq);
            return true;
        }

        // the top 15 px drag strip: the same pan, a click there does not tune (as before)
        private bool panDragArmTop(MouseEventArgs e)
        {
            if (!_pan_drag_anywhere || !_click_tune_display || _mox) return false;
            armPan(e, double.NaN);
            return true;
        }

        private void armPan(MouseEventArgs e, double click_freq)
        {
            ensurePanLatency();
            stopPanInertia(false);
            _pan_pending = true;
            _rx1_pan_drag = false;
            _pan_press_x = e.X;
            _pan_last_x = e.X;
            _pan_press_freq = click_freq;
            _pan_velocity = 0.0;
            _pan_last_move_ms = _pan_sw.Elapsed.TotalMilliseconds;
            _pan_view_centre = double.NaN;
        }

        // called first in pnlDisplay_MouseMove; returns true when it handled the move (panning)
        private bool panDragMove(MouseEventArgs e)
        {
            if (!_pan_pending && !_rx1_pan_drag) return false;
            if (_pan_pending)
            {
                if (Math.Abs(e.X - _pan_press_x) < PAN_DRAG_THRESHOLD_PX) return true;
                _pan_pending = false;
                _rx1_pan_drag = true;
                _pan_last_x = _pan_press_x;
                startPanTimer();
            }

            double now = _pan_sw.Elapsed.TotalMilliseconds;
            int dx = e.X - _pan_last_x;
            _pan_last_x = e.X;
            if (dx != 0)
            {
                double dt = Math.Max(1.0, now - _pan_last_move_ms);
                double v = dx * PAN_TIMER_MS / dt;                  // px per timer tick
                _pan_velocity = 0.5 * _pan_velocity + 0.5 * v;
                _pan_last_move_ms = now;
                panViewByPixels(dx);
            }
            DisplayCursorX = e.X;
            DisplayCursorY = e.Y;
            Cursor = grabbing;
            return true;
        }

        // called from pnlDisplay_MouseUp (left button)
        private void panDragUp()
        {
            if (_pan_pending)
            {
                // a click: tune like the original CTUN click did on the press
                _pan_pending = false;
                if (!double.IsNaN(_pan_press_freq)) VFOAFreq = Math.Round(_pan_press_freq, 6);
                return;
            }
            if (!_rx1_pan_drag) return;
            _rx1_pan_drag = false;

            double idle = _pan_sw.Elapsed.TotalMilliseconds - _pan_last_move_ms;
            if (idle > 60.0) _pan_velocity = 0.0;                   // the mouse had stopped before release
            if (Math.Abs(_pan_velocity) > PAN_INERTIA_MIN_PX)
                _pan_inertia = true;                                // the timer carries on
            else
                finishPan();
        }

        private bool panActive
        {
            get { return _pan_pending || _rx1_pan_drag || _pan_inertia; }
        }

        // Ctrl + wheel over the spectrum: zoom around the cursor. Returns true when handled.
        private bool panWheelZoom(MouseEventArgs e)
        {
            if (!Common.CtrlKeyDown) return false;
            switch (Display.CurrentDisplayMode)
            {
                case DisplayMode.PANADAPTER:
                case DisplayMode.PANAFALL:
                case DisplayMode.PANASCOPE:
                case DisplayMode.WATERFALL:
                    break;
                default:
                    return false;
            }
            Point p = pnlDisplay.PointToClient(Control.MousePosition);
            if (!pnlDisplay.ClientRectangle.Contains(p) || pnlDisplay.Width <= 0) return true;

            int rx = (rx2_enabled && p.Y > pnlDisplay.Height / 2) ? 2 : 1;
            SpecHPSDR spec = specRX.GetSpecRX(rx == 2 ? 1 : 0);
            if (!panGeometry(spec, exactZoomSlider(), exactPanSlider(), out PanGeometry g)) return true;

            double notches = e.Delta / 120.0;
            if (notches == 0) return true;
            double fx = Math.Max(0.0, Math.Min(1.0, (double)p.X / (double)pnlDisplay.Width));
            double anchor = g.low + fx * g.WidthHz;                 // Hz relative to the DDC centre, stays under the cursor

            double newWidth = g.width * Math.Pow(2.0, -notches * WHEEL_ZOOM_STEP);
            newWidth = Math.Max(g.intervals / 100.0, Math.Min(g.intervals, newWidth));
            double z = zoomSliderForWidth(newWidth, g.intervals);
            if (z < 0.02 / 230.0) z = 0.0;
            if (!panGeometry(spec, z, 0.5, out PanGeometry ng)) return true;
            double newLow = anchor - fx * ng.WidthHz;
            newLow = Math.Max(ng.MinLow, Math.Min(ng.MaxLow, newLow));
            double pan = panSliderForLow(ng, newLow);

            stopPanInertia(true);
            applyPanZoomExact(z, pan, true);
            return true;
        }

        // ---------------------------------------------------------------- pan core

        private void panViewByPixels(double dx)
        {
            if (pnlDisplay.Width <= 0) return;
            SpecHPSDR spec = specRX.GetSpecRX(0);
            if (!panGeometry(spec, exactZoomSlider(), exactPanSlider(), out PanGeometry g)) return;
            double hzpp = g.WidthHz / (double)pnlDisplay.Width;
            if (double.IsNaN(_pan_view_centre))
                _pan_view_centre = CentreFrequency + (g.low + 0.5 * g.WidthHz) * 1e-6;
            _pan_view_centre -= dx * hzpp * 1e-6;                  // content follows the mouse
            panTowardsTarget(spec, g, false);
        }

        // move the view to _pan_view_centre: display only when it fits the DDC span, else re-centre the DDC
        private void panTowardsTarget(SpecHPSDR spec, PanGeometry g, bool final_)
        {
            double wantLow = (_pan_view_centre - CentreFrequency) * 1e6 - 0.5 * g.WidthHz;
            bool fits = wantLow >= g.MinLow - 0.5 && wantLow <= g.MaxLow + 0.5;
            if (fits)
            {
                _pan_retune_pending = false;
                double pan = panSliderForLow(g, wantLow);
                if (Math.Abs(pan - exactPanSlider()) > 1e-12) applyPanZoomExact(exactZoomSlider(), pan, false);
                return;
            }

            double now = _pan_sw.Elapsed.TotalMilliseconds;
            if (final_ || now - _pan_last_retune_ms >= PAN_RETUNE_PACING_MS)
            {
                _pan_retune_pending = false;
                _pan_last_retune_ms = now;
                panRetune(Math.Round(_pan_view_centre, 6));
            }
            else
            {
                // wait for the pacing slot; show the view at the span edge meanwhile
                _pan_retune_pending = true;
                double pan = wantLow < g.MinLow ? 0.0 : 1.0;
                if (Math.Abs(pan - exactPanSlider()) > 1e-12) applyPanZoomExact(exactZoomSlider(), pan, false);
            }
        }

        // re-centre the DDC on the view; the VFO keeps its frequency (CTUN), the shift moves with the DDC
        private void panRetune(double centre_mhz)
        {
            if (CentreFrequency == centre_mhz) return;
            _pan_applying = true;
            try
            {
                // pan centred first without an analyzer update, the DDC change then moves everything at once
                foreach (SpecHPSDR s in new SpecHPSDR[] { specRX.GetSpecRX(0), specRX.GetSpecRX(1), specRX.GetSpecRX(cmaster.inid(1, 0)) })
                {
                    bool u = s.Update;
                    s.Update = false;
                    s.PanSlider = 0.5;
                    s.Update = u;
                }
                _align_rx1_osc = true;
                CentreFrequency = centre_mhz;
                txtVFOAFreq_LostFocus(this, EventArgs.Empty);
                _align_rx1_osc = false;
                applyPanZoomExact(exactZoomSlider(), 0.5, false);
            }
            finally
            {
                _align_rx1_osc = false;
                _pan_applying = false;
            }
        }

        private void finishPan()
        {
            _pan_inertia = false;
            if (_pan_retune_pending && !double.IsNaN(_pan_view_centre))
            {
                SpecHPSDR spec = specRX.GetSpecRX(0);
                if (panGeometry(spec, exactZoomSlider(), exactPanSlider(), out PanGeometry g))
                    panTowardsTarget(spec, g, true);                // the exact final position
            }
            _pan_retune_pending = false;
            _pan_view_centre = double.NaN;
            if (_pan_timer != null) _pan_timer.Stop();
        }

        private void stopPanInertia(bool finish)
        {
            if (!_pan_inertia) return;
            _pan_velocity = 0.0;
            if (finish) finishPan();
            else
            {
                _pan_inertia = false;
                if (_pan_timer != null) _pan_timer.Stop();
            }
        }

        private void startPanTimer()
        {
            if (_pan_timer == null)
            {
                _pan_timer = new System.Windows.Forms.Timer();
                _pan_timer.Interval = PAN_TIMER_MS;
                _pan_timer.Tick += panTimerTick;
            }
            _pan_timer.Start();
        }

        private void panTimerTick(object sender, EventArgs e)
        {
            if (_mox)
            {
                _pan_pending = false;
                _rx1_pan_drag = false;
                finishPan();
                return;
            }
            if (_rx1_pan_drag)
            {
                // mouse held still: send a retune that waited for its pacing slot
                if (_pan_retune_pending && !double.IsNaN(_pan_view_centre))
                {
                    SpecHPSDR spec = specRX.GetSpecRX(0);
                    if (panGeometry(spec, exactZoomSlider(), exactPanSlider(), out PanGeometry g))
                        panTowardsTarget(spec, g, false);
                }
                return;
            }
            if (_pan_inertia)
            {
                if (Math.Abs(_pan_velocity) < PAN_INERTIA_MIN_PX)
                {
                    finishPan();
                    return;
                }
                panViewByPixels(_pan_velocity);
                _pan_velocity *= PAN_INERTIA_DECAY;
                return;
            }
            finishPan();
        }

        private static double envMs(string name, double fallback, double min, double max)
        {
            try
            {
                string s = Environment.GetEnvironmentVariable(name);
                if (!string.IsNullOrEmpty(s) && double.TryParse(s, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double v) && v >= min && v <= max)
                    return v;
            }
            catch { }
            return fallback;
        }

        private void ensurePanLatency()
        {
            if (_pan_latency_set) return;
            _pan_latency_set = true;
            RetuneLatencyMs = envMs("THETIS_RETUNE_LATENCY_MS", RetuneLatencyMs, 0.0, 200.0);
            RetuneGuardMs = envMs("THETIS_RETUNE_GUARD_MS", RetuneGuardMs, 0.0, 50.0);
            RetuneRampMs = envMs("THETIS_RETUNE_RAMP_MS", RetuneRampMs, 0.0, 50.0);
            try
            {
                SpecHPSDRDLL.SetAnalyzerRetuneLatency(cmaster.inid(0, 0), RetuneLatencyMs);
                SpecHPSDRDLL.SetAnalyzerRetuneLatency(cmaster.inid(0, 1), RetuneLatencyMs);
            }
            catch { }
        }

        // RX1 shift change from txtVFOAFreq_LostFocus: aligned with the DDC retune while a pan re-centres
        private void setRX1Osc(int subrx, double osc)
        {
            if (_align_rx1_osc)
                radio.GetDSPRX(0, subrx).SetRXOscAligned(osc, RetuneLatencyMs, RetuneGuardMs, RetuneRampMs);
            else
                radio.GetDSPRX(0, subrx).RXOsc = osc;
        }
    }
}
