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
        private static readonly double passCalcTolerance = 0.0001; //The difference from zero or one before explicit calculation of attenuation is halted, because there is no point
        private static readonly int chunkSize = 4096; //Size of the parallelisation chunks

        private unsafe struct FTKernelData
        {
            public Complex* rPtr;
            public Complex* ePtr;
            public Complex* oPtr;

            public FTKernelData(Complex* rp, Complex* ep, Complex* op)
            {
                rPtr = rp;
                ePtr = ep;
                oPtr = op;
            }
        }

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

        public static Complex[] ShiftArrayInterp(Complex[] array, double shiftAmount)
        {
            Complex[] output = new Complex[array.Length];
            shiftAmount += array.Length;
            int intshift = (int)shiftAmount;
            double beforeFac = 1 - (shiftAmount - intshift);
            double afterFac = shiftAmount - intshift;
            for(int i = 0; i < array.Length; i++)
            {
                output[i] = beforeFac * array[(i + intshift) % array.Length] + afterFac * array[(i + intshift + 1) % array.Length];
            }
            return output;
        }

        public static Complex[] BandPassFilter(Complex[] spectrum, double spectrumWidth, double center, double width, double attenuation, double strength = 1.0)
        {
            Complex[] output = new Complex[spectrum.Length];

            //Calculate attenuation
            double freq = 0.0;
            double sampleRate = spectrumWidth / spectrum.Length;
            double lowLev = 1.0 - strength;
            for (int i = -spectrum.Length / 2; i < spectrum.Length / 2; i++) 
            {
                freq = i * sampleRate;
                output[(i + spectrum.Length) % spectrum.Length] = spectrum[(i + spectrum.Length) % spectrum.Length] * (lowLev + (strength / Math.Sqrt(1 + Math.Pow(Math.Abs((freq - center) / (width * 0.5)), 2*attenuation))));
            }

            return output;
        }

        public static Complex[] NotchFilter(Complex[] spectrum, double spectrumWidth, double center, double width, double attenuation, double strength = 1.0)
        {
            Complex[] output = new Complex[spectrum.Length];

            //Calculate attenuation
            double freq = 0.0;
            double sampleRate = spectrumWidth / spectrum.Length;
            double lowLev = 1.0 - strength;
            for (int i = -spectrum.Length / 2; i < spectrum.Length / 2; i++)
            {
                freq = i * sampleRate;
                output[(i + spectrum.Length) % spectrum.Length] = spectrum[(i + spectrum.Length) % spectrum.Length] * (1 - (strength / Math.Sqrt(1 + Math.Pow(Math.Abs((freq - center) / (width * 0.5)), 2 * attenuation))));
            }

            return output;
        }

        public static Complex[] ShiftFilter(Complex[] signal, double shiftAmount, double size = 1.0, double origAmount = 1.0)
        {
            Complex[] output = new Complex[signal.Length];
            int intshiftL = (int)shiftAmount;
            int intshiftR = -intshiftL;
            intshiftL += signal.Length;
            intshiftR += signal.Length;
            shiftAmount += signal.Length;
            double beforeFacL = 1 - (shiftAmount - intshiftL);
            double afterFacL = shiftAmount - intshiftL;
            double beforeFacR = 1 - (shiftAmount - intshiftR);
            double afterFacR = shiftAmount - intshiftR;
            for (int i = 0; i < signal.Length; i++)
            {
                output[i] = (signal[i] * origAmount +
                             size * (beforeFacL * signal[(i + intshiftL) % signal.Length] + afterFacL * signal[(i + intshiftL + 1) % signal.Length]) +
                             size * (beforeFacR * signal[(i + intshiftR) % signal.Length] + afterFacR * signal[(i + intshiftR + 1) % signal.Length])) / (origAmount + 2.0 * size);
            }
            return output;
        }

        public static Complex[] FourierTransform(double[] input, int zeroPadFactor = 0)
        {
            Complex[] complexifiedInput = new Complex[input.Length];
            for(int i = 0; i < input.Length; i++)
            {
                complexifiedInput[i] = input[i];
            }
            return FourierTransform(complexifiedInput, zeroPadFactor);
        }

        public static unsafe Complex[] FourierTransform(Complex[] input, int zeroPadFactor = 0) //fft time baby
        {
            if(input.Length == 1)
            {
                return new Complex[1] { input[0] };
            }
            int powOf2 = 0;
            int compLength = input.Length;
            while(compLength > 1)
            {
                compLength >>= 1;
                powOf2++;
            }
            int ftLength = 1 << powOf2;
            if(ftLength < input.Length)
            {
                ftLength <<= 1;
            }
            ftLength <<= zeroPadFactor;

            int halfLength = ftLength >> 1;
            int addEvenLength = (input.Length + 1) / 2;
            int addOddLength = input.Length / 2;
            Complex[] result = new Complex[ftLength];
            Complex[] feven = new Complex[halfLength];
            Complex[] fodd = new Complex[halfLength];
            Complex[][] fevenStack = new Complex[64][];
            Complex[][] foddStack = new Complex[64][];

            fixed ( //Skip bounds checking for extra speed
                Complex* inPtr = &input[0],
                         ePtr = &feven[0],
                         oPtr = &fodd[0]
            )
            {
                for (int i = 0; i < addEvenLength; i++)
                {
                    ePtr[i] = inPtr[2 * i];
                }
                for (int i = 0; i < addOddLength; i++)
                {
                    oPtr[i] = inPtr[2 * i + 1];
                }
            }

            fevenStack[0] = feven;
            foddStack[0] = fodd;
            int stackDepth = 1;
            int stackPoint = 1;
            int[] parity = new int[64];
            int curLen = 0;
            int curHalfLen = 0;
            Complex[] splitList;

            for(int i = 0; i < parity.Length; i++)
            {
                parity[i] = 0;
            }

            while(true) //Allocate stack arrays
            {
                fevenStack[stackDepth] = new Complex[fevenStack[stackDepth - 1].Length >> 1];
                foddStack[stackDepth] = new Complex[foddStack[stackDepth - 1].Length >> 1];
                if (fevenStack[stackDepth].Length <= 1 || foddStack[stackDepth].Length <= 1)
                {
                    break;
                }
                stackDepth++;
            }

            while(true) //Traverse the section tree
            {
                if (parity[stackPoint - 1] < 2)
                {
                    splitList = parity[stackPoint - 1] == 0 ? fevenStack[stackPoint - 1] : foddStack[stackPoint - 1];
                    curLen = splitList.Length;
                    curHalfLen = splitList.Length >> 1;
                    fixed ( //Skip bounds checking for extra speed
                    Complex* inPtr = &splitList[0],
                             ePtr = &fevenStack[stackPoint][0],
                             oPtr = &foddStack[stackPoint][0]
                    )
                    {
                        for (int i = 0; i < curHalfLen; i++) //Select entries
                        {
                            ePtr[i] = inPtr[2 * i];
                        }
                        for (int i = 0; i < curHalfLen; i++)
                        {
                            oPtr[i] = inPtr[2 * i + 1];
                        }
                    }

                    if (stackPoint < stackDepth) //Propagate down the stack if we haven't reached the end
                    {
                        stackPoint++;
                        continue;
                    }
                }
                else //Calculate results for the level above once we have all the necessary results
                {
                    parity[stackPoint - 1] = 0;
                    stackPoint--;
                    if (stackPoint == 0) break;
                    splitList = parity[stackPoint - 1] == 0 ? fevenStack[stackPoint - 1] : foddStack[stackPoint - 1];
                    curLen = splitList.Length;
                    curHalfLen = splitList.Length >> 1;
                }
                fixed ( //Skip bounds checking for extra speed
                Complex* rPtr = &splitList[0],
                         ePtr = &fevenStack[stackPoint][0],
                         oPtr = &foddStack[stackPoint][0]
                )
                {
                    if (curHalfLen <= chunkSize)
                    {
                        Complex oVal;
                        for (int i = 0; i < curHalfLen; i++)
                        {
                            oVal = Complex.Exp((-2.0 * Math.PI * Complex.ImaginaryOne / (double)curLen) * i) * oPtr[i];
                            rPtr[i] = ePtr[i] + oVal;
                            rPtr[i + curHalfLen] = ePtr[i] - oVal;
                        }
                    }
                    else
                    {
                        FTKernelData fd = new FTKernelData(rPtr, ePtr, oPtr);
                        ParallelOptions opt = new ParallelOptions();
                        opt.MaxDegreeOfParallelism = 64;
                        opt.TaskScheduler = TaskScheduler.Current;
                        Parallel.ForEach(Partitioner.Create(0, curHalfLen), opt, (range) =>
                        {
                            Complex oVal;
                            for (int i = range.Item1; i < range.Item2; i++)
                            {
                                oVal = Complex.Exp((-2.0 * Math.PI * Complex.ImaginaryOne / (double)curLen) * i) * fd.oPtr[i];
                                fd.rPtr[i] = fd.ePtr[i] + oVal;
                                fd.rPtr[i + curHalfLen] = fd.ePtr[i] - oVal;
                            }
                        });
                    }
                }
                parity[stackPoint - 1]++;
            }

            fixed ( //Skip bounds checking for extra speed
                Complex* rPtr = &result[0],
                         ePtr = &feven[0],
                         oPtr = &fodd[0]
            )
            {
                if (halfLength <= chunkSize)
                {
                    Complex oVal;
                    for (int i = 0; i < halfLength; i++)
                    {
                        oVal = Complex.Exp((-2.0 * Math.PI * Complex.ImaginaryOne / (double)ftLength) * i) * oPtr[i];
                        rPtr[i] = ePtr[i] + oVal;
                        rPtr[i + halfLength] = ePtr[i] - oVal;
                    }
                }
                else
                {
                    FTKernelData fd = new FTKernelData(rPtr, ePtr, oPtr);
                    ParallelOptions opt = new ParallelOptions();
                    opt.MaxDegreeOfParallelism = 64;
                    opt.TaskScheduler = TaskScheduler.Current;
                    Parallel.ForEach(Partitioner.Create(0, halfLength), opt, (range) =>
                    {
                        Complex oVal;
                        for (int i = range.Item1; i < range.Item2; i++)
                        {
                            oVal = Complex.Exp((-2.0 * Math.PI * Complex.ImaginaryOne / (double)ftLength) * i) * fd.oPtr[i];
                            fd.rPtr[i] = fd.ePtr[i] + oVal;
                            fd.rPtr[i + halfLength] = fd.ePtr[i] - oVal;
                        }
                    });
                }
            }

            return result;
        }
        
        public static unsafe Complex[] InverseFourierTransform(Complex[] input)
        {
            if (input.Length == 1)
            {
                return new Complex[1] { input[0] };
            }
            int powOf2 = 0;
            int compLength = input.Length;
            while (compLength > 1)
            {
                compLength >>= 1;
                powOf2++;
            }
            int ftLength = 1 << powOf2;
            if (ftLength < input.Length)
            {
                ftLength <<= 1;
            }
            double ftiLengthf = 1.0/((double)ftLength);

            int halfLength = ftLength >> 1;
            int addEvenLength = (input.Length + 1) / 2;
            int addOddLength = input.Length / 2;
            Complex[] result = new Complex[ftLength];
            Complex[] feven = new Complex[halfLength];
            Complex[] fodd = new Complex[halfLength];
            Complex[][] fevenStack = new Complex[64][];
            Complex[][] foddStack = new Complex[64][];

            fixed ( //Skip bounds checking for extra speed
                Complex* inPtr = &input[0],
                         ePtr = &feven[0],
                         oPtr = &fodd[0]
            )
            {
                for (int i = 0; i < addEvenLength; i++)
                {
                    ePtr[i] = inPtr[2 * i];
                }
                for (int i = 0; i < addOddLength; i++)
                {
                    oPtr[i] = inPtr[2 * i + 1];
                }
            }

            fevenStack[0] = feven;
            foddStack[0] = fodd;
            int stackDepth = 1;
            int stackPoint = 1;
            int[] parity = new int[64];
            int curLen = 0;
            int curHalfLen = 0;
            Complex[] splitList;

            for (int i = 0; i < parity.Length; i++)
            {
                parity[i] = 0;
            }

            while (true) //Allocate stack arrays
            {
                fevenStack[stackDepth] = new Complex[fevenStack[stackDepth - 1].Length >> 1];
                foddStack[stackDepth] = new Complex[foddStack[stackDepth - 1].Length >> 1];
                if (fevenStack[stackDepth].Length <= 1 || foddStack[stackDepth].Length <= 1)
                {
                    break;
                }
                stackDepth++;
            }

            while (true) //Traverse the section tree
            {
                if (parity[stackPoint - 1] < 2)
                {
                    splitList = parity[stackPoint - 1] == 0 ? fevenStack[stackPoint - 1] : foddStack[stackPoint - 1];
                    curLen = splitList.Length;
                    curHalfLen = splitList.Length >> 1;
                    fixed ( //Skip bounds checking for extra speed
                    Complex* inPtr = &splitList[0],
                             ePtr = &fevenStack[stackPoint][0],
                             oPtr = &foddStack[stackPoint][0]
                    )
                    {
                        for (int i = 0; i < curHalfLen; i++) //Select entries
                        {
                            ePtr[i] = inPtr[2 * i];
                        }
                        for (int i = 0; i < curHalfLen; i++)
                        {
                            oPtr[i] = inPtr[2 * i + 1];
                        }
                    }

                    if (stackPoint < stackDepth) //Propagate down the stack if we haven't reached the end
                    {
                        stackPoint++;
                        continue;
                    }
                }
                else //Calculate results for the level above once we have all the necessary results
                {
                    parity[stackPoint - 1] = 0;
                    stackPoint--;
                    if (stackPoint == 0) break;
                    splitList = parity[stackPoint - 1] == 0 ? fevenStack[stackPoint - 1] : foddStack[stackPoint - 1];
                    curLen = splitList.Length;
                    curHalfLen = splitList.Length >> 1;
                }
                fixed ( //Skip bounds checking for extra speed
                Complex* rPtr = &splitList[0],
                         ePtr = &fevenStack[stackPoint][0],
                         oPtr = &foddStack[stackPoint][0]
                )
                {
                    if (curHalfLen <= chunkSize)
                    {
                        Complex oVal;
                        for (int i = 0; i < curHalfLen; i++)
                        {
                            oVal = Complex.Exp((-2.0 * Math.PI * Complex.ImaginaryOne / (double)curLen) * i) * oPtr[i];
                            rPtr[i] = ePtr[i] + oVal;
                            rPtr[i + curHalfLen] = ePtr[i] - oVal;
                        }
                    }
                    else
                    {
                        FTKernelData fd = new FTKernelData(rPtr, ePtr, oPtr);
                        ParallelOptions opt = new ParallelOptions();
                        opt.MaxDegreeOfParallelism = 64;
                        opt.TaskScheduler = TaskScheduler.Current;
                        Parallel.ForEach(Partitioner.Create(0, curHalfLen), opt, (range) =>
                        {
                            Complex oVal;
                            for (int i = range.Item1; i < range.Item2; i++)
                            {
                                oVal = Complex.Exp((-2.0 * Math.PI * Complex.ImaginaryOne / (double)curLen) * i) * fd.oPtr[i];
                                fd.rPtr[i] = fd.ePtr[i] + oVal;
                                fd.rPtr[i + curHalfLen] = fd.ePtr[i] - oVal;
                            }
                        });
                    }
                }
                parity[stackPoint - 1]++;
            }

            fixed ( //Skip bounds checking for extra speed
                Complex* rPtr = &result[0],
                         ePtr = &feven[0],
                         oPtr = &fodd[0]
            )
            {
                if (halfLength <= chunkSize)
                {
                    Complex oVal;
                    for (int i = 0; i < halfLength; i++)
                    {
                        oVal = Complex.Exp((-2.0 * Math.PI * Complex.ImaginaryOne / (double)ftLength) * i) * oPtr[i];
                        rPtr[i] = (ePtr[i] + oVal) * ftiLengthf;
                        rPtr[i + halfLength] = (ePtr[i] - oVal) * ftiLengthf;
                    }
                }
                else
                {
                    FTKernelData fd = new FTKernelData(rPtr, ePtr, oPtr);
                    ParallelOptions opt = new ParallelOptions();
                    opt.MaxDegreeOfParallelism = 64;
                    opt.TaskScheduler = TaskScheduler.Current;
                    Parallel.ForEach(Partitioner.Create(0, halfLength), opt, (range) =>
                    {
                        Complex oVal;
                        for (int i = range.Item1; i < range.Item2; i++)
                        {
                            oVal = Complex.Exp((-2.0 * Math.PI * Complex.ImaginaryOne / (double)ftLength) * i) * fd.oPtr[i];
                            fd.rPtr[i] = (fd.ePtr[i] + oVal) * ftiLengthf;
                            fd.rPtr[i + halfLength] = (fd.ePtr[i] - oVal) * ftiLengthf;
                        }
                    });
                }
            }

            return result;
        }
    }
}
