using System;
using System.Numerics;
using System.Threading.Tasks;
using System.Collections.Concurrent;

/*
 * Maths utilities
 * 2022-2023 Maxim Hoxha
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 */

namespace AnalogueConvertEffect
{
    public struct FIRFilter
    {
        public double[] fir;
        public int forwardLen;
        public int backport;

        public FIRFilter(int forL, int backL)
        {
            fir = new double[forL + backL];
            forwardLen = forL;
            backport = backL;
        }

        public double this[int x]
        {
            get { return fir[x + backport]; }
            set { fir[x + backport] = value; }
        }
    }

    public static class MathsUtil
    {
        public static readonly double sqrt2 = Math.Sqrt(2.0);
        public const int totpoints = 8192;
        public const double totpointsdbl = 8192.0;
        public const double minMagnitude = 0.001;
        public const int maxStepsUnderMinMag = 6;


        public static double Clamp(double input, double low, double high)
        {
            return input < low ? low : input > high ? high : input;
        }

        public static double SRGBGammaTransform(double val)
        {
            return val > 0.04045 ? Math.Pow((val + 0.055) / 1.055, 2.4) : (val / 12.92);
        }

        public static double SRGBInverseGammaTransform(double val)
        {
            return val > 0.0031308 ? (Math.Pow(val, 1 / 2.4) * 1.055) - 0.055 : val * 12.92;
        }

        public static FIRFilter MakeFIRFilter(double sampleRate, int size, double center, double width, double attenuation)
        {
            FIRFilter outfir = new FIRFilter(size, 5);
            double integral = 0.0;
            double integpoint = 0.0;
            double freqpointbef = 0.0;
            double freqpointmid = 0.0;
            double freqpointaf = 0.0;
            double sampleTime = 1 / sampleRate;
            int truesize = 0;
            int truebackport = 0;
            int stepsUnderTolerance = 0;

            for (int i = -1; i >= -outfir.backport; i--)
            {
                integral = 0.0;
                for (int j = 0; j < totpoints; j++) //Integrate with Simpson's rule
                {
                    //The following integral bounds may seem strange, but they were found to create better filters that don't require rescaling
                    freqpointbef = (sampleRate * ((((double)j) / totpointsdbl) - 0.5)) + center;
                    freqpointaf = (sampleRate * ((((double)(j + 1)) / totpointsdbl) - 0.5)) + center;
                    freqpointmid = (freqpointaf + freqpointbef) * 0.5;
                    integpoint = Math.Cos(2.0 * Math.PI * freqpointbef * sampleTime * i) / Math.Sqrt(1 + Math.Pow(Math.Abs((freqpointbef - center) / (width * 0.5)), 2 * attenuation));
                    integpoint += (4.0 * Math.Cos(2.0 * Math.PI * freqpointmid * sampleTime * i)) / Math.Sqrt(1 + Math.Pow(Math.Abs((freqpointmid - center) / (width * 0.5)), 2 * attenuation));
                    integpoint += Math.Cos(2.0 * Math.PI * freqpointaf * sampleTime * i) / Math.Sqrt(1 + Math.Pow(Math.Abs((freqpointaf - center) / (width * 0.5)), 2 * attenuation));
                    integpoint *= (freqpointaf - freqpointbef) / 6.0;
                    integral += integpoint / sampleRate;
                }
                outfir[i] = integral;
                truesize++;
                truebackport++;
                if (Math.Abs(integral) < minMagnitude)
                {
                    stepsUnderTolerance++;
                }
                else
                {
                    stepsUnderTolerance = 0;
                }
                if (stepsUnderTolerance >= maxStepsUnderMinMag)
                {
                    break;
                }
            }
            stepsUnderTolerance = 0;
            for (int i = 0; i < size; i++)
            {
                integral = 0.0;
                for(int j = 0; j < totpoints; j++) //Integrate with Simpson's rule
                {
                    //The following integral bounds may seem strange, but they were found to create better filters that don't require rescaling
                    freqpointbef = (sampleRate * ((((double)j) / totpointsdbl) - 0.5)) + center;
                    freqpointaf = (sampleRate * ((((double)(j + 1)) / totpointsdbl) - 0.5)) + center;
                    freqpointmid = (freqpointaf + freqpointbef) * 0.5;
                    integpoint = Math.Cos(2.0 * Math.PI * freqpointbef * sampleTime * i) / Math.Sqrt(1 + Math.Pow(Math.Abs((freqpointbef - center) / (width * 0.5)), 2 * attenuation));
                    integpoint += (4.0 * Math.Cos(2.0 * Math.PI * freqpointmid * sampleTime * i)) / Math.Sqrt(1 + Math.Pow(Math.Abs((freqpointmid - center) / (width * 0.5)), 2 * attenuation));
                    integpoint += Math.Cos(2.0 * Math.PI * freqpointaf * sampleTime * i) / Math.Sqrt(1 + Math.Pow(Math.Abs((freqpointaf - center) / (width * 0.5)), 2 * attenuation));
                    integpoint *= (freqpointaf - freqpointbef) / 6.0;
                    integral += integpoint / sampleRate;
                }
                if (i > truebackport) integral *= 2.0;
                outfir[i] = integral;
                truesize++;
                if (Math.Abs(integral) < minMagnitude)
                {
                    stepsUnderTolerance++;
                }
                else
                {
                    stepsUnderTolerance = 0;
                }
                if (stepsUnderTolerance >= maxStepsUnderMinMag)
                {
                    break;
                }
            }

            FIRFilter outfilt = new FIRFilter(truesize - truebackport, truebackport);
            for (int i = -outfilt.backport; i < outfilt.forwardLen; i++)
            {
                outfilt[i] = outfir[i];
            }

            return outfilt;
        }

        public static double[] FIRFilter(double[] signal, FIRFilter fir)
        {
            double[] output = new double[signal.Length];

            for(int i = 0; i < fir.forwardLen; i++) //Ease in
            {
                output[i] = 0.0;
                for(int j = -fir.backport; j <= i; j++)
                {
                    output[i] += signal[i - j] * fir[j];
                }
            }

            ParallelOptions opt = new ParallelOptions(); //Main loop
            opt.MaxDegreeOfParallelism = 64;
            opt.TaskScheduler = TaskScheduler.Current;
            Parallel.ForEach(Partitioner.Create(fir.forwardLen, signal.Length - fir.backport), opt, (range) =>
            {
                for (int i = range.Item1; i < range.Item2; i++)
                {
                    double outsig = 0.0;
                    for(int j = -fir.backport; j < fir.forwardLen; j++)
                    {
                        outsig += signal[i - j] * fir[j];
                    }
                    output[i] = outsig;
                }
            });

            for (int i = signal.Length - fir.backport; i < signal.Length; i++) //Ease out
            {
                output[i] = 0.0;
                for (int j = i - signal.Length + 1; j < fir.forwardLen; j++)
                {
                    output[i] += signal[i - j] * fir[j];
                }
            }

            return output;
        }

        public static double[] FIRFilterShift(double[] signal, FIRFilter fir, double sampleTime, double centerangfreq)
        {
            FIRFilter shiftfir = new FIRFilter(fir.forwardLen, fir.backport);
            double time = 0.0;

            for (int i = -fir.backport; i < fir.forwardLen; i++)
            {
                time = i * sampleTime;
                shiftfir[i] = fir[i] * Math.Cos(centerangfreq * time) * 2.0;
            }

            return FIRFilter(signal, shiftfir);
        }

        public static double[] FIRFilterCrosstalk(double[] signal, FIRFilter fir, double crosstalk)
        {
            FIRFilter shiftfir = new FIRFilter(fir.forwardLen, fir.backport);

            for (int i = -fir.backport; i < fir.forwardLen; i++)
            {
                shiftfir[i] = fir[i] * (1.0 - crosstalk);
            }
            shiftfir[0] = (1.0 - crosstalk) * fir[0] + crosstalk;

            return FIRFilter(signal, shiftfir);
        }

        public static double[] FIRFilterCrosstalkShift(double[] signal, FIRFilter fir, double crosstalk, double sampleTime, double centerangfreq)
        {
            FIRFilter shiftfir = new FIRFilter(fir.forwardLen, fir.backport);
            double time = 0.0;

            for (int i = -fir.backport; i < fir.forwardLen; i++)
            {
                time = i * sampleTime;
                shiftfir[i] = fir[i] * Math.Cos(centerangfreq * time) * (1.0 - crosstalk) * 2.0;
            }
            shiftfir[0] = (1.0 - crosstalk) * fir[0] + crosstalk;

            return FIRFilter(signal, shiftfir);
        }
    }
}
