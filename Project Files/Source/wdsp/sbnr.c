/*  sbnr.c

This file is part of a program that implements a Software-Defined Radio.

This code/file can be found on GitHub : https://github.com/ramdor/Thetis

Copyright (C) 2000-2025 Original authors
Copyright (C) 2020-2025 Richard Samphire MW0LGE

This program is free software; you can redistribute it and/or
modify it under the terms of the GNU General Public License
as published by the Free Software Foundation; either version 2
of the License, or (at your option) any later version.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU General Public License for more details.

You should have received a copy of the GNU General Public License
along with this program; if not, write to the Free Software
Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301, USA.

The author can be reached by email at

mw0lge@grange-lane.co.uk

This code is based on code and ideas from  : https://github.com/vu3rdd/wdsp
and and uses libspecbleach
https://github.com/lucianodato/libspecbleach
*/
//
//============================================================================================//
// Dual-Licensing Statement (Applies Only to Author's Contributions, Richard Samphire MW0LGE) //
// ------------------------------------------------------------------------------------------ //
// For any code originally written by Richard Samphire MW0LGE, or for any modifications       //
// made by him, the copyright holder for those portions (Richard Samphire) reserves the       //
// right to use, license, and distribute such code under different terms, including           //
// closed-source and proprietary licences, in addition to the GNU General Public License      //
// granted above. Nothing in this statement restricts any rights granted to recipients under  //
// the GNU GPL. Code contributed by others (not Richard Samphire) remains licensed under      //
// its original terms and is not affected by this dual-licensing statement in any way.        //
// Richard Samphire can be reached by email at :  mw0lge@grange-lane.co.uk                    //
//============================================================================================//

#define _CRT_SECURE_NO_WARNINGS

#include "comm.h"

// Jull1ano fork: NR4 as AetherSDR runs it (SpecbleachFilter.cpp): 40 ms frames, the parameters loaded only when
// they change, and the first second after a start passed through unprocessed while the noise profile is learned
#define SBNR_FRAME_MS 40.0F
#define SBNR_LVL_TARGET 0.01		// mean square the input is brought to (-20 dBFS, audio after AGC)
#define SBNR_LVL_TAU 1.0			// s, level tracking
#define SBNR_LVL_GMAX 1.0e7
#define SBNR_LVL_GMIN 1.0e-2

// (re)sizes the gain ring for the library's latency; every gain in it starts at the current gain
static void sbnr_ring (SBNR a)
{
	int i;
	_aligned_free (a->gain_ring);
	a->latency = a->st ? (int)specbleach_get_latency (a->st) : 0;
	a->ring_size = a->latency + a->buffer_size + 1;
	a->gain_ring = (double*)malloc0 (a->ring_size * sizeof (double));
	for (i = 0; i < a->ring_size; i++) a->gain_ring[i] = a->lvl_gain;
	a->ring_pos = 0;
}

static void sbnr_restart (SBNR a)
{
	if (a->st) specbleach_reset_noise_profile (a->st);
	a->learn = a->rate;
	a->params_dirty = 1;
	a->lvl_power = 0.0;
	a->lvl_gain = 1.0;
	sbnr_ring (a);
}

void setSize_sbnr (SBNR a, int size)
{
    _aligned_free(a->input);
    _aligned_free(a->output);
    a->input = malloc0(size * sizeof(float));
    a->output = malloc0(size * sizeof(float));
    a->buffer_size = size;
    sbnr_ring(a);
}

void setBuffers_sbnr (SBNR a, double* in, double* out)
{
	a->in = in;
	a->out = out;
}

SBNR create_sbnr (int run, int position, int size, double *in, double *out, int rate)
{
    SBNR a = (SBNR) malloc0 (sizeof (sbnr));

    a->run = run;
    a->position = position;
    a->rate = rate;
    a->st = specbleach_initialize(a->rate, SBNR_FRAME_MS);
    a->in = in;
    a->out = out;
    a->reduction_amount = 10.F;
    a->smoothing_factor = 0.F;
    a->whitening_factor = 0.F;
    a->noise_scaling_type = 0;
    a->noise_rescale = 2.F;
    a->post_filter_threshold = -10.F;
    a->adaptive_noise = 1;
    a->noise_estimation_method = 0;
    a->masking_depth = 0.5F;
    a->suppression_strength = 0.5F;
    a->params_dirty = 1;
    a->learn = a->rate;
    a->buffer_size = size;
    a->input = malloc0(a->buffer_size * sizeof(float));
    a->output = malloc0(a->buffer_size * sizeof(float));
    a->lvl_power = 0.0;
    a->lvl_gain = 1.0;
    sbnr_ring(a);

    return a;
}

void setSamplerate_sbnr(SBNR a, int rate)
{
    if (a->st) specbleach_free(a->st);
    a->rate = rate;
    a->st = specbleach_initialize(a->rate, SBNR_FRAME_MS);
    a->params_dirty = 1;
    a->learn = a->rate;
    sbnr_ring(a);
}

void xsbnr (SBNR a, int pos)
{
    if (a->run && pos == a->position && a->st)
    {
        double*  in = a->in;
        double* out = a->out;
        int      bs = a->buffer_size;
        float* proc_out = a->output;
        float*  to_proc = a->input;

        if (a->params_dirty)
        {
            SpectralBleachDenoiserParameters p;
            memset (&p, 0, sizeof (p));
            p.learn_noise = 0;
            p.residual_listen = false;
            p.reduction_amount = a->reduction_amount;
            p.smoothing_factor = a->smoothing_factor;
            p.whitening_factor = a->whitening_factor;
            p.adaptive_noise = a->adaptive_noise;
            p.noise_estimation_method = a->noise_estimation_method;
            p.masking_depth = a->masking_depth;
            // Jull1ano fork: libspecbleach divides suppression_strength by 100 (it takes percent, although its
            // header says 0..1); AetherSDR passes 0..1, so its slider moves the over-subtraction by at most 1 %.
            // Here the 0..1 control is the 0..100 % the library expects.
            p.suppression_strength = a->suppression_strength * 100.0F;
            p.aggressiveness = 0.0F;
            p.tonal_reduction = 0.0F;
            specbleach_load_parameters (a->st, p);
            a->params_dirty = 0;
        }

        // the level normaliser: the block's power moves the tracked level, the gain ramps over the block to its
        // new value; each input sample's gain goes into the ring
        double p = 0.0;
        for (int i = 0; i < bs; i++)
            p += in[2 * i + 0] * in[2 * i + 0];
        p /= (double)bs;
        {
            double alpha = 1.0 - exp (-(double)bs / ((double)a->rate * SBNR_LVL_TAU));
            if (a->lvl_power <= 0.0) a->lvl_power = p;	// first block: start at its level
            else a->lvl_power += alpha * (p - a->lvl_power);
        }
        double g1 = a->lvl_power > 1.0e-30 ? sqrt (SBNR_LVL_TARGET / a->lvl_power) : SBNR_LVL_GMAX;
        if (g1 > SBNR_LVL_GMAX) g1 = SBNR_LVL_GMAX;
        if (g1 < SBNR_LVL_GMIN) g1 = SBNR_LVL_GMIN;
        double g0 = a->lvl_gain;
        for (int i = 0; i < bs; i++)
        {
            double g = g0 + (g1 - g0) * (double)(i + 1) / (double)bs;
            to_proc[i] = (float)(in[2 * i + 0] * g);
            a->gain_ring[a->ring_pos] = g;
            if (++a->ring_pos >= a->ring_size) a->ring_pos = 0;
        }
        a->lvl_gain = g1;

        specbleach_process(a->st, (uint32_t)bs, to_proc, proc_out);

        if (a->learn > 0)
        {
            // still learning the noise: the input goes out as it came in (the output would be silence)
            a->learn -= bs;
            for (int i = 0; i < bs; i++)
            {
                out[2 * i + 0] = in[2 * i + 0];
                out[2 * i + 1] = 0.0;
            }
        }
        else
        {
            // output sample i left the library 'latency' samples after its input: take off that sample's gain
            int k = a->ring_pos - bs - a->latency;
            while (k < 0) k += a->ring_size;
            for (int i = 0; i < bs; i++)
            {
                out[2 * i + 0] = (double)proc_out[i] / a->gain_ring[k];
                out[2 * i + 1] = 0.0;
                if (++k >= a->ring_size) k = 0;
            }
        }
    }
    else if (a->out != a->in)
    {
        memcpy (a->out, a->in, a->buffer_size * sizeof (complex));
    }
}

void destroy_sbnr (SBNR a)
{
    if (a->st) specbleach_free(a->st);
    _aligned_free (a->input);
    _aligned_free (a->output);
    _aligned_free (a->gain_ring);
    _aligned_free (a);
}

PORT
void SetRXASBNRRun (int channel, int run)
{
	SBNR a = rxa[channel].sbnr.p;
	if (a->run != run)
	{
		RXAbp1Check (channel, rxa[channel].amd.p->run, rxa[channel].snba.p->run,
                             rxa[channel].emnr.p->run, rxa[channel].anf.p->run, rxa[channel].anr.p->run,
                             rxa[channel].rnnr.p->run, run);
		EnterCriticalSection (&ch[channel].csDSP);
		if (run) sbnr_restart (a); // Jull1ano fork: a fresh noise profile, as AetherSDR starts NR4
		a->run = run;
		RXAbp1Set (channel);
		LeaveCriticalSection (&ch[channel].csDSP);
	}
}

/* Sets the amount of dBs that the noise will be attenuated: 0 dB to 40 dB (this libspecbleach) */
PORT
void SetRXASBNRreductionAmount (int channel, float amount)
{
    if (amount < 0 || amount > 40) return;

	EnterCriticalSection (&ch[channel].csDSP);
	rxa[channel].sbnr.p->reduction_amount = amount;
	rxa[channel].sbnr.p->params_dirty = 1;
	LeaveCriticalSection (&ch[channel].csDSP);
}

/* Percentage of smoothing to apply. Averages the reduction calculation frame
 * per frame so the rate of change is less resulting in less musical noise but
 * if too strong it can blur transient and reduce high frequencies. It goes
 * from 0 to 100 percent */
PORT
void SetRXASBNRsmoothingFactor (int channel, float factor)
{
    if (factor < 0 || factor > 100) return;

	EnterCriticalSection (&ch[channel].csDSP);
	rxa[channel].sbnr.p->smoothing_factor = factor;
	rxa[channel].sbnr.p->params_dirty = 1;
	LeaveCriticalSection (&ch[channel].csDSP);
}

/* Percentage of whitening that is going to be applied to the residue of the
 * reduction. It modifies the noise floor to be more like white noise. This
 * can help hide musical noise when the noise is colored. It goes from 0 to
 * 100 percent */
PORT
void SetRXASBNRwhiteningFactor (int channel, float factor)
{
    if (factor < 0 || factor > 100) return;

	EnterCriticalSection (&ch[channel].csDSP);
	rxa[channel].sbnr.p->whitening_factor = factor;
	rxa[channel].sbnr.p->params_dirty = 1;
	LeaveCriticalSection (&ch[channel].csDSP);
}

// Jull1ano fork: the old libspecbleach's noise rescale, post-filter threshold and noise scaling type are kept as
// calls (Thetis' Setup still sends them) but this libspecbleach has no such settings
PORT
void SetRXASBNRnoiseRescale (int channel, float factor)
{
    if (factor < 0 || factor > 12) return;

	EnterCriticalSection (&ch[channel].csDSP);
	rxa[channel].sbnr.p->noise_rescale = factor;
	LeaveCriticalSection (&ch[channel].csDSP);
}

PORT
void SetRXASBNRpostFilterThreshold (int channel, float threshold)
{
    if (threshold < -10 || threshold > 10) return;

	EnterCriticalSection (&ch[channel].csDSP);
	rxa[channel].sbnr.p->post_filter_threshold = threshold;
	LeaveCriticalSection (&ch[channel].csDSP);
}

PORT
void SetRXASBNRnoiseScalingType(int channel, int noise_scaling_type)
{
    if (noise_scaling_type < 0 || noise_scaling_type > 2) return;

    EnterCriticalSection(&ch[channel].csDSP);
    rxa[channel].sbnr.p->noise_scaling_type = noise_scaling_type;
    LeaveCriticalSection(&ch[channel].csDSP);
}

// Jull1ano fork: AetherSDR's NR4 settings
PORT
void SetRXASBNRadaptiveNoise (int channel, int on)
{
	EnterCriticalSection (&ch[channel].csDSP);
	rxa[channel].sbnr.p->adaptive_noise = on ? 1 : 0;
	rxa[channel].sbnr.p->params_dirty = 1;
	LeaveCriticalSection (&ch[channel].csDSP);
}

// 0 SPP-MMSE, 1 Brandt (trimmed mean), 2 Martin (minimum statistics)
PORT
void SetRXASBNRnoiseEstimationMethod (int channel, int method)
{
	if (method < 0 || method > 2) return;
	EnterCriticalSection (&ch[channel].csDSP);
	rxa[channel].sbnr.p->noise_estimation_method = method;
	rxa[channel].sbnr.p->params_dirty = 1;
	LeaveCriticalSection (&ch[channel].csDSP);
}

PORT
void SetRXASBNRmaskingDepth (int channel, float depth)
{
	if (depth < 0 || depth > 1) return;
	EnterCriticalSection (&ch[channel].csDSP);
	rxa[channel].sbnr.p->masking_depth = depth;
	rxa[channel].sbnr.p->params_dirty = 1;
	LeaveCriticalSection (&ch[channel].csDSP);
}

PORT
void SetRXASBNRsuppressionStrength (int channel, float strength)
{
	if (strength < 0 || strength > 1) return;
	EnterCriticalSection (&ch[channel].csDSP);
	rxa[channel].sbnr.p->suppression_strength = strength;
	rxa[channel].sbnr.p->params_dirty = 1;
	LeaveCriticalSection (&ch[channel].csDSP);
}

// forgets the noise profile and learns it again (AetherSDR's reset button)
PORT
void SetRXASBNRresetNoiseProfile (int channel)
{
	EnterCriticalSection (&ch[channel].csDSP);
	sbnr_restart (rxa[channel].sbnr.p);
	LeaveCriticalSection (&ch[channel].csDSP);
}

PORT
void SetRXASBNRPosition(int channel, int position)
{
    EnterCriticalSection(&ch[channel].csDSP);
    rxa[channel].sbnr.p->position = position;
    rxa[channel].bp1.p->position = position;
    LeaveCriticalSection(&ch[channel].csDSP);
}
