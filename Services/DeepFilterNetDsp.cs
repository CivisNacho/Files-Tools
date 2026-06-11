using System;

namespace Files_Tools.Services;

/// <summary>
/// Signal-processing for DeepFilterNet3 speech enhancement, ported to match the Rust libDF
/// reference bit-for-bit (within fp32 tolerance): Vorbis-window STFT/ISTFT at 48 kHz, ERB band
/// structure, the two normalized feature streams the encoder consumes, and the ERB-mask + complex
/// deep-filter reconstruction. The neural net runs in ONNX Runtime; this is the surrounding DSP.
/// Verified against the validated Python reference in <c>DeepFilterNetDspTests</c>.
/// </summary>
internal static class DeepFilterNetDsp
{
    public const int SampleRate = 48000;
    public const int Fft = 960;
    public const int Hop = 480;
    public const int Freq = (Fft / 2) + 1;   // 481
    public const int NbErb = 32;
    public const int NbDf = 96;
    public const int DfOrder = 5;
    public const int Lookahead = 2;

    // libDF scales the analysis spectrum by 1/(fft^2 / (2*hop)) = 1/960.
    public const double WNorm = (2.0 * Hop) / ((double)Fft * Fft);
    // EMA normalization coefficient: exp(-hop/(sr*norm_tau)), norm_tau=1.
    public static readonly double Alpha = Math.Exp(-(double)Hop / SampleRate);
    // Algorithmic delay trimmed from the output start: (fft-hop) + lookahead*hop = 1440.
    public const int Delay = (Fft - Hop) + (Lookahead * Hop);

    /// <summary>Vorbis window: w[n] = sin(π/2 · sin²(π·(n+0.5)/N)).</summary>
    public static double[] VorbisWindow()
    {
        var w = new double[Fft];
        for (int n = 0; n < Fft; n++)
        {
            double s = Math.Sin(Math.PI * (n + 0.5) / Fft);
            w[n] = Math.Sin(Math.PI / 2 * s * s);
        }

        return w;
    }

    /// <summary>ERB band widths (bin counts per band, summing to <see cref="Freq"/>).</summary>
    public static int[] ErbFb()
    {
        const double nyq = SampleRate / 2.0;
        static double Freq2Erb(double f) => 9.265 * Math.Log(1 + (f / (24.7 * 9.265)));
        static double Erb2Freq(double e) => 24.7 * 9.265 * (Math.Exp(e / 9.265) - 1);

        double erbLow = Freq2Erb(0), erbHigh = Freq2Erb(nyq);
        double binHz = (double)SampleRate / Fft;
        var widths = new int[NbErb];
        int prev = 0;
        for (int i = 1; i <= NbErb; i++)
        {
            double step = erbLow + ((erbHigh - erbLow) * i / NbErb);
            double f = Erb2Freq(step);
            int b = (int)Math.Round(f / binHz, MidpointRounding.AwayFromZero);
            b = Math.Max(b, prev + 2);   // min_nb_freqs = 2
            b = Math.Min(b, Freq);
            widths[i - 1] = b - prev;
            prev = b;
        }

        int sum = 0;
        foreach (var v in widths)
        {
            sum += v;
        }

        widths[NbErb - 1] += Freq - sum; // last band absorbs the remainder
        return widths;
    }

    /// <summary>Frame-major one-sided STFT (Re/Im length = frames * Freq), with libDF's wnorm scaling.</summary>
    public static (double[] Re, double[] Im, int Frames) Stft(ReadOnlySpan<float> signal, double[] window)
    {
        // libDF prepends one hop of zero "overlap memory".
        int paddedLen = Hop + signal.Length;
        int frames = paddedLen >= Fft ? 1 + ((paddedLen - Fft) / Hop) : 1;

        var re = new double[frames * Freq];
        var im = new double[frames * Freq];
        var bufRe = new double[Fft];
        var bufIm = new double[Fft];

        for (int t = 0; t < frames; t++)
        {
            int start = (t * Hop) - Hop; // position in the original signal (first hop is zero-pad)
            for (int i = 0; i < Fft; i++)
            {
                int idx = start + i;
                double s = idx >= 0 && idx < signal.Length ? signal[idx] : 0.0;
                bufRe[i] = s * window[i];
                bufIm[i] = 0.0;
            }

            Fft960.Transform(bufRe, bufIm, inverse: false);
            int o = t * Freq;
            for (int f = 0; f < Freq; f++)
            {
                re[o + f] = bufRe[f] * WNorm;
                im[o + f] = bufIm[f] * WNorm;
            }
        }

        return (re, im, frames);
    }

    /// <summary>Inverse STFT (window²-overlap-add), returning the signal after the leading hop.</summary>
    public static float[] Istft(double[] re, double[] im, int frames, double[] window)
    {
        int paddedLen = Hop * (frames + 1);
        var outBuf = new double[paddedLen];
        var wsum = new double[paddedLen];
        var bufRe = new double[Fft];
        var bufIm = new double[Fft];

        for (int t = 0; t < frames; t++)
        {
            int o = t * Freq;
            for (int f = 0; f < Freq; f++)
            {
                bufRe[f] = re[o + f];
                bufIm[f] = im[o + f];
            }

            for (int f = Freq; f < Fft; f++)
            {
                int mirror = Fft - f;
                bufRe[f] = re[o + mirror];
                bufIm[f] = -im[o + mirror];
            }

            Fft960.Transform(bufRe, bufIm, inverse: true);
            int start = t * Hop;
            for (int i = 0; i < Fft; i++)
            {
                double wv = window[i];
                outBuf[start + i] += bufRe[i] * wv;
                wsum[start + i] += wv * wv;
            }
        }

        // Normalize by the steady-state overlap (constant; =1 for the Vorbis PR window) rather than
        // per-sample wsum, so partial-overlap edge frames are attenuated, never amplified (which
        // would blow up after the wnorm gain). Matches libDF frame_synthesis.
        double wmax = 1e-9;
        for (int i = 0; i < wsum.Length; i++)
        {
            if (wsum[i] > wmax)
            {
                wmax = wsum[i];
            }
        }

        var result = new float[paddedLen - Hop];
        for (int i = 0; i < result.Length; i++)
        {
            result[i] = (float)(outBuf[Hop + i] / wmax);
        }

        return result;
    }

    /// <summary>
    /// Computes the two encoder feature streams from a frame-major spectrum:
    /// <paramref name="featErb"/> = [frames * NbErb], <paramref name="featSpec"/> = [frames * 2 * NbDf]
    /// (channel-major: real plane then imag plane per frame).
    /// </summary>
    public static void Features(double[] re, double[] im, int frames, int[] erbWidths,
        out float[] featErb, out float[] featSpec)
    {
        featErb = new float[frames * NbErb];
        featSpec = new float[frames * 2 * NbDf];

        // libDF init_norm_states: linspace, NOT zeros.
        var erbState = new double[NbErb];
        for (int b = 0; b < NbErb; b++)
        {
            erbState[b] = -60.0 + ((-90.0 - -60.0) * b / (NbErb - 1));
        }

        var specState = new double[NbDf];
        for (int f = 0; f < NbDf; f++)
        {
            specState[f] = 0.001 + ((0.0001 - 0.001) * f / (NbDf - 1));
        }

        for (int t = 0; t < frames; t++)
        {
            int o = t * Freq;

            // feat_erb: per-band mean power -> dB -> EMA mean-norm / 40.
            int bin = 0;
            for (int b = 0; b < NbErb; b++)
            {
                double power = 0.0;
                int width = erbWidths[b];
                for (int j = 0; j < width; j++)
                {
                    double r = re[o + bin + j], i2 = im[o + bin + j];
                    power += (r * r) + (i2 * i2);
                }

                power /= width;
                bin += width;
                double db = 10.0 * Math.Log10(power + 1e-10);
                erbState[b] = (db * (1 - Alpha)) + (erbState[b] * Alpha);
                featErb[(t * NbErb) + b] = (float)((db - erbState[b]) / 40.0);
            }

            // feat_spec: first NbDf bins, complex unit-norm by sqrt(EMA magnitude).
            int reBase = t * 2 * NbDf;
            int imBase = reBase + NbDf;
            for (int f = 0; f < NbDf; f++)
            {
                double r = re[o + f], i2 = im[o + f];
                double mag = Math.Sqrt((r * r) + (i2 * i2));
                specState[f] = (mag * (1 - Alpha)) + (specState[f] * Alpha);
                double d = Math.Sqrt(specState[f]);
                featSpec[reBase + f] = (float)(r / d);
                featSpec[imBase + f] = (float)(i2 / d);
            }
        }
    }

    /// <summary>
    /// Applies the model outputs to the noisy spectrum and reconstructs audio: per output frame t
    /// (model frame nm = min(t+lookahead, frames-1)), multiply all 481 bins by the expanded ERB mask,
    /// then overwrite bins 0..95 with the 5-tap complex deep filter over the noisy spectrum (±2),
    /// then ISTFT and trim the algorithmic delay. <paramref name="mask"/> = [frames*NbErb];
    /// <paramref name="coefs"/> = [frames*NbDf*DfOrder*2] (per freq: order pairs of re,im).
    /// </summary>
    public static float[] ApplyAndReconstruct(
        double[] re, double[] im, int frames, int[] erbWidths,
        float[] mask, float[] coefs, double[] window)
    {
        var outRe = new double[frames * Freq];
        var outIm = new double[frames * Freq];

        for (int t = 0; t < frames; t++)
        {
            int nm = Math.Min(t + Lookahead, frames - 1);
            int o = t * Freq;

            // ERB mask over all bins.
            int bin = 0;
            for (int b = 0; b < NbErb; b++)
            {
                float g = mask[(nm * NbErb) + b];
                int width = erbWidths[b];
                for (int j = 0; j < width; j++)
                {
                    outRe[o + bin + j] = re[o + bin + j] * g;
                    outIm[o + bin + j] = im[o + bin + j] * g;
                }

                bin += width;
            }

            // Deep filter on the first NbDf bins over the NOISY spectrum.
            int coefFrame = nm * NbDf * DfOrder * 2;
            for (int f = 0; f < NbDf; f++)
            {
                double accRe = 0.0, accIm = 0.0;
                int cBase = coefFrame + (f * DfOrder * 2);
                for (int oo = 0; oo < DfOrder; oo++)
                {
                    int src = t - 2 + oo;
                    if (src < 0 || src >= frames)
                    {
                        continue;
                    }

                    double cr = coefs[cBase + (oo * 2)];
                    double ci = coefs[cBase + (oo * 2) + 1];
                    double sr = re[(src * Freq) + f], si = im[(src * Freq) + f];
                    accRe += (sr * cr) - (si * ci);
                    accIm += (sr * ci) + (si * cr);
                }

                outRe[o + f] = accRe;
                outIm[o + f] = accIm;
            }
        }

        var enhanced = Istft(outRe, outIm, frames, window);
        if (Delay >= enhanced.Length)
        {
            return enhanced;
        }

        var trimmed = new float[enhanced.Length - Delay];
        Array.Copy(enhanced, Delay, trimmed, 0, trimmed.Length);
        return trimmed;
    }
}

/// <summary>
/// In-place complex FFT for the DeepFilterNet transform size (960 = non-power-of-two): Bluestein's
/// chirp-z over a power-of-two radix-2 FFT. Inverse is 1/N scaled.
/// </summary>
internal static class Fft960
{
    public static void Transform(double[] re, double[] im, bool inverse)
    {
        int n = re.Length;
        if (n == 0)
        {
            return;
        }

        if ((n & (n - 1)) == 0)
        {
            Radix2(re, im, inverse);
        }
        else
        {
            Bluestein(re, im, inverse);
        }

        if (inverse)
        {
            for (int i = 0; i < n; i++)
            {
                re[i] /= n;
                im[i] /= n;
            }
        }
    }

    private static void Radix2(double[] re, double[] im, bool inverse)
    {
        int n = re.Length;
        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1)
            {
                j ^= bit;
            }

            j ^= bit;
            if (i < j)
            {
                (re[i], re[j]) = (re[j], re[i]);
                (im[i], im[j]) = (im[j], im[i]);
            }
        }

        double sign = inverse ? 1.0 : -1.0;
        for (int len = 2; len <= n; len <<= 1)
        {
            double ang = sign * 2.0 * Math.PI / len;
            double wRe = Math.Cos(ang), wIm = Math.Sin(ang);
            for (int i = 0; i < n; i += len)
            {
                double curRe = 1.0, curIm = 0.0;
                for (int k = 0; k < len / 2; k++)
                {
                    int a = i + k, b = i + k + (len / 2);
                    double vRe = (re[b] * curRe) - (im[b] * curIm);
                    double vIm = (re[b] * curIm) + (im[b] * curRe);
                    re[b] = re[a] - vRe;
                    im[b] = im[a] - vIm;
                    re[a] += vRe;
                    im[a] += vIm;
                    double nextRe = (curRe * wRe) - (curIm * wIm);
                    curIm = (curRe * wIm) + (curIm * wRe);
                    curRe = nextRe;
                }
            }
        }
    }

    private static void Bluestein(double[] re, double[] im, bool inverse)
    {
        int n = re.Length;
        int m = 1;
        while (m < (2 * n) + 1)
        {
            m <<= 1;
        }

        double sign = inverse ? 1.0 : -1.0;
        var cos = new double[n];
        var sin = new double[n];
        for (int i = 0; i < n; i++)
        {
            long j = (long)i * i % (2L * n);
            double ang = sign * Math.PI * j / n;
            cos[i] = Math.Cos(ang);
            sin[i] = Math.Sin(ang);
        }

        var aRe = new double[m];
        var aIm = new double[m];
        for (int i = 0; i < n; i++)
        {
            aRe[i] = (re[i] * cos[i]) - (im[i] * sin[i]);
            aIm[i] = (re[i] * sin[i]) + (im[i] * cos[i]);
        }

        var bRe = new double[m];
        var bIm = new double[m];
        bRe[0] = cos[0];
        bIm[0] = -sin[0];
        for (int i = 1; i < n; i++)
        {
            bRe[i] = bRe[m - i] = cos[i];
            bIm[i] = bIm[m - i] = -sin[i];
        }

        Radix2(aRe, aIm, inverse: false);
        Radix2(bRe, bIm, inverse: false);
        for (int i = 0; i < m; i++)
        {
            double tRe = (aRe[i] * bRe[i]) - (aIm[i] * bIm[i]);
            aIm[i] = (aRe[i] * bIm[i]) + (aIm[i] * bRe[i]);
            aRe[i] = tRe;
        }

        Radix2(aRe, aIm, inverse: true);
        for (int i = 0; i < m; i++)
        {
            aRe[i] /= m;
            aIm[i] /= m;
        }

        for (int i = 0; i < n; i++)
        {
            re[i] = (aRe[i] * cos[i]) - (aIm[i] * sin[i]);
            im[i] = (aRe[i] * sin[i]) + (aIm[i] * cos[i]);
        }
    }
}
