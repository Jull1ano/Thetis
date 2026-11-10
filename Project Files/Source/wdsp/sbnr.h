/*  snnr.h

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
and and uses RNNoise and libspecbleach
https://gitlab.xiph.org/xiph/rnnoise
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

#ifndef _sbnr_h
#define _sbnr_h

// Jull1ano fork: NR4 on the libspecbleach AetherSDR uses (folder libspecbleach, built into wdsp.dll): adaptive
// noise estimation (SPP-MMSE / Brandt / Martin), masking veto depth and suppression strength
#include <specbleach_denoiser.h>

typedef struct _sbnr
{
	int run;
	int position;
	double *in;
	double *out;
	float reduction_amount;
	float smoothing_factor;
	float whitening_factor;
	int noise_scaling_type;			// kept for the old API; this libspecbleach has no such setting
	float noise_rescale;			// kept for the old API; not used
	float post_filter_threshold;	// kept for the old API; not used
	int adaptive_noise;				// 1: the noise profile follows the signal
	int noise_estimation_method;	// 0 SPP-MMSE, 1 Brandt (trimmed mean), 2 Martin (minimum statistics)
	float masking_depth;			// 0..1
	float suppression_strength;		// 0..1
	int params_dirty;
	int learn;						// samples still passed through while the first noise profile is learned
	// level normaliser: libspecbleach works on absolute levels (its silence threshold is about -100 dB, its
	// masking model maps 0 dBFS to 96 dB SPL) and AetherSDR feeds it audio after AGC; before AGC WDSP's
	// samples are far quieter. A slow gain brings the input to that level and the same gain, delayed by the
	// library's latency, is taken off the output.
	double lvl_power;				// smoothed input power
	double lvl_gain;				// gain applied to the last input sample
	double* gain_ring;				// gain applied to each input sample, for the output's delay
	int ring_size;
	int ring_pos;
	int latency;					// libspecbleach latency, samples
	SpectralBleachHandle st;
	int buffer_size;
	int rate;

	float* input;
	float* output;
} sbnr, *SBNR;

// define the public api of this module
extern SBNR create_sbnr(int run, int position, int size, double *in, double *out, int rate);
extern void destroy_sbnr(SBNR a);
extern void setSize_sbnr(SBNR a, int size);
extern void setBuffers_sbnr(SBNR a, double *in, double *out);
extern void xsbnr(SBNR a, int pos);
extern void setSamplerate_sbnr(SBNR a, int rate);

#endif // _sbnr_h
