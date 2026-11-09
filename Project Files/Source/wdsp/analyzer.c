/*  analyzer.c

This file is part of a program that implements a Spectrum Analyzer
used in conjunction with software-defined-radio hardware.

Copyright (C) 2012, 2013, 2014, 2016, 2023, 2025 Warren Pratt, NR0V
Copyright (C) 2012 David McQuate, WA8YWQ - Kaiser window & Bessel function added.

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

warren@wpratt.com

*/

#include "comm.h"

DP pdisp[dMAX_DISPLAYS];

double bessi0(double x)
{
	double ax,ans;
	double y;

	if ((ax=fabs(x)) < 3.75) {
		y = x / 3.75,  y = y * y;
		ans = 1.0 + y * (3.5156229 + y * (3.0899424 +  y* (1.2067492
			+ y * (0.2659732 + y * (0.360768e-1 + y * 0.45813e-2)))));
	} else {
		y = 3.75 / ax;
		ans = (exp(ax) / sqrt(ax)) * (0.39894228 + y * (0.1328592e-1
			+ y * (0.225319e-2 + y * (-0.157565e-2 + y * (0.916281e-2
			+ y * (-0.2057706e-1 + y * (0.2635537e-1 + y * (-0.1647633e-1
			+ y * 0.392377e-2))))))));
	}
	return ans;
}

void new_window(int disp, int type, int size, double PiAlpha)
{
	DP a = pdisp[disp];
	int i;
	double arg0, arg1, cgsum, igsum;
	switch (type)
	{
	case 0:					// rectangular window
		{
			a->inv_coherent_gain = 1.0;
			igsum = (double)size;
			for (i = 0; i < size; i++)
				a->window[i] = a->inv_coherent_gain * 1.0;
			break;
		}
	case 1:					// blackman-harris window (4 term)
		{
			arg0 = 2.0 * PI / ((double)size - 1.0);
			cgsum = 0.0;
			igsum = 0.0;
			for (i = 0; i < size; i++)
			{
				arg1 = arg0 * (double)i;
				a->window[i] = 0.35875 - 0.48829 * cos(arg1) + 0.14128 * cos(2.0 * arg1) - 0.01168 * cos(3.0 * arg1);
				cgsum += a->window[i];
				igsum += a->window[i] * a->window[i];
			}
			a->inv_coherent_gain = (double)size / cgsum;
			for (i = 0; i < size; i++)
				a->window[i] *= a->inv_coherent_gain;
			break;
		}
	case 2:					// hann window
		{
			arg0 = 2.0 * PI / ((double)size - 1.0);
			cgsum = 0.0;
			igsum = 0.0;
			for (i = 0; i < size; i++)
			{
				a->window[i] = 0.5 * (1.0 - cos((double)i * arg0));
				cgsum += a->window[i];
				igsum += a->window[i] * a->window[i];
			}
			a->inv_coherent_gain = (double)size / cgsum;
			for (i = 0; i < size; i++)
				a->window[i] *= a->inv_coherent_gain;
			break;
		}
	case 3:					// flat-top window
		{
			arg0 = 2.0 * PI / ((double)size - 1.0);
			cgsum = 0.0;
			igsum = 0.0;
			for (i = 0; i < size; i++)
			{
				arg1 = arg0 * (double)i;
				a->window[i] = 0.21557895 - 0.41663158 * cos(arg1) + 0.277263158 * cos(2.0 * arg1) - 0.083578947 * cos(3.0 * arg1) + 0.006947368 * cos (4.0 * arg1);
				cgsum += a->window[i];
				igsum += a->window[i] * a->window[i];
			}
			a->inv_coherent_gain = (double)size / cgsum;
			for (i = 0; i < size; i++)
				a->window[i] *= a->inv_coherent_gain;
			break;
		}
	case 4:					// hamming window
		{
			arg0 = 2.0 * PI / ((double)size - 1.0);
			cgsum = 0.0;
			igsum = 0.0;
			for (i = 0; i < size; i++)
			{
				a->window[i] = (0.54 - 0.46 * cos((double)i * arg0));
				cgsum += a->window[i];
				igsum += a->window[i] * a->window[i];
			}
			a->inv_coherent_gain = (double)size / cgsum;
			for (i = 0; i < size; i++)
				a->window[i] *= a->inv_coherent_gain;
			break;
		}
	case 5:					// Kaiser window
		{	arg0 = bessi0(PiAlpha);
			arg1 = (double)(size - 1);
			cgsum = 0.0;
			igsum = 0.0;
			for (i = 0; i < size; ++i)
			{
				a->window[i] = bessi0(PiAlpha * sqrt(1.0 - pow(2.0 * (double)i / arg1 - 1.0, 2))) / arg0;
				cgsum += a->window[i];
				igsum += a->window[i] * a->window[i];
			}
			a->inv_coherent_gain = (double)size / cgsum;
			for (i = 0; i < size; i++)
				a->window[i] *= a->inv_coherent_gain;
			break;
		}
	case 6:					// Blackman-Harris window (7-term)
	default:
		{
			arg0 = 2.0 * PI / ((double)size - 1.0);
			cgsum = 0.0;
			igsum = 0.0;
			for (i = 0; i < size; ++i)
			{
				arg1 = cos (arg0 * (double)i);
				a->window[i]   =	+ 6.3964424114390378e-02
						+ arg1 *  ( - 2.3993864599352804e-01
						+ arg1 *  ( + 3.5015956323820469e-01
						+ arg1 *  ( - 2.4774111897080783e-01
						+ arg1 *  ( + 8.5438256055858031e-02
						+ arg1 *  ( - 1.2320203369293225e-02
						+ arg1 *  ( + 4.3778825791773474e-04 ))))));
				cgsum += a->window[i];
				igsum += a->window[i] * a->window[i];
			}
			a->inv_coherent_gain = (double)size / cgsum;
			for (i = 0; i < size; i++)
				a->window[i] *= a->inv_coherent_gain;
			break;
		}
	}
	a->inherent_power_gain = igsum / (double)size;
	a->inv_enb = 1.0 / (a->inherent_power_gain * a->inv_coherent_gain * a->inv_coherent_gain);
	// print_window_gain ("windows.txt", type, a->inv_coherent_gain, a->inherent_power_gain);
}

// spur elimination, REAL input data
void eliminate(int disp, int ss, int LO)
{
	DP a = pdisp[disp];
	int i, k, begin, end, ilim;
	double mag;

	if (ss == a->begin_ss)
		begin = a->fscL + a->clip;
	else
		begin = a->clip;
	if (ss == a->end_ss)
		end = a->out_size - 1 -a->clip - a->fscH;
	else
		end = a->out_size - 1 - a->clip;

	ilim = a->out_size - 1;

	if (a->flip[LO])
		for (i = ilim - begin, k = 0; i > ilim - end; i--, k++)
		{
			mag = (a->fft_out[ss][LO])[i][0] * (a->fft_out[ss][LO])[i][0] + (a->fft_out[ss][LO])[i][1] * (a->fft_out[ss][LO])[i][1];
			if ((a->spec_flag[ss] == 0) || (mag < (a->result[ss])[k]))
				(a->result[ss])[k] = mag;
		}
	else
		for (i = begin, k = 0; i < end; i++, k++)
		{
			mag = (a->fft_out[ss][LO])[i][0] * (a->fft_out[ss][LO])[i][0] + (a->fft_out[ss][LO])[i][1] * (a->fft_out[ss][LO])[i][1];
			if ((a->spec_flag[ss] == 0) || (mag < (a->result[ss])[k]))
				(a->result[ss])[k] = mag;
		}
	a->ss_bins[ss] = k;
}

// spur elimination, COMPLEX input data
void Celiminate(int disp, int ss, int LO)
{
	DP a = pdisp[disp];
	int i, k, begin0, end0, begin1, end1, ilim;
	double mag;
	// smooth mode: deliver the whole span, the visible part is cut out in sm_stitch() so a pan needs no restart
	int fscL = a->smooth_on ? 0 : a->fscL;
	int fscH = a->smooth_on ? 0 : a->fscH;

	if (ss == a->begin_ss)
	{
		begin0 = a->out_size / 2 + 1 + a->clip + fscL;
		if (begin0 > a->out_size)
			begin1 = begin0 - a->out_size;
		else
			begin1 = 0;
	}
	else
	{
		begin0 = a->out_size / 2 + 1 + a->clip;
		begin1 = 0;
	}
	if (ss == a->end_ss)
	{
		end1 = a->out_size / 2 - a->clip - fscH;
		if (end1 < 0)
			end0 = a->out_size + end1;
		else
			end0 = a->out_size;
	}
	else
	{
		end0 = a->out_size;
		end1 = a->out_size / 2 - a->clip;
	}

	ilim = a->out_size - 1;

	if (a->flip[LO])
	{
		for (i = ilim - begin0, k = 0; i > ilim - end0; i--, k++)
		{
			mag = (a->fft_out[ss][LO])[i][0] * (a->fft_out[ss][LO])[i][0] + (a->fft_out[ss][LO])[i][1] * (a->fft_out[ss][LO])[i][1];
			if ((a->spec_flag[ss] == 0) || (mag < (a->result[ss])[k]))
				(a->result[ss])[k] = mag;
		}
		for (i = ilim - begin1; i > ilim - end1; i--, k++)
		{
			mag = (a->fft_out[ss][LO])[i][0] * (a->fft_out[ss][LO])[i][0] + (a->fft_out[ss][LO])[i][1] * (a->fft_out[ss][LO])[i][1];
			if ((a->spec_flag[ss] == 0) || (mag < (a->result[ss])[k]))
				(a->result[ss])[k] = mag;
		}
	}
	else
	{
		for (i = begin0, k = 0; i < end0; i++, k++)
		{
			mag = (a->fft_out[ss][LO])[i][0] * (a->fft_out[ss][LO])[i][0] + (a->fft_out[ss][LO])[i][1] * (a->fft_out[ss][LO])[i][1];
			if ((a->spec_flag[ss] == 0) || (mag < (a->result[ss])[k]))
				(a->result[ss])[k] = mag;
		}
		for (i = begin1; i < end1; i++, k++)
		{
			mag = (a->fft_out[ss][LO])[i][0] * (a->fft_out[ss][LO])[i][0] + (a->fft_out[ss][LO])[i][1] * (a->fft_out[ss][LO])[i][1];
			if ((a->spec_flag[ss] == 0) || (mag < (a->result[ss])[k]))
				(a->result[ss])[k] = mag;
		}
	}
	a->ss_bins[ss] = k;
}

void detector (	int det_type,			// detector type
				int m,					// number of bins
				int num_pixels,			// number of output pixels
				double pix_per_bin,		// pixels per bin
				double bin_per_pix,		// bins per pixel
				double* bins,			// input buffer
				double* pixels,			// output buffer
				double inv_enb,			// inverse equivalent noise bandwidth
				double fsclipL,
				double fsclipH,
				double det_offset
				)
{
	int i, imin, ilim;
	int pix_count = 0;
	int rose, fell, next_pix_count, bcount, last_pix_count;
	double prev_maxi, mini, maxi, psum;
	if (pix_per_bin <= 1.0)
	{
		if (fsclipL == floor(fsclipL)) imin = 0;
		else  imin = 1;
		if (fsclipH == floor(fsclipH)) ilim = m;
		else  ilim = m - 1;
		switch (det_type)
		{

		case 0:		// positive peak
			for (i = 0; i < num_pixels; i++)
				pixels[i]   = - 1.0e300;

			for (i = imin; i < ilim; i++)
			{
				pix_count = (int)(det_offset + (double)i * pix_per_bin);
				if (pix_count >= num_pixels) pix_count = num_pixels - 1;
				if (bins[i] > pixels[pix_count])
					pixels[pix_count] = bins[i];
			}
			break;

		case 1:		// rosenfell
			rose         = 0;
			fell         = 0;
			mini         = + 1.0e300;
			maxi         = - 1.0e300;
			prev_maxi    = - 1.0e300;

			for (i = imin; i < ilim; i++)		// for each FFT bin
			{
				// determine the pixel number that this FFT bin goes into
				pix_count = (int)(det_offset + (double)i * pix_per_bin);
				if (pix_count >= num_pixels) pix_count = num_pixels - 1;
				// determine the pixel number for the NEXT FFT bin
				next_pix_count = (int)((double)(i + 1) * pix_per_bin);
				// update the minimum and maximum of the set of bins within the pixel
				if (bins[i] <   mini)     mini = bins[i];
				if (bins[i] >   maxi)     maxi = bins[i];
				// if the next bin is also within the pixel && there is a next bin,
				//    compare its value with the current bin and update rose and fell
				if (next_pix_count == pix_count && i < ilim - 1)
				{
					// NOTE:  when next_pix_count != pix_count, rose and fell do not get updated;
					//    that's OK because we do NOT need to know if there's a rise or fall across bins
					if (bins[i + 1] > bins[i]) rose    = 1;
					if (bins[i + 1] < bins[i]) fell    = 1;
				}
				// if the next bin is NOT within the pixel || there is no next bin, finalize the pixel 
				//    value and reset parameters
				else
				{
					if (rose && fell)
						if (pix_count & 1)				// odd pixel
							pixels[pix_count] = max (prev_maxi, maxi);
						else							// even pixel
							pixels[pix_count] = mini;
					else
						pixels[pix_count] = maxi;
					rose = 0;
					fell = 0;
					prev_maxi = maxi;
					mini = + 1.0e300;
					maxi = - 1.0e300;
				}
			}
			break;

		case 2:		// average - adjusted for window's equivalent noise bandwidth
			psum = 0.0;
			bcount = 0;
			for (i = imin; i < ilim; i++)
			{
				last_pix_count = pix_count;
				pix_count = (int)(det_offset + (double)i * pix_per_bin);
				if (pix_count >= num_pixels) pix_count = num_pixels - 1;
				if (pix_count == last_pix_count)
				{
					psum += bins[i];
					bcount++;
				}
				else
				{
					pixels[last_pix_count] = psum / (double)bcount * inv_enb;
					psum = bins[i];
					bcount = 1;
				}
				if (i == ilim - 1)
				{
					pixels[pix_count] = psum / (double)bcount * inv_enb;
				}
			}
			break;

		case 3:		// sample - adjusted for window's equivalent noise bandwidth
			bcount = 0;
			for (i = imin; i < ilim; i++)
			{
				last_pix_count = pix_count;
				pix_count = (int)(det_offset + (double)i * pix_per_bin);
				if (pix_count >= num_pixels) pix_count = num_pixels - 1;
				if (pix_count == last_pix_count)
				{
					bcount++;
				}
				else
				{
					pixels[last_pix_count] = bins[i - bcount / 2 - 1] * inv_enb;
					bcount = 1;
				}
				if (i == ilim - 1)
				{
					pixels[pix_count] = bins[i - bcount / 2] * inv_enb;
				}
			}
			break;

		case 4:		// rms
			psum = 0.0;
			bcount = 0;
			for (i = imin; i < ilim; i++)
			{
				last_pix_count = pix_count;
				pix_count = (int)(det_offset + (double)i * pix_per_bin);
				if (pix_count >= num_pixels) pix_count = num_pixels - 1;
				if (pix_count == last_pix_count)
				{
					psum += bins[i] * bins[i];
					bcount++;
				}
				else
				{
					pixels[last_pix_count] = sqrt (psum / (double)bcount) * inv_enb;
					psum = bins[i] * bins[i];
					bcount = 1;
				}
				if (i == ilim - 1)
				{
					pixels[pix_count] = sqrt (psum / (double)bcount) * inv_enb;
				}
			}
			break;
		}
	}
	else
	{
		double frac;
		double pix_pos = fsclipL - floor(fsclipL);
		int ampl_comp = (det_type == 2) || (det_type == 3) || (det_type == 4);
		for (i = 1; i < m; i++)
		{
			while (pix_pos < ((double)i + 1.0e-06) && pix_count < num_pixels)
			{
				frac = pix_pos - (double)(i - 1);
				pixels[pix_count]   = bins[i - 1] * (1.0 - frac) + bins[i] * frac;
				if (ampl_comp) pixels[pix_count] *= inv_enb;
				pix_count++;
				pix_pos += bin_per_pix;
			}
		}
	}
	
}

void avenger (  int av_mode,				// averaging mode
				int num_pixels,				// number of pixels
				int* avail_frames,			// number of available frames for window averaging
				int num_average,			// number of frames to average within a window
				int* av_in_idx,				// in index for av_buff
				int* av_out_idx,			// out index for av_buff
				double av_backmult,			// multiplier for recursive averaging
				double scale,				// scale factor
				double* t_pixels,			// input buffer
				double* av_sum,				// history buffer for averaging
				double** av_buff,			// frame buffer for window averaging
				double* cd,					// correction factor buffer
				int norm,					// if TRUE, normalize to one Hz bandwidth
				double norm_oneHz,			// normalization factor to add
				dOUTREAL* pixels			// output buffer
	)
{
	int i;
	double factor;
	switch (av_mode)
	{
	case -1:	// peak-hold
		{
			for (i = 0; i < num_pixels; i++)
			{
				if (t_pixels[i] > av_sum[i])
					av_sum[i] = t_pixels[i];
				pixels[i] = (dOUTREAL)(10.0 * mlog10(scale * cd[i] * av_sum[i] + 1.0e-60));
			}
			break;
		}
	case 0:		// no averaging
	default:
		{
			for (i = 0; i < num_pixels; i++)
				pixels[i] = (dOUTREAL)(10.0 * mlog10(scale * cd[i] * t_pixels[i] + 1.0e-60));
			break;
		}
	case 1:		// weighted averaging of linear data
		{
			double onem_avb = 1.0 - av_backmult;
			for (i = 0; i < num_pixels; i++)
			{
				av_sum[i] = av_backmult * av_sum[i] + onem_avb * t_pixels[i];
				pixels[i] = (dOUTREAL)(10.0 * mlog10(scale * cd[i] * av_sum[i] + 1.0e-60));
			}
			break;
		}
	case 2:		// window averaging of linear data
		{
			if (*avail_frames < num_average)
			{
				factor = scale / (double)++(*avail_frames);
				for (i = 0; i < num_pixels; i++)
				{
					av_sum[i] += t_pixels[i];
					av_buff[*av_in_idx][i] = t_pixels[i];
					pixels[i] = (dOUTREAL)(10.0 * mlog10(cd[i] * av_sum[i] * factor + 1.0e-60));
				}
			}
			else
			{
				factor = scale / (double)(*avail_frames);
				for (i = 0; i < num_pixels; i++)
				{
					av_sum[i] += t_pixels[i] - (av_buff[*av_out_idx])[i];
					av_buff[*av_in_idx][i] = t_pixels[i];
					pixels[i] = (dOUTREAL)(10.0 * mlog10(cd[i] * av_sum[i] * factor + 1.0e-60));
				}
				if (++(*av_out_idx) == dMAX_AVERAGE)
						*av_out_idx = 0;
			}
			if (++(*av_in_idx) == dMAX_AVERAGE)
				*av_in_idx = 0;
			break;
		}
	case 3:		// weighted averaging of log data - looks nice, not accurate for time-varying signals
		{
			double onem_avb = 1.0 - av_backmult;
			for (i = 0; i < num_pixels; i++)
			{
				av_sum[i] = av_backmult * av_sum[i] + onem_avb * (10.0 * mlog10(scale * cd[i] * t_pixels[i] + 1e-60));
				pixels[i] = (dOUTREAL)av_sum[i];
			}
			break;
		}
	}
	if (norm)
		for (i = 0; i < num_pixels; i++)
			pixels[i] += (dOUTREAL)norm_oneHz;
}

/********************************************************************************************************
*																										*
*								SMOOTH PAN / RETUNE (Jull1ano fork)										*
*																										*
*	The averaging state of each pixel output lives on a grid anchored in absolute frequency:			*
*	av pixel j is at av_low_hz + j * av_hzpp.  When the view (pan, zoom) or the LO moves, the state		*
*	is shifted by whole pixels or resampled, never smeared or restarted.  Pixels without data are		*
*	marked invalid, shown at the noise floor and seeded by the first fresh value.						*
*	Every LO change is marked in the input sample stream (SetPixelRef).  Samples older than the last	*
*	LO change inside an fft window are frequency-rotated onto the newest LO (phase continuous, like		*
*	the radio's NCO), so frames stay valid while the LO moves; the band edge the old samples cannot		*
*	cover is marked invalid for that frame.  All of this needs complex data and one sub-span.			*
*																										*
********************************************************************************************************/

typedef struct _sm_frame
{
	int smooth;			// frame was prepared by sm_fill_window()
	int skip;			// frame has too little valid span (big LO jump inside the window)
	double lo;			// LO the frame is computed for, MHz
	double vlo_hz;		// valid part of the frame, Hz relative to lo
	double vhi_hz;
} sm_frame;

static int sm_geometry(DP a, double* low_rel_hz, double* hzpp)
{
	double bw;
	int bins;
	if (a->sample_rate <= 0 || a->size <= 0 || a->num_pixels < 2)
		return 0;
	bw = (double)a->sample_rate / (double)a->size;
	bins = a->num_stitch * (a->out_size - 1 - 2 * a->clip);
	*low_rel_hz = (-0.5 * (double)(bins - 1) + a->fsclipL) * bw;
	*hzpp = a->bin_per_pix * bw;
	return *hzpp > 0.0;
}

static double sm_view_centre_mhz(DP a, double lo_mhz)
{
	double bw = (a->size > 0) ? (double)a->sample_rate / (double)a->size : 0.0;
	return lo_mhz + 0.5 * (a->fsclipL - a->fsclipH) * bw * 1.0e-6;
}

static double sm_db(DP a, int i, int j)
{
	double* av = a->av_sum[i];
	switch (a->av_mode[i])
	{
	case 3:
		return av[j];
	case 2:
		if (a->avail_frames[i] <= 0) return -200.0;
		return 10.0 * mlog10(a->cd[j] * av[j] * a->scale / (double)a->avail_frames[i] + 1.0e-60);
	default:
		return 10.0 * mlog10(a->scale * a->cd[j] * av[j] + 1.0e-60);
	}
}

static void sm_invalidate(DP a, int i)
{
	memset(a->av_valid[i], 0, dMAX_PIXELS);
}

static void sm_shift_d(double* arr, int n, int k)	// arr[j] = arr[j + k]
{
	if (k > 0)
		memmove(arr, arr + k, (n - k) * sizeof(double));
	else if (k < 0)
		memmove(arr - k, arr, (n + k) * sizeof(double));
}

static void sm_shift_b(unsigned char* arr, int n, int k)
{
	if (k > 0)
	{
		memmove(arr, arr + k, n - k);
		memset(arr + n - k, 0, k);
	}
	else if (k < 0)
	{
		memmove(arr - k, arr, n + k);
		memset(arr, 0, -k);
	}
}

static void sm_resample(DP a, int i, double new_low, double new_hzpp, int new_n)
{
	int j, k, x0, n_old = a->av_n[i];
	double x, fr, mean;
	double old_low = a->av_low_hz[i], old_hzpp = a->av_hzpp[i];
	double* src = a->av_sum[i];
	double* dst = a->sm_tmp;
	unsigned char* vs = a->av_valid[i];
	unsigned char* vd = a->sm_vtmp;
	for (j = 0; j < new_n; j++)
	{
		x = (new_low + (double)j * new_hzpp - old_low) / old_hzpp;
		x0 = (int)floor(x + 1.0e-9);
		fr = x - (double)x0;
		if (fr < 1.0e-9) fr = 0.0;
		vd[j] = 0;
		dst[j] = 0.0;
		if (x0 < 0 || x0 >= n_old)
			continue;
		if (fr == 0.0 || x0 + 1 >= n_old)
		{
			if (fr == 0.0 && vs[x0]) { dst[j] = src[x0]; vd[j] = 1; }
		}
		else if (vs[x0] && vs[x0 + 1])
		{
			dst[j] = src[x0] * (1.0 - fr) + src[x0 + 1] * fr;
			vd[j] = 1;
		}
		else if (vs[x0] && fr < 0.5) { dst[j] = src[x0]; vd[j] = 1; }
		else if (vs[x0 + 1] && fr >= 0.5) { dst[j] = src[x0 + 1]; vd[j] = 1; }
	}
	memcpy(src, dst, new_n * sizeof(double));
	memcpy(vs, vd, new_n);
	if (new_n < dMAX_PIXELS) memset(vs + new_n, 0, dMAX_PIXELS - new_n);
	if (a->av_mode[i] == 2)
	{
		// window average: rebuild a flat history that has the resampled mean
		for (j = 0; j < new_n; j++)
		{
			mean = (vs[j] && a->avail_frames[i] > 0) ? src[j] / (double)a->avail_frames[i] : 0.0;
			if (!vs[j]) src[j] = 0.0;
			for (k = 0; k < dMAX_AVERAGE; k++)
				a->av_buff[i][k][j] = mean;
		}
	}
	a->av_low_hz[i] = new_low;
	a->av_hzpp[i] = new_hzpp;
	a->av_n[i] = new_n;
}

// move the averaging grid of pixel output i onto the grid that starts at new_low
static void sm_realign(DP a, int i, double new_low, double new_hzpp, int new_n)
{
	long long k;
	int j;
	if (new_n < 2 || new_n > dMAX_PIXELS || new_hzpp <= 0.0)
		return;
	if (a->av_hzpp[i] <= 0.0 || a->av_n[i] < 2)
	{
		sm_invalidate(a, i);
		a->av_low_hz[i] = new_low;
		a->av_hzpp[i] = new_hzpp;
		a->av_n[i] = new_n;
		return;
	}
	if (new_n == a->av_n[i] && fabs(new_hzpp - a->av_hzpp[i]) <= 1.0e-9 * new_hzpp)
	{
		k = llround((new_low - a->av_low_hz[i]) / new_hzpp);
		if (k == 0)
			return;
		if (llabs(k) >= new_n)
		{
			sm_invalidate(a, i);
			a->av_low_hz[i] = new_low;
			return;
		}
		sm_shift_d(a->av_sum[i], new_n, (int)k);
		sm_shift_b(a->av_valid[i], new_n, (int)k);
		if (a->av_mode[i] == 2)
			for (j = 0; j < dMAX_AVERAGE; j++)
				sm_shift_d(a->av_buff[i][j], new_n, (int)k);
		a->av_low_hz[i] += (double)k * new_hzpp;
	}
	else
		sm_resample(a, i, new_low, new_hzpp, new_n);
}

static double sm_select(double* v, int n, int kth)	// k-th smallest, reorders v
{
	int lo = 0, hi = n - 1, l, r;
	double pivot, t;
	while (lo < hi)
	{
		pivot = v[(lo + hi) >> 1];
		l = lo; r = hi;
		while (l <= r)
		{
			while (v[l] < pivot) l++;
			while (v[r] > pivot) r--;
			if (l <= r) { t = v[l]; v[l] = v[r]; v[r] = t; l++; r--; }
		}
		if (kth <= r) hi = r;
		else if (kth >= l) lo = l;
		else break;
	}
	return v[kth];
}

// publish the averaging state of pixel output i as a new pixel frame
static void sm_publish(DP a, int i, double tag_mhz)
{
	int j, c = 0, n = a->num_pixels;
	double fl;
	double* db = a->sm_tmp;
	double* tmp = a->sm_tmp2;
	unsigned char* v = a->av_valid[i];
	dOUTREAL* out;
	if (n != a->av_n[i] || n < 2)
		return;
	for (j = 0; j < n; j++)
		if (v[j])
		{
			db[j] = sm_db(a, i, j);
			tmp[c++] = db[j];
		}
	if (c >= 16)
		a->av_floor[i] = sm_select(tmp, c, c / 5);	// noise floor = 20th percentile of the pixels with data
	else if (a->av_floor[i] > 1.0e29)
		return;										// no data yet and no floor to show
	fl = a->av_floor[i];
	out = a->pixels[i][a->w_pix_buff[i]];
	for (j = 0; j < n; j++)
		out[j] = (dOUTREAL)(v[j] ? db[j] : fl);
	if (a->normalize[i])
		for (j = 0; j < n; j++)
			out[j] += (dOUTREAL)a->norm_oneHz;

	EnterCriticalSection(&a->PB_ControlsSection[i]);
	EnterCriticalSection(&a->PixelRefSection);
	a->pixel_refs[i][a->w_pix_buff[i]] = tag_mhz;
	LeaveCriticalSection(&a->PixelRefSection);
	a->last_pix_buff[i] = a->w_pix_buff[i];
	while ((a->w_pix_buff[i] = (a->w_pix_buff[i] + 1) % dNUM_PIXEL_BUFFS) == a->r_pix_buff[i]);
	LeaveCriticalSection(&a->PB_ControlsSection[i]);
	InterlockedBitTestAndSet(&(a->pb_ready[i][a->last_pix_buff[i]]), 0);
}

// average a fresh frame t (frame pixel p <-> averaging pixel p + d) into pixel output i;
// only frame pixels pv0..pv1 hold valid data
static void sm_accumulate(DP a, int i, const double* t, int n, double d, int pv0, int pv1)
{
	int j, k, x0, mode = a->av_mode[i];
	double x, fr, val;
	double* av = a->av_sum[i];
	double* ft = a->sm_tmp;
	unsigned char* v = a->av_valid[i];
	unsigned char* vt = a->sm_vtmp;
	double b = a->av_backmult[i], omb = 1.0 - a->av_backmult[i];

	for (j = 0; j < n; j++)
	{
		vt[j] = 0;
		x = (double)j - d;
		if (x < (double)pv0 - 1.0e-6 || x > (double)pv1 + 1.0e-6)
			continue;
		x0 = (int)floor(x + 1.0e-6);
		if (x0 < pv0) x0 = pv0;
		fr = x - (double)x0;
		if (fr < 1.0e-6 || x0 >= pv1)
			val = t[x0 > pv1 ? pv1 : x0];
		else
			val = t[x0] * (1.0 - fr) + t[x0 + 1] * fr;
		ft[j] = val;
		vt[j] = 1;
	}

	switch (mode)
	{
	case -1:	// peak hold
		for (j = 0; j < n; j++)
			if (vt[j] && (!v[j] || ft[j] > av[j])) { av[j] = ft[j]; v[j] = 1; }
		break;
	case 1:		// weighted averaging of linear data
		for (j = 0; j < n; j++)
			if (vt[j])
			{
				if (!v[j]) { av[j] = ft[j]; v[j] = 1; }
				else av[j] = b * av[j] + omb * ft[j];
			}
		break;
	case 2:		// window averaging of linear data
		{
			int full = a->avail_frames[i] >= a->num_average[i];
			int avail = a->avail_frames[i];
			double* inb = a->av_buff[i][a->av_in_idx[i]];
			double* outb = a->av_buff[i][a->av_out_idx[i]];
			for (j = 0; j < n; j++)
			{
				if (vt[j])
				{
					val = ft[j];
					if (!v[j])
					{
						for (k = 0; k < dMAX_AVERAGE; k++)
							a->av_buff[i][k][j] = val;
						av[j] = val * (double)avail;
						v[j] = 1;
					}
				}
				else if (v[j])
					val = avail > 0 ? av[j] / (double)avail : 0.0;	// no data here: keep the mean
				else
				{
					inb[j] = 0.0;
					continue;
				}
				if (full) av[j] += val - outb[j];
				else av[j] += val;
				inb[j] = val;
			}
			if (!full) a->avail_frames[i]++;
			else if (++a->av_out_idx[i] == dMAX_AVERAGE) a->av_out_idx[i] = 0;
			if (++a->av_in_idx[i] == dMAX_AVERAGE) a->av_in_idx[i] = 0;
		}
		break;
	case 3:		// weighted averaging of log data
		for (j = 0; j < n; j++)
			if (vt[j])
			{
				val = 10.0 * mlog10(a->scale * a->cd[j] * ft[j] + 1e-60);
				if (!v[j]) { av[j] = val; v[j] = 1; }
				else av[j] = b * av[j] + omb * val;
			}
		break;
	case 0:		// no averaging: the state is the last frame
	default:
		for (j = 0; j < n; j++)
			if (vt[j]) { av[j] = ft[j]; v[j] = 1; }
		break;
	}
}

// move all pixel outputs onto the requested view (current LO + current geometry); publish if asked
static void sm_follow_view(DP a, int publish)
{
	int i;
	double low_rel, hzpp, lo;
	if (!sm_geometry(a, &low_rel, &hzpp))
		return;
	EnterCriticalSection(&a->PixelRefSection);
	lo = a->pixel_ref;
	LeaveCriticalSection(&a->PixelRefSection);
	for (i = 0; i < a->num_pixout; i++)
	{
		sm_realign(a, i, lo * 1.0e6 + low_rel, hzpp, a->num_pixels);
		if (publish && (a->publish_mask & (1 << i)))
			sm_publish(a, i, sm_view_centre_mhz(a, lo));
	}
}

// copy the window into Cfft_in[ss][LO], rotating samples taken before the last LO change onto the newest LO
static void sm_fill_window(DP a, int ss, int LO, sm_frame* fi)
{
	static const double TWOPI_ = 6.283185307179586476925286766559;
	long long ws = a->win_start[ss][LO];
	int size = a->size;
	int i, m, nev = 0, start, end, idx = a->IQO_idx[ss][LO];
	long long eidx[dMAX_RETUNE_EV];
	double elo[dMAX_RETUNE_EV];
	double Fs[dMAX_RETUNE_EV + 1], Gs[dMAX_RETUNE_EV + 1];
	double lo0, lo_m, lo_m1, Fmax = 0.0, Fmin = 0.0, F, half, fs = (double)a->sample_rate;
	double sgn = a->flip[LO] ? 1.0 : -1.0;
	dINREAL* I = a->I_samples[ss][LO];
	dINREAL* Q = a->Q_samples[ss][LO];
	fftw_complex* out = a->Cfft_in[ss][LO];

	EnterCriticalSection(&a->PixelRefSection);
	while (a->ev_count > 0 && a->ev_idx[0] <= ws)
	{
		a->base_lo = a->ev_lo[0];
		a->ev_count--;
		memmove(a->ev_idx, a->ev_idx + 1, a->ev_count * sizeof(long long));
		memmove(a->ev_lo, a->ev_lo + 1, a->ev_count * sizeof(double));
	}
	lo0 = a->base_lo;
	for (m = 0; m < a->ev_count && a->ev_idx[m] < ws + size; m++)
	{
		eidx[nev] = a->ev_idx[m] - ws;
		elo[nev] = a->ev_lo[m];
		nev++;
	}
	LeaveCriticalSection(&a->PixelRefSection);

	fi->smooth = 1;
	fi->skip = 0;
	fi->lo = nev ? elo[nev - 1] : lo0;
	half = 0.5 * fs * (double)(size - 2 * a->clip) / (double)size;
	fi->vlo_hz = -1.0e12;
	fi->vhi_hz = 1.0e12;

	if (nev > 0 && fs > 0.0)
	{
		// Fs[m]: rotation frequency (Hz) of segment m = newest LO - LO of segment m
		// Gs[m]: sum of (LO step * step position) over the steps after segment m, so phase(s) = sgn*2pi/fs*(Fs*s - Gs)
		Fs[nev] = 0.0;
		Gs[nev] = 0.0;
		for (m = nev - 1; m >= 0; m--)
		{
			lo_m = (m == 0) ? lo0 : elo[m - 1];
			lo_m1 = elo[m];
			F = (lo_m1 - lo_m) * 1.0e6;
			Fs[m] = Fs[m + 1] + F;
			Gs[m] = Gs[m + 1] + F * (double)eidx[m];
			if (Fs[m] > Fmax) Fmax = Fs[m];
			if (Fs[m] < Fmin) Fmin = Fs[m];
		}
		// old samples cover [lo_m - half, lo_m + half]; in the newest LO's terms that is [-F - half, -F + half]
		fi->vlo_hz = -Fmin - half;
		fi->vhi_hz = -Fmax + half;
		if (fi->vhi_hz - fi->vlo_hz < 0.1 * fs)
			fi->skip = 1;
	}

	if (nev == 0 || fi->skip)
	{
		for (i = 0; i < size; i++)
		{
			out[i][0] = a->window[i] * (double)I[idx];
			out[i][1] = a->window[i] * (double)Q[idx];
			if (++idx >= a->bsize) idx -= a->bsize;
		}
	}
	else
	{
		for (m = 0; m <= nev; m++)
		{
			start = (m == 0) ? 0 : (int)eidx[m - 1];
			end = (m == nev) ? size : (int)eidx[m];
			if (start < 0) start = 0;
			if (end > size) end = size;
			if (Fs[m] == 0.0)
			{
				for (i = start; i < end; i++)
				{
					out[i][0] = a->window[i] * (double)I[idx];
					out[i][1] = a->window[i] * (double)Q[idx];
					if (++idx >= a->bsize) idx -= a->bsize;
				}
			}
			else
			{
				double w = sgn * TWOPI_ * Fs[m] / fs;
				double cw = cos(w), sw = sin(w);
				double c = 1.0, s = 0.0, t, xi, xq;
				for (i = start; i < end; i++)
				{
					if (((i - start) & 1023) == 0)
					{
						double ph = sgn * TWOPI_ / fs * (Fs[m] * (double)i - Gs[m]);
						ph = fmod(ph, TWOPI_);
						c = cos(ph);
						s = sin(ph);
					}
					xi = (double)I[idx];
					xq = (double)Q[idx];
					out[i][0] = a->window[i] * (xi * c - xq * s);
					out[i][1] = a->window[i] * (xi * s + xq * c);
					t = c;
					c = t * cw - s * sw;
					s = t * sw + s * cw;
					if (++idx >= a->bsize) idx -= a->bsize;
				}
			}
		}
	}
	a->IQO_idx[ss][LO] = idx;
}

static void sm_stitch(int disp, sm_frame* fi)
{
	DP a = pdisp[disp];
	int i, j, k, m, n, pv0, pv1;
	double low_rel, hzpp, lo_req, tag;

	EnterCriticalSection(&a->ResampleSection);
	if (!fi->skip && sm_geometry(a, &low_rel, &hzpp))
	{
		n = a->num_pixels;
		m = a->ss_bins[0] - a->fscL - a->fscH;
		if (m > 1)
		{
			memcpy(a->pre_av_out, a->result[0] + a->fscL, m * sizeof(double));
			EnterCriticalSection(&a->PixelRefSection);
			lo_req = a->pixel_ref;
			LeaveCriticalSection(&a->PixelRefSection);
			tag = sm_view_centre_mhz(a, lo_req);

			{
				double p0 = ceil((fi->vlo_hz - low_rel) / hzpp - 1.0e-6);
				double p1 = floor((fi->vhi_hz - low_rel) / hzpp + 1.0e-6);
				if (p0 < 0.0) p0 = 0.0;
				if (p1 > (double)(n - 1)) p1 = (double)(n - 1);
				if (p0 > (double)n) p0 = (double)n;
				if (p1 < -1.0) p1 = -1.0;
				pv0 = (int)p0;
				pv1 = (int)p1;
			}

			for (i = 0; i < a->num_pixout; i++)
			{
				j = i - 1;
				k = i;
				while (j >= 0)
				{
					if (a->det_type[i] == a->det_type[j])
						k = j;
					j--;
				}
				if (k == i)
					detector(a->det_type[i], m, n, a->pix_per_bin, a->bin_per_pix, a->pre_av_out,
						a->t_pixels[i], a->inv_enb, a->fsclipL, a->fsclipH, a->det_offset);
				else
					memcpy(a->t_pixels[i], a->t_pixels[k], n * sizeof(double));

				sm_realign(a, i, lo_req * 1.0e6 + low_rel, hzpp, n);
				if (pv0 <= pv1)
					sm_accumulate(a, i, a->t_pixels[i], n,
						(fi->lo * 1.0e6 + low_rel - a->av_low_hz[i]) / hzpp, pv0, pv1);
				sm_publish(a, i, tag);
			}
		}
	}
	LeaveCriticalSection(&a->ResampleSection);
}

void stitch(int disp)
{
	DP a = pdisp[disp];
	int i, j, k, n, m;
	double pixel_ref;
	double* ptr;

	// stitch
	m = 0;
	ptr = a->pre_av_out;
	for (n = a->begin_ss; n <= a->end_ss; n++)
	{
		memcpy(ptr, a->result[n], a->ss_bins[n] * sizeof(double));
		ptr += a->ss_bins[n];
		m += a->ss_bins[n];
	}

	EnterCriticalSection(&a->PixelRefSection);
	pixel_ref = a->pixel_ref;
	LeaveCriticalSection(&a->PixelRefSection);

	for (i = 0; i < a->num_pixout; i++)	// for each output
	{
		EnterCriticalSection(&a->ResampleSection);
		// if a detection of the same 'det_type' has already been done, use that result
		j = i - 1;
		k = i;
		while (j >= 0)
		{
			if (a->det_type[i] == a->det_type[j])
				k = j;
			j--;
		}
		if (k == i)
			// detect
			detector (a->det_type[i], m, a->num_pixels, a->pix_per_bin, a->bin_per_pix, a->pre_av_out, 
				a->t_pixels[i], a->inv_enb, a->fsclipL, a->fsclipH, a->det_offset);
		else
			memcpy (a->t_pixels[i], a->t_pixels[k], a->num_pixels * sizeof (double));
		// average & convert to dBm
		avenger (a->av_mode[i], a->num_pixels, &a->avail_frames[i], a->num_average[i], &a->av_in_idx[i], &a->av_out_idx[i],
			a->av_backmult[i], a->scale, a->t_pixels[i], a->av_sum[i], a->av_buff[i], a->cd, a->normalize[i], a->norm_oneHz,
			a->pixels[i][a->w_pix_buff[i]]);
		LeaveCriticalSection(&a->ResampleSection);

		EnterCriticalSection(&a->PB_ControlsSection[i]);
		EnterCriticalSection(&a->PixelRefSection);
		a->pixel_refs[i][a->w_pix_buff[i]] = pixel_ref;
		LeaveCriticalSection(&a->PixelRefSection);
		a->last_pix_buff[i] = a->w_pix_buff[i];	
		while ((a->w_pix_buff[i] = (a->w_pix_buff[i] + 1) % dNUM_PIXEL_BUFFS) == a->r_pix_buff[i]);
		LeaveCriticalSection(&a->PB_ControlsSection[i]);
		InterlockedBitTestAndSet(&(a->pb_ready[i][a->last_pix_buff[i]]), 0);
	}
}

DWORD WINAPI spectra (void *pargs)
{
	int i, j;
	int disp = ((int)(uintptr_t)pargs) >> 12;
	int ss = (((int)(uintptr_t)pargs) >> 4) & 255;
	int LO = ((int)(uintptr_t)pargs) & 15;
	DP a = pdisp[disp];

	if (a->stop)
	{
		InterlockedDecrement(a->pnum_threads);
		return 0;
	}

	if ((ss >= a->begin_ss) && (ss <= a->end_ss))
	{
		for (i = 0; i < a->size; i++)
		{
			(a->fft_in[ss][LO])[i] = a->window[i] * (double)((a->I_samples[ss][LO])[a->IQO_idx[ss][LO]]);
			if(++a->IQO_idx[ss][LO] >= a->bsize)
				 a->IQO_idx[ss][LO] -= a->bsize;
		}

		if (a->stop)
		{
			InterlockedDecrement(a->pnum_threads);
			return 0;
		}
		fftw_execute (a->plan[ss][LO]);
	}
	if (a->stop)
	{
		InterlockedDecrement(a->pnum_threads);
		return 0;
	}

	EnterCriticalSection(&(a->EliminateSection[ss]));
	if ((ss >= a->begin_ss) && (ss <= a->end_ss))
		eliminate(disp, ss, LO);
	a->spec_flag[ss] |= 1 << LO;

	if (a->spec_flag[ss] == ((1 << a->num_fft) - 1))
	{
		a->spec_flag[ss] = 0;
		LeaveCriticalSection (&(a->EliminateSection[ss]));

		EnterCriticalSection (&a->StitchSection);
		a->stitch_flag |= ((uint64_t)1) << ss;

		if (a->stitch_flag == ((((uint64_t)1) << a->num_stitch) - 1))
		{
			a->stitch_flag = 0;
			LeaveCriticalSection(&a->StitchSection);
			for (j = 0; j < dMAX_STITCH; j++)
				for (i = 0; i < dMAX_NUM_FFT; i++)
					InterlockedBitTestAndReset(&(a->input_busy[j][i]), 0);
			stitch(disp);
		}
		else
			LeaveCriticalSection(&a->StitchSection);
	}
	else
		LeaveCriticalSection (&(a->EliminateSection[ss]));

	InterlockedDecrement(a->pnum_threads);
	return 1;
}

/********************************************************************************************************
*																										*
*							BEGIN CODE TO GET MAX FFT_BIN WITHIN A FREQ RANGE							*
*																										*
********************************************************************************************************/

// Call in XCreateAnalyzer(...)
// This gets initialized for each 'disp' that is set up.
void Init_DetectMaxBin(int disp)
{
	DP a = pdisp[disp];
	InitializeCriticalSectionAndSpinCount(&a->cs_dmb, 2500);
	a->dmb_run = 0;
	a->dmb_disp = disp;
	a->dmb_ss = 0;
	a->dmb_LO = 0;
	a->dmb_rate = 48000.0;
	a->dmb_fLow = 0.0;
	a->dmb_fHigh = 0.0;
	a->dmb_begin0 = 0;
	a->dmb_end0 = -1;
	a->dmb_begin1 = 0;
	a->dmb_end1 = -1;
	a->dmb_max_dB = -400.0;
}

// Call in DestroyAnalyzer(...)
void Destroy_DetectMaxBin(int disp)
{
	DP a = pdisp[disp];
	DeleteCriticalSection(&a->cs_dmb);
}

// Called from SetupDetectMaxBin(...) AND anytime 'size' changes, e.g., in SetAnalyzer(...)
void calc_dmb(int disp, int size)
{
	DP a = pdisp[disp];
	double bin_spacing, min_freq, max_freq;
	bin_spacing = a->dmb_rate / size;
	min_freq = -a->dmb_rate * (double)((size / 2 - 1)) / (double)size;
	max_freq = -min_freq;
	if (a->dmb_fLow < min_freq) a->dmb_fLow = min_freq;
	if (a->dmb_fLow > max_freq) a->dmb_fLow = max_freq;
	if (a->dmb_fHigh < min_freq) a->dmb_fHigh = min_freq;
	if (a->dmb_fHigh > max_freq) a->dmb_fHigh = max_freq;

	if (a->dmb_fLow < 0.0 && a->dmb_fHigh < 0.0)
	{
		a->dmb_begin0 = size - (int)ceil(-a->dmb_fLow / bin_spacing);
		// Example:  1024, 48K, -2700 => 966
		// Example:  1024, 48K, -23953.125==min_freq => 513
		// NOTE:  since dmb_fLow < 0.0, begin0 will never be greater than a->size - 1
		a->dmb_end0 = size - (int)ceil(-a->dmb_fHigh / bin_spacing);
		// Example:  1024, 48K, -300  => 1017
		a->dmb_begin1 = 0;
		a->dmb_end1 = -1;
	}
	else if (a->dmb_fLow >= 0.0 && a->dmb_fHigh >= 0.0)
	{
		a->dmb_begin0 = (int)round(a->dmb_fLow / bin_spacing);
		// Example:  1024, 48K, +300  => 6
		a->dmb_end0 = (int)round(a->dmb_fHigh / bin_spacing);
		// Example:  1024, 48K, +2700 => 58
		// Example:  1024, 48K, 23953.125==max_freq => 511
		a->dmb_begin1 = 0;
		a->dmb_end1 = -1;
	}
	else	// fLow < 0.0 && fHigh >= 0.0
	{
		a->dmb_begin0 = size - (int)ceil(-a->dmb_fLow / bin_spacing);
		a->dmb_end0 = size - 1;
		a->dmb_begin1 = 0;
		a->dmb_end1 = (int)round(a->dmb_fHigh / bin_spacing);
	}
	a->dmb_decay = exp(-1.0 / (a->dmb_tau * (double)a->dmb_frame_rate));
	a->dmb_max_dB = -400.0;
}


// Call from console, for each 'disp' for which this function is desired.
// Call for initial setup and anytime one of the parameters changes.
// run:			Set to '1' if this is being used; cycles can be saved by setting to
//				'0' if this is not currently in use.
// disp:		Display identifier number.
// ss:			Set to '0' for Thetis use.
// LO:			Set to '0' for Thetis use.
// rate:		Sample_rate of display data, e.g., '192000.0'.
// fLow:		Lowest frequency of frequency range to evaluate, referenced to center_frequency
//				to which DDC is tuned.  For example, for LSB, not using CTUN, this might be
//				'-3000.0'.
// fHigh:		Highest frequency of frequency range to evaluate, referenced to center_frequency
//				to which DDC is tuned.  Example, LSB:  '-300.0'.
// tau:			Metering time constant in seconds.  E.g., '0.5'.
// frame_rate:	Display frame_rate currently in use.  E.g., '60'.
PORT
void SetupDetectMaxBin(int run, int disp, int ss, int LO, double rate, 
	double fLow, double fHigh, double tau, int frame_rate)
{
	// We only allow setup for one (ss,LO) pair at a time for each 'disp'.
	DP a = pdisp[disp];
	
	a->dmb_run = run;
	a->dmb_disp = disp;
	a->dmb_ss = ss;
	a->dmb_LO = LO;
	a->dmb_rate = rate;
	a->dmb_fLow = fLow;
	a->dmb_fHigh = fHigh;
	a->dmb_tau = tau;
	a->dmb_frame_rate = frame_rate;

	calc_dmb(a->dmb_disp, a->size);
}

// Call this function in 'Cspectra(...)', after the FFT.
void DetectMaxBin(int disp, int ss, int LO)
{
	DP a = pdisp[disp];
	int i;
	double mag, dmb_max;
	double dmb_max_dB;
	// If 'run' is set and the FFT Output is from the correct disp, ss, LO ...
	if (a->dmb_run && disp == a->dmb_disp && ss == a->dmb_ss && LO == a->dmb_LO)
	{
		fftw_complex* fft_out = a->fft_out[ss][LO];
		dmb_max = 1.0e-60;
		EnterCriticalSection(&a->cs_dmb);
		for (i = a->dmb_begin0; i <= a->dmb_end0; i++)
		{
			mag = fft_out[i][0] * fft_out[i][0] + fft_out[i][1] * fft_out[i][1];
			if (mag > dmb_max) dmb_max = mag;
		}
		for (i = a->dmb_begin1; i <= a->dmb_end1; i++)
		{
			mag = fft_out[i][0] * fft_out[i][0] + fft_out[i][1] * fft_out[i][1];
			if (mag > dmb_max) dmb_max = mag;
		}

		a->dmb_max_dB -= fabs((1.0 - a->dmb_decay) * a->dmb_max_dB);
		dmb_max_dB = 10.0 * mlog10(a->scale * dmb_max);
		if (dmb_max_dB > a->dmb_max_dB) a->dmb_max_dB = dmb_max_dB;
		LeaveCriticalSection(&a->cs_dmb);
		// for test only.
		// printf("Max Bin = %.5e\n", a->dmb_max_dB);
	}
}

// Call from console, for each 'disp' for which this function is desired.
// Always returns the value from the most recent display frame.
PORT
double GetDetectMaxBin(int disp)
{
	DP a = pdisp[disp];
	double dmb_max_dB;
	EnterCriticalSection(&a->cs_dmb);
	dmb_max_dB = a->dmb_max_dB;
	LeaveCriticalSection(&a->cs_dmb);
	return dmb_max_dB;
}

/********************************************************************************************************
*																										*
*							END CODE TO GET MAXIMUM FFT_BIN WITHIN A FREQ RANGE							*
*																										*
********************************************************************************************************/

DWORD WINAPI Cspectra (void *pargs)
{
	int i, j;
	int disp = ((int)(uintptr_t)pargs) >> 12;
	int ss = (((int)(uintptr_t)pargs) >> 4) & 255;
	int LO = ((int)(uintptr_t)pargs) & 15;
	DP a = pdisp[disp];
	int trans_size = a->size * sizeof(double);
	sm_frame fi;
	fi.smooth = 0;

	if (a->stop)
	{
		InterlockedDecrement(a->pnum_threads);
		return 0;
	}

	if ((ss >= a->begin_ss) && (ss <= a->end_ss))
	{
		if (a->smooth_on)
			sm_fill_window(a, ss, LO, &fi);
		else
		for (i = 0; i < a->size; i++)
		{
			(a->Cfft_in[ss][LO])[i][0] = a->window[i] * (double)((a->I_samples[ss][LO])[a->IQO_idx[ss][LO]]);
			(a->Cfft_in[ss][LO])[i][1] = a->window[i] * (double)((a->Q_samples[ss][LO])[a->IQO_idx[ss][LO]]);
			if(++a->IQO_idx[ss][LO] >= a->bsize)
				 a->IQO_idx[ss][LO] -= a->bsize;
		}

		if (a->stop)
		{
			InterlockedDecrement(a->pnum_threads);
			return 0;
		}
		fftw_execute (a->Cplan[ss][LO]);

		// Detect value of Max FFT Bin in a freq range
		DetectMaxBin(disp, ss, LO);
		// 

	}
	if (a->stop)
	{
		InterlockedDecrement(a->pnum_threads);
		return 0;
	}

	if (InterlockedBitTestAndReset(&(a->snap[ss][LO]), 0))
	{
		memcpy((char *)(a->snap_buff[ss][LO]), (char *)(a->fft_out[ss][LO]) + trans_size, trans_size);
		memcpy((char *)(a->snap_buff[ss][LO]) + trans_size, (char *)(a->fft_out[ss][LO]), trans_size);
		SetEvent(a->hSnapEvent[ss][LO]);
	}

	EnterCriticalSection(&(a->EliminateSection[ss]));
	if ((ss >= a->begin_ss) && (ss <= a->end_ss))
		Celiminate(disp, ss, LO);
	a->spec_flag[ss] |= 1 << LO;

	if (a->spec_flag[ss] == ((1 << a->num_fft) - 1))
	{
		a->spec_flag[ss] = 0;
		LeaveCriticalSection (&(a->EliminateSection[ss]));

		EnterCriticalSection (&a->StitchSection);
		a->stitch_flag |= ((uint64_t)1) << ss;

		if (a->stitch_flag == ((((uint64_t)1) << a->num_stitch) - 1))
		{
			a->stitch_flag = 0;
			LeaveCriticalSection(&a->StitchSection);
			for (j = 0; j < dMAX_STITCH; j++)
				for (i = 0; i < dMAX_NUM_FFT; i++)
					InterlockedBitTestAndReset(&(a->input_busy[j][i]), 0);
			if (fi.smooth)
				sm_stitch(disp, &fi);
			else
				stitch(disp);
		}
		else
			LeaveCriticalSection(&a->StitchSection);
	}
	else
		LeaveCriticalSection (&(a->EliminateSection[ss]));

	InterlockedDecrement(a->pnum_threads);
	return 1;
}

void interpolate(int disp, int set, double fmin, double fmax, int num_pixels)
{
	DP a = pdisp[disp];
	int i; 
	double f;
	int n = a->n_freqs[set];
	int k;
	int kmin = 0;
	int kmax = n - 1;
	int kdelta;
	double dx;
	double mag;

	for (i = 0; i < num_pixels; i++)
    {
		f = fmin + (double)i * (fmax - fmin) / (double)(num_pixels - 1);
		
		if (f < (a->freqs[set])[0])
            k = 0;
        else if (f > (a->freqs[set])[n - 1])
            k = n - 2;
        else
		{
			kdelta = 1;

			while (f < (a->freqs[set])[kmin])
			{
				kmin = max(0, kmin - kdelta);
				kdelta += kdelta;
			}
			while (f > (a->freqs[set])[kmax])
			{
				kmax = min(n - 1, kmax + kdelta);
				kdelta += kdelta;
			}
			k = (kmin + kmax) / 2;
			while ((kmax - kmin) > 1)
			{
				k = (kmin + kmax) / 2;
				if (f > (a->freqs[set])[k])
					kmin = k;
				else
					kmax = k--;
			}
		}

        dx = f - (a->freqs[set])[k];

        mag = (((a->ac3[set][0])[k] * dx + (a->ac2[set][0])[k]) * dx + (a->ac1[set][0])[k]) * dx + (a->ac0[set][0])[k];
		a->cd[i] = mag * mag;
	}
	return;
}

int build_interpolants(int disp, int set, int n, int m, double *x, double (*y)[dMAX_M])
{
	DP a = pdisp[disp];
	double dx[dMAX_N];
	double idx[dMAX_N];
	double dmain[dMAX_N];
	double dsub[dMAX_N];
	double dsup[dMAX_N];
	double d[dMAX_N][dMAX_M];
	double S[dMAX_N][dMAX_M];
	double b[dMAX_N];
	double v[dMAX_N][dMAX_M];
	double tmp;
	int i, j;
	if (n < 3) return -1;
    for (i = 0; i < n - 1; i++)
    {
        dx[i] = x[i + 1] - x[i];

        if (dx[i] < 1e-30)
            return -1;
        idx[i] = 1.0 / dx[i];
    }

    for (i = 1; i <= n - 2; i++)
    {
        if (i == 1)
        {
            dsub[i] = 0.0;
            dmain[i] = 3.0 * dx[i - 1] + 2.0 * dx[i];
            dsup[i] = dx[i];
        }
        else if (i == (n - 2))
        {
            dsub[i] = dx[i - 1];
            dmain[i] = 2.0 * dx[i - 1] + 3.0 * dx[i];
            dsup[i] = 0.0;
        }
        else
        {
            dsub[i] = dx[i - 1];
            dmain[i] = 2.0 * (dx[i - 1] + dx[i]);
            dsup[i] = dx[i];
        }
		for (j = 0; j < m; j++)
            d[i][j] = 6.0 * ((y[i + 1][j] - y[i][j]) * idx[i] - (y[i][j] - y[i - 1][j]) * idx[i - 1]);
    }

    b[1] = dmain[1];
    for (j = 0; j < m; j++)
        v[1][j] = d[1][j];

    for (i = 2; i <= n - 2; i++)
    {
        tmp = dsub[i] / b[i - 1];
        b[i] = dmain[i] - tmp * dsup[i - 1];
        for (j = 0; j < m; j++)
            v[i][j] = d[i][j] - tmp * v[i - 1][j];
    }

    for (j = 0; j < m; j++)
        S[n - 2][j] = v[n - 2][j] / b[n - 2];

    for (i = n - 3; i >= 1; i--)
        for (j = 0; j < m; j++)
            S[i][j] = (v[i][j] - dsup[i] * S[i + 1][j]) / b[i];

    for (j = 0; j < m; j++)
    {
        S[0][j] = S[1][j];
        S[n - 1][j] = S[n - 2][j];
    }

    for (i = 0; i < n - 1; i++)
        for (j = 0; j < m; j++)
        {
            (a->ac3[set][j])[i] = (S[i + 1][j] - S[i][j]) / (6.0 * dx[i]);
            (a->ac2[set][j])[i] = 0.5 * S[i][j];
            (a->ac1[set][j])[i] = (y[i + 1][j] - y[i][j]) * idx[i] - (2.0 * dx[i] * S[i][j] + dx[i] * S[i + 1][j]) / 6.0;
            (a->ac0[set][j])[i] = y[i][j];
        }
    return 0;
}

void __cdecl sendbuf(void *arg)
{
	DP a = pdisp[(int)(uintptr_t)arg];
	while(!a->end_dispatcher)
	{
		for (a->ss = 0; a->ss < a->num_stitch; a->ss++)
			for (a->LO = 0; a->LO < a->num_fft; a->LO++)
			{
				if (!_InterlockedAnd(&(a->input_busy[a->ss][a->LO]), 1) && _InterlockedAnd(&(a->buff_ready[a->ss][a->LO]), 1))
				{
					InterlockedBitTestAndSet(&(a->input_busy[a->ss][a->LO]), 0);

					EnterCriticalSection(&(a->BufferControlSection[a->ss][a->LO]));
					a->IQO_idx[a->ss][a->LO] = a->IQout_index[a->ss][a->LO];
					// absolute index of the first sample of this window (smooth pan / retune)
					a->win_start[a->ss][a->LO] = a->in_count[a->ss][a->LO] - a->have_samples[a->ss][a->LO];
					LeaveCriticalSection(&(a->BufferControlSection[a->ss][a->LO]));

					InterlockedIncrement(a->pnum_threads);
					if (a->type == 0)
						QueueUserWorkItem(spectra, (void *)(((uintptr_t)arg << 12) + (a->ss << 4) + a->LO), 0);
					else
						QueueUserWorkItem(Cspectra, (void *)(((uintptr_t)arg << 12) + (a->ss << 4) + a->LO), 0);

					if((a->IQout_index[a->ss][a->LO] += a->incr) >= a->bsize)
						a->IQout_index[a->ss][a->LO] -= a->bsize;

					EnterCriticalSection(&(a->BufferControlSection[a->ss][a->LO]));
					if ((a->have_samples[a->ss][a->LO] -= a->incr) < a->size)
						InterlockedBitTestAndReset(&(a->buff_ready[a->ss][a->LO]), 0);
					LeaveCriticalSection(&(a->BufferControlSection[a->ss][a->LO]));
				}
			}
		Sleep(1);
	}
	InterlockedBitTestAndReset(&a->dispatcher, 0);
	_endthread();
}

void CalcBandwidthNormalization (DP a)
{
	double bin_width;
	bin_width = (double)a->sample_rate / (double)a->size;
	a->norm_oneHz = 10.0 * mlog10 (1.0 / bin_width);
}

PORT
void ResetPixelBuffers(int disp)
{
	//[2.10.2]MW0LGE reset all the pixel and average buffers
	DP a = pdisp[disp];
	int i, j, k;

	EnterCriticalSection(&a->SetAnalyzerSection);
	EnterCriticalSection(&a->ResampleSection);
	EnterCriticalSection(&a->PixelRefSection);
	a->pixel_ref = 0.0;
	a->req_valid = 0;		// Jull1ano fork: forget the LO history and the averaging grids
	a->ev_count = 0;
	a->base_lo = 0.0;
	LeaveCriticalSection(&a->PixelRefSection);
	for (i = 0; i < dMAX_PIXOUTS; i++)
	{
		sm_invalidate(a, i);
		a->av_hzpp[i] = 0.0;
		a->av_n[i] = 0;
		a->av_floor[i] = 1.0e30;
		for (j = 0; j < dMAX_PIXELS; j++)
			a->t_pixels[i][j] = 0.0;
		for (j = 0; j < dMAX_AVERAGE; j++)
			for (k = 0; k < dMAX_PIXELS; k++)
				a->av_buff[i][j][k] = 0.0f;
		switch (a->av_mode[i])
		{
		case 1:
			for (j = 0; j < dMAX_PIXELS; j++)
				a->av_sum[i][j] = 1.0e-12;
			break;
		case 2:
			//done below
			//a->avail_frames[i] = 0;
			//a->av_in_idx[i] = 0;
			//a->av_out_idx[i] = 0;
			break;
		case 3:
			for (j = 0; j < dMAX_PIXELS; j++)
				a->av_sum[i][j] = -160.0;
			break;
		default:
			memset((void*)a->av_sum[i], 0, sizeof(double) * dMAX_PIXELS);
			break;
		}
		a->avail_frames[i] = 0;
		a->av_in_idx[i] = 0;
		a->av_out_idx[i] = 0;
		EnterCriticalSection(&a->PB_ControlsSection[i]);
		a->w_pix_buff[i] = 0;
		a->r_pix_buff[i] = 0;
		a->last_pix_buff[i] = 0;
		EnterCriticalSection(&a->PixelRefSection);
		for (j = 0; j < dNUM_PIXEL_BUFFS; j++)
		{
			a->pixel_refs[i][j] = 0.0;
			a->pb_ready[i][j] = 0;
		}
		LeaveCriticalSection(&a->PixelRefSection);
		LeaveCriticalSection(&a->PB_ControlsSection[i]);
	}
	memset((void*)a->pre_av_out, 0, sizeof(double) * a->max_size * a->max_stitch);
	LeaveCriticalSection(&a->ResampleSection);
	EnterCriticalSection(&a->StitchSection);
	for (i = 0; i < dMAX_STITCH; i++)
		for (j = 0; j < dMAX_NUM_FFT; j++)
			a->input_busy[i][j] = 0;
	for (i = 0; i < dMAX_STITCH; i++)
		a->spec_flag[i] = 0;
	a->stitch_flag = 0;
	a->ss = 0;
	a->LO = 0;	
	for (i = 0; i < dMAX_STITCH; i++)
		for (j = 0; j < dMAX_NUM_FFT; j++)
		{
			EnterCriticalSection(&(a->BufferControlSection[i][j]));
			a->buff_ready[i][j] = 0;
			a->have_samples[i][j] = 0;
			a->IQin_index[i][j] = 0;
			a->IQout_index[i][j] = 0;
			LeaveCriticalSection(&(a->BufferControlSection[i][j]));
		}	
	LeaveCriticalSection(&a->StitchSection);
	LeaveCriticalSection(&a->SetAnalyzerSection);
}

PORT    
void SetAnalyzer (	int disp,			// display identifier
					int n_pixout,		// pixel output identifier
					int n_fft,			// number of LO frequencies = number of ffts used in elimination
					int typ,			// 0 for real input data (I only); 1 for complex input data (I & Q)
					int *flp,			// vector with one elt for each LO frequency, 1 if high-side LO, 0 otherwise 
					int sz,				// size of the fft, i.e., number of input samples
					int bf_sz,			// number of samples transferred for each OpenBuffer()/CloseBuffer()
					int win_type,		// integer specifying which window function to use
					double pi,			// PiAlpha parameter for Kaiser window
					int ovrlp,			// number of samples each fft (other than the first) is to re-use from the previous 
					int clp,			// number of fft output bins to be clipped from EACH side of each sub-span
					double fscLin,		// number of bins to clip from low end of entire span
					double fscHin,		// number of bins to clip from high end of entire span
					int n_pix,			// number of pixel values to return.  may be either <= or > number of bins 
					int n_stch,			// number of sub-spans to concatenate to form a complete span 
					int calset,			// identifier of which set of calibration data to use 
					double fmin,		// frequency at first pixel value 
					double fmax,		// frequency at last pixel value
					int max_w
				 )
{
	DP a = pdisp[disp];
	int i, j, same_flip = 1;

	for (i = 0; i < n_fft && i < dMAX_NUM_FFT; i++)
		if (a->flip[i] != *(flp + i)) same_flip = 0;

	// Jull1ano fork: only the visible part of the span changed (pan / zoom). Keep the sample history and
	// the fft cadence, move the averaging state onto the new view and publish it at once.
	if (a->smooth_on && a->smooth_enable && same_flip &&
		sz == a->size && n_fft == a->num_fft && typ == a->type && bf_sz == a->buff_size &&
		win_type == a->window_type && pi == a->PiAlpha && ovrlp == a->overlap && clp == a->clip &&
		n_pix == a->num_pixels && n_stch == a->num_stitch && max_w == a->max_writeahead &&
		n_pixout == a->num_pixout && calset == a->cal_set && !a->cal_changed &&
		fscLin >= 0.0 && fscHin >= 0.0 &&
		fscLin + fscHin < (double)(a->num_stitch * (a->out_size - 1 - 2 * a->clip)) - 2.0)
	{
		EnterCriticalSection(&a->SetAnalyzerSection);
		EnterCriticalSection(&a->ResampleSection);
		if (((fmin != a->f_min) || (fmax != a->f_max)) && ((fmin == 0.0) && (fmax == 0.0)))
			for (i = 0; i < dMAX_PIXELS; i++)
				a->cd[i] = 1.0;
		if (((fmax != 0.0) || (fmin != 0.0)) && ((fmin != a->f_min) || (fmax != a->f_max)))
			interpolate(disp, calset, fmin, fmax, n_pix);
		a->f_min = fmin;
		a->f_max = fmax;
		a->fsclipL = fscLin;
		a->fsclipH = fscHin;
		a->fscL = (int)a->fsclipL;
		a->fscH = (int)a->fsclipH;
		a->pix_per_bin = (double)a->num_pixels / ((double)(a->num_stitch * (a->out_size - 1 - 2 * a->clip)) - a->fsclipL - a->fsclipH - 1.0);
		a->det_offset = -a->pix_per_bin * (a->fsclipL - floor(a->fsclipL));
		a->bin_per_pix = ((double)(a->num_stitch * (a->out_size - 1 - 2 * a->clip)) - 1.0 - a->fsclipL - a->fsclipH) / ((double)a->num_pixels - 1.0);
		sm_follow_view(a, 1);
		LeaveCriticalSection(&a->ResampleSection);
		LeaveCriticalSection(&a->SetAnalyzerSection);
		return;
	}

	EnterCriticalSection(&a->SetAnalyzerSection);
	a->end_dispatcher = 1;
	while (InterlockedAnd(&a->dispatcher, 1))
		Sleep(1);
	a->stop = 1;
	while (_InterlockedAnd(a->pnum_threads, 1023))
		Sleep(1);
	a->num_pixout = n_pixout;
	a->num_fft = n_fft;
	a->type = typ;
	a->buff_size = bf_sz;
	for (i = 0; i < a->num_fft; i++)
		a->flip[i] = *(flp + i);
	a->overlap = ovrlp;
	a->clip = clp;
	a->fsclipL = fscLin;
	a->fsclipH = fscHin;
	a->num_stitch = n_stch;

	if (sz != a->size)
	{
		for (i = 0; i < a->max_stitch; i++)
			for (j = 0; j < a->max_num_fft; j++)
			{
				if (a->plan[i][j])		fftw_destroy_plan (a->plan[i][j]);
				if (a->Cplan[i][j])		fftw_destroy_plan (a->Cplan[i][j]);
				a->plan[i][j] = fftw_plan_dft_r2c_1d(sz, a->fft_in[i][j], a->fft_out[i][j], FFTW_PATIENT);
				a->Cplan[i][j] = fftw_plan_dft_1d(sz, a->Cfft_in[i][j], a->fft_out[i][j], FFTW_FORWARD, FFTW_PATIENT);
			}

		// Setup DetectMaxBin for a 'size' change.
		calc_dmb(disp, sz);
		//

	}

	if ((sz != a->size) || (win_type != a->window_type) || (pi != a->PiAlpha))
		new_window(disp, win_type, sz, pi);

	a->size = sz;
	a->window_type = win_type;
	a->PiAlpha = pi;
	a->max_writeahead = max_w;
	CalcBandwidthNormalization (a);
	if (((fmin != a->f_min) || (fmax != a->f_max)) && ((fmin == 0.0) && (fmax == 0.0)))
		for (i = 0; i < dMAX_PIXELS; i++)
			a->cd[i] = 1.0;

	if (((fmax != 0.0) || (fmin != 0.0)) && ((n_pix != a->num_pixels) || (fmin != a->f_min) || (fmax != a->f_max) || (calset != a->cal_set) || a->cal_changed))
		interpolate(disp, calset, fmin, fmax, n_pix);

	a->incr = a->size - a->overlap;
	a->num_pixels = n_pix;
	a->f_min = fmin;
	a->f_max = fmax;
	a->cal_set = calset;
	a->cal_changed = 0;

	if (a->type == 0)
	{
		a->out_size = a->size / 2 + 1;
		a->scale = 4.0 / ((double)a->size * (double)a->size);
	}
	else
	{
		a->out_size = a->size;
		a->scale = 1.0 / ((double)a->size * (double)a->size);
	}

	a->begin_ss = 0;
	a->end_ss = a->num_stitch - 1;
	a->fscL = (int)a->fsclipL;
	a->fscH = (int)a->fsclipH;
	while (a->fscL >= (a->out_size - 1 - 2 * a->clip))
	{
		a->fscL -= a->out_size - 1 - 2 * a->clip;
		a->ss_bins[a->begin_ss] = 0;
		a->begin_ss++;
	}
	while (a->fscH >= (a->out_size - 1 - 2 * a->clip))
	{
		a->fscH -= a->out_size - 1 - 2 * a->clip;
		a->ss_bins[a->end_ss] = 0;
		a->end_ss--;
	}

	a->pix_per_bin = (double)a->num_pixels / ((double)(a->num_stitch * (a->out_size - 1 - 2 * a->clip)) - a->fsclipL - a->fsclipH - 1.0);
	a->det_offset = -a->pix_per_bin * (a->fsclipL - floor(a->fsclipL));
	a->bin_per_pix = ((double)(a->num_stitch * (a->out_size - 1 - 2 * a->clip)) - 1.0 - a->fsclipL - a->fsclipH) / ((double)a->num_pixels - 1.0);

	for (i = 0; i < dMAX_STITCH; i++)
		for (j = 0; j < dMAX_NUM_FFT; j++)
			a->input_busy[i][j] = 0;

	for (i = 0; i < dMAX_STITCH; i++)
		a->spec_flag[i] = 0;
	a->stitch_flag = 0;
	EnterCriticalSection(&a->PixelRefSection);
	for (i = 0; i < dMAX_PIXOUTS; i++)
	{		
		a->w_pix_buff[i] = 0;
		a->r_pix_buff[i] = 0;
		a->last_pix_buff[i] = 0;
		for (j = 0; j < dNUM_PIXEL_BUFFS; j++)
		{
			a->pixel_refs[i][j] = 0.0;
			a->pb_ready[i][j] = 0;
		}
	}
	LeaveCriticalSection(&a->PixelRefSection);
	a->ss = 0;
	a->LO = 0;
	for (i = 0; i < dMAX_STITCH; i++)
		for (j = 0; j < dMAX_NUM_FFT; j++)
		{
			a->buff_ready[i][j] = 0;
			a->have_samples[i][j] = 0;
			a->IQin_index[i][j] = 0;
			a->IQout_index[i][j] = 0;
		}

	// Jull1ano fork: smooth pan / retune needs complex data and one sub-span
	EnterCriticalSection(&a->ResampleSection);
	a->smooth_on = a->smooth_enable && (a->type == 1) && (a->num_stitch == 1) && (a->num_fft == 1);
	if (a->smooth_on)
		sm_follow_view(a, 1);	// keep showing the trace (on the new grid) while the fft history refills
	LeaveCriticalSection(&a->ResampleSection);

	a->stop = 0;
	a->end_dispatcher = 0;
	LeaveCriticalSection(&a->SetAnalyzerSection);
}

PORT
void XCreateAnalyzer(	int disp,
						int *success,
						int m_size,
						int m_num_fft,
						int m_stitch,
						char *app_data_path
						)
{

	int i, j;
	DP a = (DP) malloc0 (sizeof(dp));
	pdisp[disp] = a;

	a->max_size = m_size;
	a->max_num_fft = m_num_fft;
	a->max_stitch = m_stitch;
	
	a->pnum_threads = (LONG*) malloc0 (sizeof (LONG));

	for (i = 0; i < a->max_stitch; i++)
		for (j = 0; j < a->max_num_fft; j++)
		{
			a->hSnapEvent[i][j] = CreateEvent(NULL, FALSE, FALSE, TEXT("snap"));
			a->snap[i][j] = 0;
		}
	InitializeCriticalSectionAndSpinCount(&a->PixelRefSection, 0);
	InitializeCriticalSectionAndSpinCount(&a->ResampleSection, 0);
	InitializeCriticalSectionAndSpinCount(&a->SetAnalyzerSection, 0);
	InitializeCriticalSectionAndSpinCount(&a->StitchSection, 0);
	for (i = 0; i < dMAX_PIXOUTS; i++)
		InitializeCriticalSectionAndSpinCount(&a->PB_ControlsSection[i], 0);
	for (i = 0; i < dMAX_STITCH; i++)
	{
		InitializeCriticalSectionAndSpinCount(&(a->EliminateSection[i]), 0);
		for (j = 0; j < dMAX_NUM_FFT; j++)
			InitializeCriticalSectionAndSpinCount(&(a->BufferControlSection[i][j]), 0);
	}

	a->window = (double*) malloc0 (sizeof(double) * a->max_size);

	for (i = 0; i < a->max_stitch; i++)
	{
		a->result[i] = (double*) malloc0 (sizeof(double) * a->max_size);

	}
	for (i = 0; i < a->max_stitch; i++)
		for (j = 0; j < a->max_num_fft; j++)
		{
			a->plan[i][j] = 0;
			a->Cplan[i][j] = 0;
			a->fft_in[i][j]   = (double*) malloc0 (sizeof(double) * a->max_size);
			a->Cfft_in[i][j]  = (fftw_complex*) fftw_malloc(sizeof(fftw_complex) * a->max_size);
			a->fft_out[i][j]  = (fftw_complex*) fftw_malloc(sizeof(fftw_complex) * a->max_size);
		}
	a->pre_av_out = (double*) malloc0 (sizeof(double) * a->max_size * a->max_stitch);
	// Jull1ano fork: smooth pan / retune
	a->smooth_enable = 1;
	a->smooth_on = 0;
	a->lat_ms = 2.5;
	a->publish_mask = 1;	// pixel output 0 = panadapter; the waterfall (1) only gets real frames
	a->ev_count = 0;
	a->base_lo = 0.0;
	a->req_valid = 0;
	a->sm_tmp = (double*) malloc0 (sizeof(double) * dMAX_PIXELS);
	a->sm_tmp2 = (double*) malloc0 (sizeof(double) * dMAX_PIXELS);
	a->sm_vtmp = (unsigned char*) malloc0 (dMAX_PIXELS);
	for (i = 0; i < dMAX_PIXOUTS; i++)
	{
		a->av_valid[i] = (unsigned char*) malloc0 (dMAX_PIXELS);
		a->av_n[i] = 0;
		a->av_hzpp[i] = 0.0;
		a->av_low_hz[i] = 0.0;
		a->av_floor[i] = 1.0e30;
	}
	for (i = 0; i < a->max_stitch; i++)
		for (j = 0; j < a->max_num_fft; j++)
		{
			a->in_count[i][j] = 0;
			a->win_start[i][j] = 0;
		}
	for (i = 0; i < dMAX_PIXOUTS; i++)
	{
		a->det_type[i] = 0;
		a->av_mode[i] = 0;
		a->av_sum[i] = (double*) malloc0 (sizeof(double) * dMAX_PIXELS);
		for (j = 0; j < dMAX_AVERAGE; j++)
			a->av_buff[i][j] = (double*) malloc0 (sizeof(double) * dMAX_PIXELS);
		a->t_pixels[i] = (double*) malloc0 (sizeof(double) * dMAX_PIXELS);
		for (j = 0; j < dNUM_PIXEL_BUFFS; j++)
			a->pixels[i][j] = (dOUTREAL*) malloc0 (sizeof(dOUTREAL) * dMAX_PIXELS);
	}
	
	a->cd = (double*) malloc0 (sizeof(double) * dMAX_PIXELS);
	for (j = 0; j < dMAX_PIXELS; j++)
		a->cd[j] = 1.0;
	for (i = 0; i < dMAX_CAL_SETS; i++)
	{
		a->freqs[i] = (double*) malloc0 (sizeof(double) * dMAX_N);
		for (j = 0; j < dMAX_M; j++)
		{
			a->ac3[i][j] = (double*) malloc0 (sizeof(double) * dMAX_N);
			a->ac2[i][j] = (double*) malloc0 (sizeof(double) * dMAX_N);
			a->ac1[i][j] = (double*) malloc0 (sizeof(double) * dMAX_N);
			a->ac0[i][j] = (double*) malloc0 (sizeof(double) * dMAX_N);
		}
	}
	
	a->size = -1;
	a->window_type = -1;
	a->num_pixels = -1;
	a->cal_set = -1;
	a->f_min = -1.0;
	a->f_max = -1.0;
	EnterCriticalSection(&a->PixelRefSection);
	a->pixel_ref = 0.0;
	LeaveCriticalSection(&a->PixelRefSection);
	a->bsize = a->max_size * dSAMP_BUFF_MULT;
	for (i = 0; i < a->max_stitch; i++)
		for (j = 0; j < a->max_num_fft; j++)
		{
			a->I_samples[i][j] = (dINREAL*) malloc0 (sizeof(dINREAL) * a->bsize);
			a->Q_samples[i][j] = (dINREAL*) malloc0 (sizeof(dINREAL) * a->bsize);
		}

	// Initialize DetectMaxBin functionality
	Init_DetectMaxBin(disp);
	// for test only.
	// SetupDetectMaxBin(1, 0, 0, 0, 192000.0, -3000.0, -300.0, 0.5, 60);
	//

	*success = 0;
}

PORT   
void DestroyAnalyzer(int disp)
{
	DP a = pdisp[disp];
	int i, j;

	a->end_dispatcher = 1;
	while (InterlockedAnd(&a->dispatcher, 1))
		Sleep(1);

	for (i = 0; i < a->max_stitch; i++)
		for (j = 0; j < a->max_num_fft; j++)
		{
			_aligned_free  (a->I_samples[i][j]);
			_aligned_free  (a->Q_samples[i][j]);
		}
	
	for (i = 0; i < dMAX_CAL_SETS; i++)
	{
		_aligned_free (a->freqs[i]);
		for (j = 0; j < dMAX_M; j++)
		{
			_aligned_free  (a->ac3[i][j]);
			_aligned_free  (a->ac2[i][j]);
			_aligned_free  (a->ac1[i][j]);
			_aligned_free  (a->ac0[i][j]);
		}
	}
	_aligned_free (a->cd);
	
	for (i = 0; i < dMAX_PIXOUTS; i++)
	{
		for (j = 0; j < dNUM_PIXEL_BUFFS; j++)
			_aligned_free (a->pixels[i][j]);
		_aligned_free  (a->t_pixels[i]);
		for (j = 0; j < dMAX_AVERAGE; j++)
			_aligned_free (a->av_buff[i][j]);
		_aligned_free (a->av_sum[i]);
		_aligned_free (a->av_valid[i]);
	}
	_aligned_free (a->sm_tmp);
	_aligned_free (a->sm_tmp2);
	_aligned_free (a->sm_vtmp);

	_aligned_free (a->pre_av_out);
	for (i = 0; i < a->max_stitch; i++)
		for (j = 0; j < a->max_num_fft; j++)
		{
			fftw_destroy_plan (a->plan[i][j]);
			fftw_destroy_plan (a->Cplan[i][j]);
			fftw_free (a->Cfft_in[i][j]);
			_aligned_free (a->fft_in[i][j]);
			fftw_free (a->fft_out[i][j]);
		}
	
	for (i = 0; i < a->max_stitch; i++)
		_aligned_free (a->result[i]);
	_aligned_free (a->window);

	for (i = 0; i < dMAX_STITCH; i++)
	{
		DeleteCriticalSection(&(a->EliminateSection[i]));
		for (j = 0; j < dMAX_NUM_FFT; j++)
			DeleteCriticalSection(&(a->BufferControlSection[i][j]));
	}
	for (i = 0; i < dMAX_PIXOUTS; i++)
		DeleteCriticalSection(&a->PB_ControlsSection[i]);
	DeleteCriticalSection(&a->PixelRefSection);
	DeleteCriticalSection(&a->StitchSection);
	DeleteCriticalSection(&a->SetAnalyzerSection);
	DeleteCriticalSection(&a->ResampleSection);

	for (i = 0; i < a->max_stitch; i++)
		for (j = 0; j < a->max_num_fft; j++)
			CloseHandle(a->hSnapEvent[i][j]);

	_aligned_free ((void *) a->pnum_threads);

	// Destroy DetectMaxBin functionality.
	Destroy_DetectMaxBin(disp);
	//

	_aligned_free (a);
}

PORT   
void SetPixelRef(int disp, double pixel_ref)
{
	// [2.10.3.13]MW0LGE
	// returned via GetPixels. This can be used by a client to index the results of GetPixels
	// For example be called with the DDC centre frequency, whenever it changes, so that a client
	// when calling GetPixels can index these pixels to a deterministic frequency
	// Jull1ano fork: the call is also the LO change marker for the smooth pan / retune code. It must come
	// right when the new LO is sent to the radio (Thetis: Display.CentreFreqRX1 in UpdateRX1DDSFreq).
	DP a = pdisp[disp];
	int changed;
	if (a == 0)
		return;

	EnterCriticalSection(&a->ResampleSection);
	EnterCriticalSection(&a->PixelRefSection);
	changed = (pixel_ref != a->pixel_ref);
	a->pixel_ref = pixel_ref;
	if (changed)
	{
		if (!a->req_valid || !a->smooth_on)
		{
			// first LO, or no smooth mode: nothing to rotate
			a->ev_count = 0;
			a->base_lo = pixel_ref;
		}
		else
		{
			if (a->ev_count == dMAX_RETUNE_EV)
			{
				a->base_lo = a->ev_lo[0];
				a->ev_count--;
				memmove(a->ev_idx, a->ev_idx + 1, a->ev_count * sizeof(long long));
				memmove(a->ev_lo, a->ev_lo + 1, a->ev_count * sizeof(double));
			}
			a->ev_idx[a->ev_count] = a->in_count[0][0] + llround(a->lat_ms * 1.0e-3 * (double)a->sample_rate);
			if (a->ev_count > 0 && a->ev_idx[a->ev_count] < a->ev_idx[a->ev_count - 1])
				a->ev_idx[a->ev_count] = a->ev_idx[a->ev_count - 1];
			a->ev_lo[a->ev_count] = pixel_ref;
			a->ev_count++;
		}
		a->req_valid = 1;
	}
	LeaveCriticalSection(&a->PixelRefSection);
	if (changed && a->smooth_on)
		sm_follow_view(a, 1);
	LeaveCriticalSection(&a->ResampleSection);
}

PORT
void SetAnalyzerSmooth(int disp, int enable)
{
	DP a = pdisp[disp];
	if (a == 0)
		return;
	a->smooth_enable = enable ? 1 : 0;	// takes effect on the next SetAnalyzer()
}

PORT
void SetAnalyzerRetuneLatency(int disp, double latency_ms)
{
	DP a = pdisp[disp];
	if (a == 0)
		return;
	EnterCriticalSection(&a->PixelRefSection);
	a->lat_ms = latency_ms;
	LeaveCriticalSection(&a->PixelRefSection);
}

PORT
long long GetAnalyzerInputCount(int disp)
{
	DP a = pdisp[disp];
	if (a == 0)
		return 0;
	return a->in_count[0][0];
}

PORT
void GetPixels	(	int disp,
					int pixout,
					dOUTREAL *pix,		//if new pixel values avail, copies to pix and sets flag = 1
					int *flag,			//else, returns 0 (try again later)
					double *center_mhz
				)
{
	DP a = pdisp[disp];
	if (a == 0)
	{
		*flag = 0;
		if (center_mhz != 0)
			*center_mhz = 0.0;
		return;
	}

	EnterCriticalSection(&a->SetAnalyzerSection);
	EnterCriticalSection(&a->PB_ControlsSection[pixout]);
		a->r_pix_buff[pixout] = a->last_pix_buff[pixout];
	LeaveCriticalSection(&a->PB_ControlsSection[pixout]);

	if (_InterlockedAnd(&(a->pb_ready[pixout][a->r_pix_buff[pixout]]), 1))
	{
		memcpy (pix, a->pixels[pixout][a->r_pix_buff[pixout]], a->num_pixels * sizeof(dOUTREAL));
		*flag = 1;
		if (center_mhz != 0) 
		{
			EnterCriticalSection(&a->PixelRefSection);
			*center_mhz = a->pixel_refs[pixout][a->r_pix_buff[pixout]];
			LeaveCriticalSection(&a->PixelRefSection);
		}
		InterlockedBitTestAndReset(&(a->pb_ready[pixout][a->r_pix_buff[pixout]]), 0);
	}
	else
	{
		*flag = 0;
		if (center_mhz != 0)
			*center_mhz = 0.0;
	}
	LeaveCriticalSection(&a->SetAnalyzerSection);
}

PORT
void SnapSpectrum(	int disp,
					int ss,
					int LO,
					double *snap_buff)
{
	DP a = pdisp[disp];
	a->snap_buff[ss][LO] = snap_buff;
	InterlockedBitTestAndSet(&(a->snap[ss][LO]), 0);
	WaitForSingleObject(a->hSnapEvent[ss][LO], INFINITE);
}

PORT
void SnapSpectrumTimeout(int disp,
	int ss,
	int LO,
	double* snap_buff,
	DWORD timeout,
	int* flag)
{
	DP a = pdisp[disp];
	a->snap_buff[ss][LO] = snap_buff;
	ResetEvent(a->hSnapEvent[ss][LO]);
	InterlockedBitTestAndSet(&(a->snap[ss][LO]), 0);
	if (!WaitForSingleObject(a->hSnapEvent[ss][LO], timeout))
		*flag = 1;
	else
	{
		InterlockedBitTestAndReset(&(a->snap[ss][LO]), 0);
		*flag = 0;
	}
}

int calcompare (const void * a, const void * b)
{
	if (*(double*)a < *(double*)b)
		return -1;
	else if (*(double*)a == *(double*)b)
		return 0;
	else
		return 1;
}

PORT   
void SetCalibration (	int disp,
						int set_num,				//identifier for this calibration data set
						int n_points,				//number of calibration points in the set
						double (*cal)[dMAX_M+1]		//pointer to the calibration table, first
					)								//   column is frequency, add'l columns are
													//	 data for variables being calibrated
{
	DP a = pdisp[disp];
	int i, j;
	int k = 0;
	double y [dMAX_N][dMAX_M];

	qsort (cal, n_points, (dMAX_M+1) * sizeof(double), calcompare);

	for (i = 0; i < n_points; i++)
	{
		if ((i == n_points - 1) || (cal[i][0] != cal[i + 1][0]))
		{
			(a->freqs[set_num])[k] = cal[i][0];
			for (j = 0; j < dMAX_M; j++)
				y[k][j] = cal[i][j + 1];
			k++;
		}
	}

	a->n_freqs[set_num] = k;

	build_interpolants(disp, set_num, a->n_freqs[set_num], dMAX_M, a->freqs[set_num], y);
	a->cal_changed = 1;
}

PORT   
void OpenBuffer(int disp, int ss, int LO, void **Ipointer, void **Qpointer)
{
	DP a = pdisp[disp];
	EnterCriticalSection(&a->SetAnalyzerSection);
	*Ipointer = &((a->I_samples[ss][LO])[a->IQin_index[ss][LO]]);
	*Qpointer = &((a->Q_samples[ss][LO])[a->IQin_index[ss][LO]]);
	LeaveCriticalSection(&a->SetAnalyzerSection);
}

PORT   
void CloseBuffer(int disp, int ss, int LO)
{
	DP a = pdisp[disp];
	EnterCriticalSection(&a->SetAnalyzerSection);
	EnterCriticalSection(&(a->BufferControlSection[ss][LO]));
		if (a->have_samples[ss][LO] > a->max_writeahead)
			{
				//if we're receiving samples too much faster than we're consuming them, skip some
				if ((a->IQout_index[ss][LO] += a->have_samples[ss][LO] - a->max_writeahead) >= a->bsize)
						a->IQout_index[ss][LO] -= a->bsize;
				a->have_samples[ss][LO] = a->max_writeahead;
			}
		a->in_count[ss][LO] += a->buff_size;
		if ((a->have_samples[ss][LO] += a->buff_size) >= a->size)
			InterlockedBitTestAndSet(&(a->buff_ready[ss][LO]), 0);
	LeaveCriticalSection(&(a->BufferControlSection[ss][LO]));
	if((a->IQin_index[ss][LO] += a->buff_size) >= a->bsize)	//REQUIRES buff_size IS A SUB-MULTIPLE OF SIZE OF INPUT SAMPLE BUFFS!
		a->IQin_index[ss][LO] = 0;

	if (!InterlockedAnd(&a->dispatcher, 1))
	{
		InterlockedBitTestAndSet (&a->dispatcher, 0);
		LeaveCriticalSection(&a->SetAnalyzerSection);
		_beginthread(sendbuf, 0, (void *)(uintptr_t)disp);
	}
	else
		LeaveCriticalSection(&a->SetAnalyzerSection);
}

PORT
void Spectrum(int disp, int ss, int LO, dINREAL* pI, dINREAL* pQ)
{
	dINREAL *Ipointer;
	dINREAL *Qpointer;
	DP a = pdisp[disp];
	EnterCriticalSection(&a->SetAnalyzerSection);
	Ipointer = &((a->I_samples[ss][LO])[a->IQin_index[ss][LO]]);
	Qpointer = &((a->Q_samples[ss][LO])[a->IQin_index[ss][LO]]);
	LeaveCriticalSection(&a->SetAnalyzerSection);

	memcpy(Ipointer, pI, a->buff_size * sizeof(dINREAL));
	memcpy(Qpointer, pQ, a->buff_size * sizeof(dINREAL));

	EnterCriticalSection(&a->SetAnalyzerSection);
	EnterCriticalSection(&(a->BufferControlSection[ss][LO]));
		if (a->have_samples[ss][LO] > a->max_writeahead)
			{
				//if we're receiving samples too much faster than we're consuming them, skip some
				if ((a->IQout_index[ss][LO] += a->have_samples[ss][LO] - a->max_writeahead) >= a->bsize)
						a->IQout_index[ss][LO] -= a->bsize;
				a->have_samples[ss][LO] = a->max_writeahead;
			}
		a->in_count[ss][LO] += a->buff_size;
		if ((a->have_samples[ss][LO] += a->buff_size) >= a->size)
			InterlockedBitTestAndSet(&(a->buff_ready[ss][LO]), 0);
	LeaveCriticalSection(&(a->BufferControlSection[ss][LO]));
	if((a->IQin_index[ss][LO] += a->buff_size) >= a->bsize)	//REQUIRES buff_size IS A SUB-MULTIPLE OF SIZE OF INPUT SAMPLE BUFFS!
		a->IQin_index[ss][LO] = 0;

	if (!InterlockedAnd(&a->dispatcher, 1))
	{
		InterlockedBitTestAndSet(&a->dispatcher, 0);
		LeaveCriticalSection(&a->SetAnalyzerSection);
		_beginthread(sendbuf, 0, (void *)(uintptr_t)disp);
	}
	else
		LeaveCriticalSection(&a->SetAnalyzerSection);
}

PORT
void Spectrum2(int run, int disp, int ss, int LO, dINREAL* pbuff)
{
	if (run)
	{
		int i;
		dINREAL *Ipointer;
		dINREAL *Qpointer;
		DP a = pdisp[disp];
		EnterCriticalSection(&a->SetAnalyzerSection);
		Ipointer = &((a->I_samples[ss][LO])[a->IQin_index[ss][LO]]);
		Qpointer = &((a->Q_samples[ss][LO])[a->IQin_index[ss][LO]]);
		LeaveCriticalSection(&a->SetAnalyzerSection);

		for (i = 0; i < a->buff_size; i++)
		{
			Ipointer[i] = pbuff[2 * i + 1];
			Qpointer[i] = pbuff[2 * i + 0];
		}

		EnterCriticalSection(&a->SetAnalyzerSection);
		EnterCriticalSection(&(a->BufferControlSection[ss][LO]));
			if (a->have_samples[ss][LO] > a->max_writeahead)
				{
					//if we're receiving samples too much faster than we're consuming them, skip some
					if ((a->IQout_index[ss][LO] += a->have_samples[ss][LO] - a->max_writeahead) >= a->bsize)
							a->IQout_index[ss][LO] -= a->bsize;
					a->have_samples[ss][LO] = a->max_writeahead;
				}
			a->in_count[ss][LO] += a->buff_size;
			if ((a->have_samples[ss][LO] += a->buff_size) >= a->size)
				InterlockedBitTestAndSet(&(a->buff_ready[ss][LO]), 0);
		LeaveCriticalSection(&(a->BufferControlSection[ss][LO]));
		if((a->IQin_index[ss][LO] += a->buff_size) >= a->bsize)	//REQUIRES buff_size IS A SUB-MULTIPLE OF SIZE OF INPUT SAMPLE BUFFS!
			a->IQin_index[ss][LO] = 0;

		if (!InterlockedAnd(&a->dispatcher, 1))
		{
			InterlockedBitTestAndSet(&a->dispatcher, 0);
			LeaveCriticalSection(&a->SetAnalyzerSection);
			_beginthread(sendbuf, 0, (void *)(uintptr_t)disp);
		}
		else
			LeaveCriticalSection(&a->SetAnalyzerSection);
	}
}

PORT
void Spectrum0(int run, int disp, int ss, int LO, double* pbuff)
{
	if (run)
	{
		int i;
		dINREAL *Ipointer;
		dINREAL *Qpointer;
		DP a = pdisp[disp];
		EnterCriticalSection(&a->SetAnalyzerSection);
		Ipointer = &((a->I_samples[ss][LO])[a->IQin_index[ss][LO]]);
		Qpointer = &((a->Q_samples[ss][LO])[a->IQin_index[ss][LO]]);
		LeaveCriticalSection(&a->SetAnalyzerSection);

		for (i = 0; i < a->buff_size; i++)
		{
			Ipointer[i] = (dINREAL)pbuff[2 * i + 1];
			Qpointer[i] = (dINREAL)pbuff[2 * i + 0];
		}

		EnterCriticalSection(&a->SetAnalyzerSection);
		EnterCriticalSection(&(a->BufferControlSection[ss][LO]));
			if (a->have_samples[ss][LO] > a->max_writeahead)
				{
					//if we're receiving samples too much faster than we're consuming them, skip some
					if ((a->IQout_index[ss][LO] += a->have_samples[ss][LO] - a->max_writeahead) >= a->bsize)
					 	a->IQout_index[ss][LO] -= a->bsize;
					a->have_samples[ss][LO] = a->max_writeahead;
				}
			a->in_count[ss][LO] += a->buff_size;
			if ((a->have_samples[ss][LO] += a->buff_size) >= a->size)
				InterlockedBitTestAndSet(&(a->buff_ready[ss][LO]), 0);
		LeaveCriticalSection(&(a->BufferControlSection[ss][LO]));
		if((a->IQin_index[ss][LO] += a->buff_size) >= a->bsize)	//REQUIRES buff_size IS A SUB-MULTIPLE OF SIZE OF INPUT SAMPLE BUFFS!
			a->IQin_index[ss][LO] = 0;

		if (!InterlockedAnd(&a->dispatcher, 1))
		{
			InterlockedBitTestAndSet(&a->dispatcher, 0);
			LeaveCriticalSection(&a->SetAnalyzerSection);
			_beginthread(sendbuf, 0, (void *)(uintptr_t)disp);
		}
		else
			LeaveCriticalSection(&a->SetAnalyzerSection);
	}
}

PORT
void SetDisplayDetectorMode (int disp, int pixout, int mode)
{
	DP a = pdisp[disp];
	if (a->det_type[pixout] != mode)
	{
		EnterCriticalSection (&a->ResampleSection);
		a->det_type[pixout] = mode;
		LeaveCriticalSection (&a->ResampleSection);
	}
}

PORT
void SetDisplayAverageMode (int disp, int pixout, int mode)
{
	int i;
	DP a = pdisp[disp];
	if (a->av_mode[pixout] != mode)
	{
		EnterCriticalSection (&a->ResampleSection);
		a->av_mode[pixout] = mode;
		switch (mode)
		{
		case 1:
			for (i = 0; i < dMAX_PIXELS; i++)
				a->av_sum[pixout][i] = 1.0e-12;
			break;
		case 2:
			a->avail_frames[pixout] = 0;
			a->av_in_idx[pixout] = 0;
			a->av_out_idx[pixout] = 0;
			break;
		case 3:
			for (i = 0; i < dMAX_PIXELS; i++)
				a->av_sum[pixout][i] = -160.0;
			break;
		default:
			memset ((void *)a->av_sum[pixout], 0, sizeof(double) * dMAX_PIXELS);
			break;
		}
		sm_invalidate(a, pixout);	// Jull1ano fork: the state was reset, re-seed from the next frame
		LeaveCriticalSection (&a->ResampleSection);
	}
}

PORT
void SetDisplayNumAverage (int disp, int pixout, int num)
{
	DP a = pdisp[disp];
	if (a->num_average[pixout] != num)
	{
		EnterCriticalSection (&a->ResampleSection);
		a->num_average[pixout] = num;
		a->avail_frames[pixout] = 0;
		a->av_in_idx[pixout] = 0;
		a->av_out_idx[pixout] = 0;
		if (a->av_mode[pixout] == 2)
		{
			memset ((void *)a->av_sum[pixout], 0, sizeof(double) * dMAX_PIXELS);
			sm_invalidate(a, pixout);	// Jull1ano fork
		}
		LeaveCriticalSection (&a->ResampleSection);
	}
}

PORT
void SetDisplayAvBackmult (int disp, int pixout, double mult)
{
	DP a = pdisp[disp];
	if (a->av_backmult[pixout] != mult)
	{
		EnterCriticalSection (&a->ResampleSection);
		a->av_backmult[pixout] = mult;
		LeaveCriticalSection (&a->ResampleSection);
	}
}

PORT
void SetDisplaySampleRate (int disp, int rate)
{
	DP a = pdisp[disp];
	if (a->sample_rate != rate)
	{
		EnterCriticalSection (&a->ResampleSection);
		a->sample_rate = rate;
		CalcBandwidthNormalization (a);
		EnterCriticalSection (&a->PixelRefSection);
		a->ev_count = 0;			// Jull1ano fork: samples from the old rate cannot be rotated
		a->base_lo = a->pixel_ref;
		LeaveCriticalSection (&a->PixelRefSection);
		LeaveCriticalSection (&a->ResampleSection);
	}
}

PORT
void SetDisplayNormOneHz (int disp, int pixout, int norm)
{
	DP a = pdisp[disp];
	if (a->normalize[pixout] != norm)
	{
		EnterCriticalSection (&a->ResampleSection);
		a->normalize[pixout] = norm;
		LeaveCriticalSection (&a->ResampleSection);
	}
}

PORT
double GetDisplayENB (int disp)
{
	DP a = pdisp[disp];
	double enb;
	EnterCriticalSection(&a->SetAnalyzerSection);
	enb = 1.0 / a->inv_enb;
	LeaveCriticalSection(&a->SetAnalyzerSection);
	return enb;
}
