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
    public static class MathsUtil
    {
        public static readonly double sqrt2 = Math.Sqrt(2.0);

        public static double Clamp(double input, double low, double high)
        {
            return input < low ? low : input > high ? high : input;
        }

        public static T[] ShiftArray<T>(T[] array, int shiftAmount)
        {
            T[] output = new T[array.Length];
            shiftAmount += array.Length;
            for(int i = 0; i < array.Length; i++)
            {
                output[i] = array[(i + shiftAmount) % array.Length];
            }
            return output;
        }

        public static double[] ShiftArrayInterp(double[] array, double shiftAmount)
        {
            double[] output = new double[array.Length];
            shiftAmount += array.Length;
            int intshift = (int)shiftAmount;
            double beforeFac = 1 - (shiftAmount - intshift);
            double afterFac = shiftAmount - intshift;
            for (int i = 0; i < array.Length; i++)
            {
                output[i] = beforeFac * array[(i + intshift) % array.Length] + afterFac * array[(i + intshift + 1) % array.Length];
            }
            return output;
        }

        public static double[] MakeFIRFilter(double sampleRate, int size, double center, double width, double attenuation)
        {
            double[] outfir = new double[size];
            double integral = 0.0;
            double integpoint = 0.0;
            double freqpointbef = 0.0;
            double freqpointmid = 0.0;
            double freqpointaf = 0.0;
            double sampleTime = 1 / sampleRate;
            int totpoints = 8192;

            for(int i = 0; i < size; i++)
            {
                integral = 0.0;
                for(int j = 0; j < totpoints; j++) //Integrate with Simpson's rule
                {
                    freqpointbef = sampleRate * ((((double)j) / ((double)totpoints)) - 0.5);
                    freqpointaf = sampleRate * ((((double)(j + 1)) / ((double)totpoints)) - 0.5);
                    freqpointmid = (freqpointaf + freqpointbef) * 0.5;
                    integpoint = Math.Cos(2.0 * Math.PI * freqpointbef * sampleTime * i) / Math.Sqrt(1 + Math.Pow(Math.Abs((freqpointbef - center) / (width * 0.5)), 2 * attenuation));
                    integpoint += (4.0 * Math.Cos(2.0 * Math.PI * freqpointmid * sampleTime * i)) / Math.Sqrt(1 + Math.Pow(Math.Abs((freqpointmid - center) / (width * 0.5)), 2 * attenuation));
                    integpoint += Math.Cos(2.0 * Math.PI * freqpointaf * sampleTime * i) / Math.Sqrt(1 + Math.Pow(Math.Abs((freqpointaf - center) / (width * 0.5)), 2 * attenuation));
                    integpoint *= (freqpointaf - freqpointbef) / 6.0;
                    integral += integpoint / sampleRate;
                }
                if (i != 0) integral *= 2.0;
                outfir[i] = integral;
            }

            return outfir;
        }

        public static double[] FIRFilter(double[] signal, double[] fir)
        {
            double[] output = new double[signal.Length];

            for(int i = 0; i < fir.Length; i++) //Ease in
            {
                output[i] = 0.0;
                for(int j = 0; j <= i; j++)
                {
                    output[i] += signal[i - j] * fir[j];
                }
            }

            ParallelOptions opt = new ParallelOptions(); //Main loop
            opt.MaxDegreeOfParallelism = 64;
            opt.TaskScheduler = TaskScheduler.Current;
            Parallel.ForEach(Partitioner.Create(fir.Length, signal.Length), opt, (range) =>
            {
                for (int i = range.Item1; i < range.Item2; i++)
                {
                    output[i] = 0.0;
                    for(int j = 0; j < fir.Length; j++)
                    {
                        output[i] += signal[i - j] * fir[j];
                    }
                }
            });

            return output;
        }

        public static double[] FIRFilterShift(double[] signal, double[] fir, double sampleTime, double centerangfreq)
        {
            double[] shiftfir = new double[fir.Length];
            double time = 0.0;

            for(int i = 0; i < fir.Length; i++)
            {
                time = i * sampleTime;
                shiftfir[i] = fir[i] * Math.Cos(centerangfreq * time);
            }

            return FIRFilter(signal, shiftfir);
        }

        public static double[] FIRFilterCrosstalk(double[] signal, double[] fir, double crosstalk)
        {
            double[] shiftfir = new double[fir.Length];

            shiftfir[0] = (1.0 - crosstalk) * fir[0] + crosstalk;
            for (int i = 1; i < fir.Length; i++)
            {
                shiftfir[i] = fir[i] * (1.0 - crosstalk);
            }

            return FIRFilter(signal, shiftfir);
        }

        public static double[] FIRFilterCrosstalkShift(double[] signal, double[] fir, double crosstalk, double sampleTime, double centerangfreq)
        {
            double[] shiftfir = new double[fir.Length];
            double time = 0.0;

            shiftfir[0] = (1.0 - crosstalk) * fir[0] + crosstalk;
            for (int i = 1; i < fir.Length; i++)
            {
                time = i * sampleTime;
                shiftfir[i] = fir[i] * Math.Cos(centerangfreq * time) * (1.0 - crosstalk);
            }

            return FIRFilter(signal, shiftfir);
        }
    }
}
