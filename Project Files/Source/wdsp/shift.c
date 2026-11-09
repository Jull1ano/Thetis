/*  shift.c

This file is part of a program that implements a Software-Defined Radio.

Copyright (C) 2013 Warren Pratt, NR0V

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

void calc_shift (SHIFT a)
{
	a->delta = TWOPI * a->shift / a->rate;
	a->cos_delta = cos (a->delta);
	a->sin_delta = sin (a->delta);
}

SHIFT create_shift (int run, int size, double* in, double* out, int rate, double fshift)
{
	SHIFT a = (SHIFT) malloc0 (sizeof (shift));
	a->run = run;
	a->size = size;
	a->in = in;
	a->out = out;
	a->rate = (double)rate;
	a->shift = fshift;
	a->phase = 0.0;
	calc_shift (a);
	return a;
}

void destroy_shift (SHIFT a)
{
	_aligned_free (a);
}

// Jull1ano fork: apply every scheduled change now (the samples they were aimed at are gone)
static void shift_apply_pending (SHIFT a)
{
	if (a->npend > 0)
	{
		a->shift = a->pend_f[a->npend - 1];
		calc_shift (a);
		a->npend = 0;
	}
}

void flush_shift (SHIFT a)
{
	a->phase = 0.0;
	shift_apply_pending (a);
	a->last_at = -(1LL << 40);
}

// Jull1ano fork: gain of the soft mute around a scheduled change d samples away
static double shift_gain (SHIFT a, long long d)
{
	if (d < 0) d = -d;
	if (d < a->guard) return 0.0;
	d -= a->guard;
	if (d < a->ramp) return 0.5 - 0.5 * cos (PI * (double)d / (double)a->ramp);
	return 1.0;
}

void xshift (SHIFT a)
{
	long long n0 = a->proc;
	int envelope = (a->guard > 0 || a->ramp > 0) &&
		(a->npend > 0 || n0 < a->last_at + a->guard + a->ramp);
	if (a->npend == 0 && !envelope)
	{
		// unchanged original path
		if (a->run)
		{
			int i;
			double I1, Q1, t1, t2;
			double cos_phase = cos (a->phase);
			double sin_phase = sin (a->phase);
			for (i = 0; i < a->size; i++)
			{
				I1 = a->in[2 * i + 0];
				Q1 = a->in[2 * i + 1];
				a->out[2 * i + 0] = I1 * cos_phase - Q1 * sin_phase;
				a->out[2 * i + 1] = I1 * sin_phase + Q1 * cos_phase;
				t1 = cos_phase;
				t2 = sin_phase;
				cos_phase = t1 * a->cos_delta - t2 * a->sin_delta;
				sin_phase = t1 * a->sin_delta + t2 * a->cos_delta;
				a->phase += a->delta;
				if (a->phase >= TWOPI) a->phase -= TWOPI;
				if (a->phase <   0.0 ) a->phase += TWOPI;
			}
		}
		else if (a->in != a->out)
			memcpy (a->out, a->in, a->size * sizeof (complex));
	}
	else
	{
		// a scheduled change falls into (or near) this block: switch at the exact sample, soft mute around it
		int i, k;
		long long n;
		double I1, Q1, t1, t2, g, gk;
		double cos_phase = cos (a->phase);
		double sin_phase = sin (a->phase);
		for (i = 0; i < a->size; i++)
		{
			n = n0 + i;
			while (a->npend > 0 && n >= a->pend_at[0])
			{
				a->shift = a->pend_f[0];
				calc_shift (a);
				a->last_at = a->pend_at[0];
				a->npend--;
				for (k = 0; k < a->npend; k++)
				{
					a->pend_at[k] = a->pend_at[k + 1];
					a->pend_f[k] = a->pend_f[k + 1];
				}
			}
			g = 1.0;
			if (a->guard > 0 || a->ramp > 0)
			{
				g = shift_gain (a, n - a->last_at);
				for (k = 0; k < a->npend && g > 0.0; k++)
				{
					gk = shift_gain (a, a->pend_at[k] - n);
					if (gk < g) g = gk;
				}
			}
			I1 = a->in[2 * i + 0];
			Q1 = a->in[2 * i + 1];
			if (a->run)
			{
				a->out[2 * i + 0] = g * (I1 * cos_phase - Q1 * sin_phase);
				a->out[2 * i + 1] = g * (I1 * sin_phase + Q1 * cos_phase);
				t1 = cos_phase;
				t2 = sin_phase;
				cos_phase = t1 * a->cos_delta - t2 * a->sin_delta;
				sin_phase = t1 * a->sin_delta + t2 * a->cos_delta;
				a->phase += a->delta;
				if (a->phase >= TWOPI) a->phase -= TWOPI;
				if (a->phase <   0.0 ) a->phase += TWOPI;
			}
			else
			{
				a->out[2 * i + 0] = g * I1;
				a->out[2 * i + 1] = g * Q1;
			}
		}
	}
	a->proc += a->size;
}

void setBuffers_shift(SHIFT a, double* in, double* out)
{
	a->in = in;
	a->out = out;
}

void setSamplerate_shift (SHIFT a, int rate)
{
	a->rate = rate;
	a->phase = 0.0;
	shift_apply_pending (a);
	calc_shift(a);
}

void setSize_shift (SHIFT a, int size)
{
	a->size = size;
	flush_shift (a);
}

/********************************************************************************************************
*																										*
*											RXA Properties												*
*																										*
********************************************************************************************************/

PORT
void SetRXAShiftRun (int channel, int run)
{
	EnterCriticalSection (&ch[channel].csDSP);
	rxa[channel].shift.p->run = run;
	LeaveCriticalSection (&ch[channel].csDSP);
}

PORT
void SetRXAShiftFreq (int channel, double fshift)
{
	SHIFT a;
	EnterCriticalSection (&ch[channel].csDSP);
	a = rxa[channel].shift.p;
	a->npend = 0;					// Jull1ano fork: an immediate change wins over scheduled ones
	a->shift = fshift;
	calc_shift (a);
	LeaveCriticalSection (&ch[channel].csDSP);
}

// Jull1ano fork: change the shift at the input sample where a DDC retune (sent right now) is expected to
// start: the samples already queued for this channel, plus delay_ms (radio + transport latency). The DDC
// and the shift then move together, so a pan under a fixed VFO does not play a few ms at the wrong
// frequency. guard_ms / ramp_ms add a short soft mute around the switch to cover the timing uncertainty.
PORT
void SetRXAShiftFreqAt (int channel, double fshift, double delay_ms, double guard_ms, double ramp_ms)
{
	SHIFT a;
	IOB b;
	int queued;
	long long at;
	EnterCriticalSection (&ch[channel].csDSP);
	a = rxa[channel].shift.p;
	b = ch[channel].iob.pd;
	queued = b->r1_inidx - b->r1_outidx;
	if (queued < 0) queued += b->r1_active_buffsize;
	queued = (int)((double)queued * a->rate / (double)ch[channel].in_rate + 0.5);	// shift runs at in_rate
	at = a->proc + queued + llround(delay_ms * 1.0e-3 * a->rate);
	if (a->npend > 0 && at < a->pend_at[a->npend - 1])
		at = a->pend_at[a->npend - 1];
	if (a->npend == SHIFT_MAX_PEND)
	{
		int k;
		a->shift = a->pend_f[0];
		calc_shift (a);
		a->npend--;
		for (k = 0; k < a->npend; k++)
		{
			a->pend_at[k] = a->pend_at[k + 1];
			a->pend_f[k] = a->pend_f[k + 1];
		}
	}
	a->pend_at[a->npend] = at;
	a->pend_f[a->npend] = fshift;
	a->npend++;
	a->guard = (int)llround(guard_ms * 1.0e-3 * a->rate);
	a->ramp = (int)llround(ramp_ms * 1.0e-3 * a->rate);
	if (a->guard < 0) a->guard = 0;
	if (a->ramp < 0) a->ramp = 0;
	LeaveCriticalSection (&ch[channel].csDSP);
}
