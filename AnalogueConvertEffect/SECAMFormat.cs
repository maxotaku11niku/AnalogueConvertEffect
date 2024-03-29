﻿using System;
using System.Numerics;

/* 
 * Implementation of the SECAM format
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
    //The SECAM format, used in France etc.
    public class SECAMFormat : AnalogueFormat
    {
        const double SECAMGamma = 2.8;

        public SECAMFormat() : base(0.299, //R to Y
                                   0.587, //G to Y
                                   0.114, //B to Y
                                   1.333, //Db maximum
                                   -1.333, //Dr maximum
                                   0.0, //Chroma conversion phase relative to YUV (YDbDr is just YUV but scaled differently)
                                   5e+6, //Main bandwidth
                                   0.75e+6, //Side bandwidth
                                   1.3e+6, //Colour bandwidth lower part
                                   0.57e+6, //Colour bandwidth upper part
                                   4328125, //Colour subcarrier frequency
                                   625, //Total scanlines
                                   576, //Visible scanlines
                                   50.0, //Nominal framerate
                                   5.195e-5, //Active video time
                                   true) //Interlaced?
        { }

        private readonly double[] SubCarrierFrequencies = { 4250000,   //Db
                                                            4406250 }; //Dr
        private readonly double[] SubCarrierAngFrequencies = { 4250000 * 2.0 * Math.PI,   //Db
                                                               4406250 * 2.0 * Math.PI }; //Dr
        private readonly double[] AngFrequencyShifts = { 230000 * 2.0 * Math.PI,   //Db
                                                         280000 * 2.0 * Math.PI }; //Dr
        private readonly double[] SubCarrierLowerFrequencies = { 2.0 * 506000,   //Db
                                                                 2.0 * 350000 }; //Dr
        private readonly double[] SubCarrierUpperFrequencies = { 2.0 * 350000,   //Db
                                                                 2.0 * 506000 }; //Dr
        private readonly double SubCarrierStartTime = 0.4e-6;

        public override ImageData Decode(double[] signal, int activeWidth, double bwMult = 1.0, double crosstalk = 0.0, double phError = 0.0, double phNoise = 0.0, double resonance = 1.0, double scanlineJitter = 0.0, int channelFlags = 0x7)
        {
            int[] activeSignalStarts = new int[videoScanlines]; //Start points of the active parts
            byte R = 0;
            byte G = 0;
            byte B = 0;
            double Y = 0.0;
            double Db = 0.0;
            double Dr = 0.0;
            int polarity = 0;
            int pos = 0;
            int DbPos = 0;
            int DrPos = 0;
            int componentAlternate = 0; //SECAM alternates between Db and Dr with each scanline
            double sigNum = 0.0;
            double freqPoint = 0.0;
            double sampleRate = activeWidth / realActiveTime; //Correction for the fact that the signal we've created only has active scanlines.

            double blendStr = 1.0 - crosstalk;
            bool inclY = ((channelFlags & 0x1) == 0) ? false : true;
            bool inclDb = ((channelFlags & 0x2) == 0) ? false : true;
            bool inclDr = ((channelFlags & 0x4) == 0) ? false : true;

            for (int i = 0; i < videoScanlines; i++) //Where the active signal starts
            {
                activeSignalStarts[i] = (int)((((double)i * (double)signal.Length) / (double)videoScanlines) + ((scanlineTime - realActiveTime) / (2 * realActiveTime)) * activeWidth);
            }

            double sampleTime = realActiveTime / (double)activeWidth;
            FIRFilter mainfir = MathsUtil.MakeFIRFilter(sampleRate, 256, ((mainBandwidth - sideBandwidth) / 2.0) * bwMult, (mainBandwidth + sideBandwidth) * bwMult, resonance);
            FIRFilter dbfir = MathsUtil.MakeFIRFilter(sampleRate, 256, ((chromaBandwidthUpper - chromaBandwidthLower) / 2.0) * bwMult, (chromaBandwidthLower + chromaBandwidthUpper) * bwMult, resonance);
            FIRFilter drfir = MathsUtil.MakeFIRFilter(sampleRate, 256, ((chromaBandwidthUpper - chromaBandwidthLower) / 2.0) * bwMult, (chromaBandwidthLower + chromaBandwidthUpper) * bwMult, resonance);
            FIRFilter colfir = MathsUtil.MakeFIRFilter(sampleRate, 256, ((chromaBandwidthUpper - chromaBandwidthLower) / 2.0) * bwMult, (chromaBandwidthLower + chromaBandwidthUpper) * bwMult, resonance);
            FIRFilter notchfir = new FIRFilter(colfir.forwardLen, colfir.backport);
            for (int i = -notchfir.backport; i < notchfir.forwardLen; i++)
            {
                notchfir[i] = -colfir[i];
            }
            notchfir[0] = 1.0 - colfir[0];
            double[] DbSignalPre = MathsUtil.FIRFilterCrosstalkShift(signal, dbfir, crosstalk, sampleTime, SubCarrierAngFrequencies[0]);
            double[] DrSignalPre = MathsUtil.FIRFilterCrosstalkShift(signal, drfir, crosstalk, sampleTime, SubCarrierAngFrequencies[1]);
            signal = MathsUtil.FIRFilter(signal, mainfir);
            double[] DbSignal = new double[DbSignalPre.Length];
            double[] DrSignal = new double[DrSignalPre.Length];
            int combdelay = 1;
            for(int i = 0; i < combdelay; i++)
            {
                DbSignal[i] = DbSignalPre[i];
                DrSignal[i] = DrSignalPre[i];
            }
            for (int i = combdelay; i < signal.Length; i++)
            {
                DbSignal[i] = 0.5 * (DbSignalPre[i] - DbSignalPre[i - combdelay]);
                DrSignal[i] = 0.5 * (DrSignalPre[i] - DrSignalPre[i - combdelay]);
            }

            double DbDecodeAngFreq = SubCarrierAngFrequencies[0];
            double DrDecodeAngFreq = SubCarrierAngFrequencies[1];
            double DbDecodePhase = 0.0;
            double DrDecodePhase = 0.0;
            double DbDeriv = 0.0;
            double DrDeriv = 0.0;
            double DbLast = 0.0;
            double DrLast = 0.0;
            double curDb = 0.0;
            double curDr = 0.0;
            double DbFreqShift = 0.0;
            double DrFreqShift = 0.0;
            double DbLastFreqShift = 0.0;
            double DrLastFreqShift = 0.0;
            for (int i = 0; i < signal.Length; i++)
            {
                DbDeriv = DbSignal[i] - DbLast;
                DrDeriv = DrSignal[i] - DrLast;
                DbLast = DbSignal[i];
                DrLast = DrSignal[i];
                curDb = (DbDecodeAngFreq - SubCarrierAngFrequencies[0]) / AngFrequencyShifts[0];
                curDr = (DrDecodeAngFreq - SubCarrierAngFrequencies[1]) / AngFrequencyShifts[1];
                DbSignal[i] = curDb;
                DrSignal[i] = curDr;
                DbFreqShift = -(Math.Cos(DbDecodePhase) * DbDeriv) - (DbDecodeAngFreq * Math.Sin(DbDecodePhase) * DbLast);
                DrFreqShift = -(Math.Cos(DrDecodePhase) * DrDeriv) - (DrDecodeAngFreq * Math.Sin(DrDecodePhase) * DrLast);
                DbDecodeAngFreq += 5.0 * (DbFreqShift - DbLastFreqShift);
                DrDecodeAngFreq += 5.0 * (DrFreqShift - DrLastFreqShift);
                DbDecodePhase += sampleTime * DbDecodeAngFreq;
                DrDecodePhase += sampleTime * DrDecodeAngFreq;
                DbLastFreqShift = DbFreqShift;
                DrLastFreqShift = DrFreqShift;
            }


            signal = MathsUtil.FIRFilterCrosstalkShift(signal, notchfir, crosstalk, sampleTime, carrierAngFreq);
            DbSignal = MathsUtil.FIRFilter(DbSignal, dbfir);
            DrSignal = MathsUtil.FIRFilter(DrSignal, drfir);

            ImageData writeToSurface = new ImageData();
            writeToSurface.Width = activeWidth;
            writeToSurface.Height = videoScanlines;
            writeToSurface.Data = new byte[activeWidth * videoScanlines * 4];

            byte[] surfaceColours = writeToSurface.Data;
            int currentScanline;
            Random rng = new Random();
            int curjit = 0;
            double dR = 0.0;
            double dG = 0.0;
            double dB = 0.0;
            for (int i = 0; i < videoScanlines; i++)
            {
                if (i * 2 >= videoScanlines) //Simulate interlacing
                {
                    polarity = 1;
                }
                currentScanline = isInterlaced ? (i * 2 + polarity) % videoScanlines : i;
                if ((i % 2) == 1) //Do colour component alternation
                {
                    componentAlternate = 1;
                }
                else componentAlternate = 0;

                curjit = (int)(scanlineJitter * 2.0 * (rng.NextDouble() - 0.5) * activeWidth);
                pos = activeSignalStarts[i] + curjit;
                DbPos = activeSignalStarts[componentAlternate == 0 ? i : (i - 1)] + curjit;

                if (i <= 0 || i * 2 == videoScanlines)
                {
                    for (int j = 0; j < writeToSurface.Width; j++) //Decode active signal region only
                    {
                        Y = inclY ? signal[pos] : 0.5;
                        Db = inclDb ? DbSignal[DbPos] : 0.0;
                        Dr = 0.0;
                        dR = Math.Pow(YUVtoRGBConversionMatrix[0] * Y + YUVtoRGBConversionMatrix[2] * Dr, SECAMGamma);
                        dG = Math.Pow(YUVtoRGBConversionMatrix[3] * Y + YUVtoRGBConversionMatrix[4] * Db + YUVtoRGBConversionMatrix[5] * Dr, SECAMGamma);
                        dB = Math.Pow(YUVtoRGBConversionMatrix[6] * Y + YUVtoRGBConversionMatrix[7] * Db, SECAMGamma);
                        R = (byte)(MathsUtil.Clamp(MathsUtil.SRGBInverseGammaTransform(dR), 0.0, 1.0) * 255.0);
                        G = (byte)(MathsUtil.Clamp(MathsUtil.SRGBInverseGammaTransform(dG), 0.0, 1.0) * 255.0);
                        B = (byte)(MathsUtil.Clamp(MathsUtil.SRGBInverseGammaTransform(dB), 0.0, 1.0) * 255.0);
                        surfaceColours[(currentScanline * writeToSurface.Width + j) * 4 + 3] = 255;
                        surfaceColours[(currentScanline * writeToSurface.Width + j) * 4 + 2] = R;
                        surfaceColours[(currentScanline * writeToSurface.Width + j) * 4 + 1] = G;
                        surfaceColours[(currentScanline * writeToSurface.Width + j) * 4] = B;
                        pos++;
                        DbPos++;
                    }
                }
                else
                {
                    DrPos = activeSignalStarts[componentAlternate == 0 ? (i - 1) : i] + curjit;
                    for (int j = 0; j < writeToSurface.Width; j++) //Decode active signal region only
                    {
                        Y = inclY ? signal[pos] : 0.5;
                        Db = inclDb ? DbSignal[DbPos] : 0.0;
                        Dr = inclDr ? DrSignal[DrPos] : 0.0;
                        dR = Math.Pow(YUVtoRGBConversionMatrix[0] * Y + YUVtoRGBConversionMatrix[2] * Dr, SECAMGamma);
                        dG = Math.Pow(YUVtoRGBConversionMatrix[3] * Y + YUVtoRGBConversionMatrix[4] * Db + YUVtoRGBConversionMatrix[5] * Dr, SECAMGamma);
                        dB = Math.Pow(YUVtoRGBConversionMatrix[6] * Y + YUVtoRGBConversionMatrix[7] * Db, SECAMGamma);
                        R = (byte)(MathsUtil.Clamp(MathsUtil.SRGBInverseGammaTransform(dR), 0.0, 1.0) * 255.0);
                        G = (byte)(MathsUtil.Clamp(MathsUtil.SRGBInverseGammaTransform(dG), 0.0, 1.0) * 255.0);
                        B = (byte)(MathsUtil.Clamp(MathsUtil.SRGBInverseGammaTransform(dB), 0.0, 1.0) * 255.0);
                        surfaceColours[(currentScanline * writeToSurface.Width + j) * 4 + 3] = 255;
                        surfaceColours[(currentScanline * writeToSurface.Width + j) * 4 + 2] = R;
                        surfaceColours[(currentScanline * writeToSurface.Width + j) * 4 + 1] = G;
                        surfaceColours[(currentScanline * writeToSurface.Width + j) * 4] = B;
                        pos++;
                        DbPos++;
                        DrPos++;
                    }
                }
            }
            return writeToSurface;
        }

        public override double[] Encode(ImageData surface)
        {
            int signalLen = (int)(surface.Width * videoScanlines * (scanlineTime / realActiveTime)); //To get a good analogue feel, we must limit the vertical resolution; the horizontal resolution will be limited as we decode the distorted signal.
            int[] boundaryPoints = new int[videoScanlines + 1]; //Boundaries of the scanline signals
            int[] activeSignalStarts = new int[videoScanlines]; //Start points of the active parts
            double[] signalOut = new double[signalLen];
            double R = 0.0;
            double G = 0.0;
            double B = 0.0;
            double Db = 0.0;
            double Dr = 0.0;
            double time = 0;
            int pos = 0;
            int polarity = 0;
            int componentAlternate = 0; //SECAM alternates between Db and Dr with each scanline
            int remainingSync = 0;
            double sampleTime = realActiveTime / (double)surface.Width;

            boundaryPoints[0] = 0; //Beginning of the signal
            boundaryPoints[videoScanlines] = signalLen; //End of the signal
            for (int i = 1; i < videoScanlines; i++) //Rest of the signal
            {
                boundaryPoints[i] = (i * signalLen) / videoScanlines;
            }

            boundPoints = boundaryPoints;

            for (int i = 0; i < videoScanlines; i++) //Where the active signal starts
            {
                activeSignalStarts[i] = (int)((((double)i * (double)signalLen) / (double)videoScanlines) + ((scanlineTime - realActiveTime) / (2 * realActiveTime)) * surface.Width) - boundaryPoints[i];
            }

            double instantPhase = 0.0;
            byte[] surfaceColours = surface.Data;
            int currentScanline;
            int subcarrierstartind = (int)((SubCarrierStartTime/realActiveTime) * ((double)surface.Width));
            for (int i = 0; i < videoScanlines; i++) //Only generate active scanlines
            {
                instantPhase = 0.0;
                if (i * 2 >= videoScanlines) //Simulate interlacing
                {
                    polarity = 1;
                }
                currentScanline = isInterlaced ? (i * 2 + polarity) % videoScanlines : i;
                if ((i % 2) == 1) //Do colour component alternation
                {
                    componentAlternate = 1;
                }
                else componentAlternate = 0;
                for (int j = 0; j < subcarrierstartind; j++) //Front porch, ignore sync signal because we don't see its results
                {
                    signalOut[pos] = 0.0;
                    pos++;
                    time = pos * sampleTime;
                }
                for (int j = subcarrierstartind; j < activeSignalStarts[i]; j++) //Front porch, ignore sync signal because we don't see its results
                {
                    instantPhase += sampleTime * SubCarrierAngFrequencies[componentAlternate];
                    signalOut[pos] = 0.115 * Math.Cos(instantPhase); //Add chroma lead-in via FM
                    pos++;
                    time = pos * sampleTime;
                }
                for (int j = 0; j < surface.Width; j++) //Active signal
                {
                    signalOut[pos] = 0.0;
                    R = surfaceColours[(currentScanline * surface.Width + j) * 4 + 2] / 255.0;
                    G = surfaceColours[(currentScanline * surface.Width + j) * 4 + 1] / 255.0;
                    B = surfaceColours[(currentScanline * surface.Width + j) * 4] / 255.0;
                    R = MathsUtil.SRGBGammaTransform(R);
                    G = MathsUtil.SRGBGammaTransform(G);
                    B = MathsUtil.SRGBGammaTransform(B);
                    R = Math.Pow(R, 1.0 / SECAMGamma); //Gamma correction
                    G = Math.Pow(G, 1.0 / SECAMGamma);
                    B = Math.Pow(B, 1.0 / SECAMGamma);
                    Db = RGBtoYUVConversionMatrix[3] * R + RGBtoYUVConversionMatrix[4] * G + RGBtoYUVConversionMatrix[5] * B; //Encode Db and Dr
                    Dr = RGBtoYUVConversionMatrix[6] * R + RGBtoYUVConversionMatrix[7] * G + RGBtoYUVConversionMatrix[8] * B;
                    instantPhase += sampleTime * (SubCarrierAngFrequencies[componentAlternate] + AngFrequencyShifts[componentAlternate] * (componentAlternate == 0 ? Db : Dr));
                    signalOut[pos] += RGBtoYUVConversionMatrix[0] * R + RGBtoYUVConversionMatrix[1] * G + RGBtoYUVConversionMatrix[2] * B; //Add luma straightforwardly
                    signalOut[pos] += 0.115 * Math.Cos(instantPhase); //Add chroma via FM
                    pos++;
                    time = pos * sampleTime;
                }
                while (pos < boundaryPoints[i + 1]) //Back porch, ignore sync signal because we don't see its results
                {
                    signalOut[pos] = 0.0;
                    pos++;
                    time = pos * sampleTime;
                }
            }
            return signalOut;
        }
    }
}
