using System;
using System.Drawing;
using PaintDotNet;
using PaintDotNet.Effects;
using PaintDotNet.IndirectUI;
using PaintDotNet.PropertySystem;
using PaintDotNet.Imaging;
using PaintDotNet.Rendering;

/*
 * Analogue Convert Effect for Paint.NET
 * Makes an image look like it was put on analogue TV
 * by simulating the actual signal generated.
 * Time for some nostalgia!
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
    public class PluginSupportInfo : IPluginSupportInfo
    {
        public string? DisplayName => "Analogue Convert";
        public string? Author => "Maxim Hoxha";
        public string? Copyright => "2022-2023 Maxim Hoxha";
        public Version? Version => new Version("1.1");
        public Uri? WebsiteUri => new Uri("https://github.com/maxotaku11niku/AnalogueConvertEffect");
    }

    [PluginSupportInfo(typeof(PluginSupportInfo))]
    public class AnalogueConvertEffect : PropertyBasedEffect
    {
        public enum PropertyNames
        {
            Format,
            Interlacing,
            MonitorGamma,
            BandwidthMult,
            Noise,
            PhaseNoise,
            ScanlineJitter,
            Crosstalk,
            Resonance,
            PhaseError,
            Ramp,
            Channels
        }
        string chosenFormat;
        bool interlace;
        double monitorGamma;
        double bandwidthMult;
        double noiseAmount;
        double phaseNoise;
        double jitter;
        double crosstalk;
        double resonance;
        double phaseError;
        double distortionRamp;
        bool doY;
        bool doU;
        bool doV;
        static Image iconImage;

        static AnalogueConvertEffect()
        {
            System.Resources.ResourceManager resm = new System.Resources.ResourceManager("AnalogueConvertEffect.resources", typeof(AnalogueConvertEffect).Assembly);
            iconImage = (Bitmap)resm.GetObject("icon");
        }

        public AnalogueConvertEffect() : base("Analogue Convert", iconImage, SubmenuNames.Stylize, new EffectOptions() { Flags = EffectFlags.Configurable | EffectFlags.ForceAliasedSelectionQuality, RenderingSchedule = EffectRenderingSchedule.None })
        {
        }

        protected override PropertyCollection OnCreatePropertyCollection()
        {
            List<Property> properties = new List<Property>();
            properties.Add(new StaticListChoiceProperty(PropertyNames.Format, new string[] {"PAL", "NTSC", "SECAM"}, 0));
            properties.Add(new BooleanProperty(PropertyNames.Interlacing, true));
            properties.Add(new DoubleProperty(PropertyNames.MonitorGamma, 2.5, 1.0, 4.0));
            properties.Add(new DoubleProperty(PropertyNames.BandwidthMult, 1.0, 0.5, 1.0));
            properties.Add(new DoubleProperty(PropertyNames.Noise, 0.0, 0.0, 1.0));
            properties.Add(new DoubleProperty(PropertyNames.PhaseNoise, 0.0, 0.0, 180.0));
            properties.Add(new DoubleProperty(PropertyNames.ScanlineJitter, 0.0, 0.0, 0.005));
            properties.Add(new DoubleProperty(PropertyNames.Crosstalk, 0.0, 0.0, 1.0));
            properties.Add(new DoubleProperty(PropertyNames.Resonance, 5.0, 1.0, 20.0));
            properties.Add(new DoubleProperty(PropertyNames.PhaseError, 0.0, -180.0, 180.0));
            properties.Add(new DoubleProperty(PropertyNames.Ramp, 0.0, 0.0, 10.0));
            properties.Add(new StaticListChoiceProperty(PropertyNames.Channels, new string[] {"YUV", "Y", "U", "V", "UV", "YU", "YV"}, 0));
            List<PropertyCollectionRule> propertyRules = new List<PropertyCollectionRule>();
            return new PropertyCollection(properties, propertyRules);
        }

        protected override ControlInfo OnCreateConfigUI(PropertyCollection props)
        {
            ControlInfo controlUI = PropertyControlInfo.CreateDefaultConfigUI(props);
            controlUI.SetPropertyControlValue(PropertyNames.Format, ControlInfoPropertyNames.DisplayName, "Format");
            controlUI.SetPropertyControlValue(PropertyNames.Interlacing, ControlInfoPropertyNames.DisplayName, "Do Interlacing?");
            controlUI.SetPropertyControlValue(PropertyNames.MonitorGamma, ControlInfoPropertyNames.DisplayName, "Your Monitor's Gamma");
            controlUI.SetPropertyControlValue(PropertyNames.BandwidthMult, ControlInfoPropertyNames.DisplayName, "Bandwidth Multiplier");
            controlUI.SetPropertyControlValue(PropertyNames.Noise, ControlInfoPropertyNames.DisplayName, "Noise Amount");
            controlUI.SetPropertyControlValue(PropertyNames.Noise, ControlInfoPropertyNames.DecimalPlaces, 3);
            controlUI.SetPropertyControlValue(PropertyNames.PhaseNoise, ControlInfoPropertyNames.DisplayName, "Phase Noise");
            controlUI.SetPropertyControlValue(PropertyNames.ScanlineJitter, ControlInfoPropertyNames.DisplayName, "Scanline Jitter");
            controlUI.SetPropertyControlValue(PropertyNames.ScanlineJitter, ControlInfoPropertyNames.DecimalPlaces, 5);
            controlUI.SetPropertyControlValue(PropertyNames.Crosstalk, ControlInfoPropertyNames.DisplayName, "Crosstalk");
            controlUI.SetPropertyControlValue(PropertyNames.Crosstalk, ControlInfoPropertyNames.DecimalPlaces, 3);
            controlUI.SetPropertyControlValue(PropertyNames.Resonance, ControlInfoPropertyNames.DisplayName, "Resonance");
            controlUI.SetPropertyControlValue(PropertyNames.PhaseError, ControlInfoPropertyNames.DisplayName, "Phase Error");
            controlUI.SetPropertyControlValue(PropertyNames.Ramp, ControlInfoPropertyNames.DisplayName, "Distortion Ramp");
            controlUI.SetPropertyControlValue(PropertyNames.Channels, ControlInfoPropertyNames.DisplayName, "Output Channels");
            return controlUI;
        }

        protected override void OnSetRenderInfo(PropertyBasedEffectConfigToken newToken, RenderArgs dstArgs, RenderArgs srcArgs)
        {
            chosenFormat = (string)newToken.GetProperty<StaticListChoiceProperty>(PropertyNames.Format).Value;
            interlace = newToken.GetProperty<BooleanProperty>(PropertyNames.Interlacing).Value;
            monitorGamma = newToken.GetProperty<DoubleProperty>(PropertyNames.MonitorGamma).Value;
            bandwidthMult = newToken.GetProperty<DoubleProperty>(PropertyNames.BandwidthMult).Value;
            noiseAmount = newToken.GetProperty<DoubleProperty>(PropertyNames.Noise).Value;
            phaseNoise = newToken.GetProperty<DoubleProperty>(PropertyNames.PhaseNoise).Value;
            jitter = newToken.GetProperty<DoubleProperty>(PropertyNames.ScanlineJitter).Value;
            crosstalk = newToken.GetProperty<DoubleProperty>(PropertyNames.Crosstalk).Value;
            resonance = newToken.GetProperty<DoubleProperty>(PropertyNames.Resonance).Value;
            phaseError = newToken.GetProperty<DoubleProperty>(PropertyNames.PhaseError).Value;
            distortionRamp = newToken.GetProperty<DoubleProperty>(PropertyNames.Ramp).Value;
            switch((string)newToken.GetProperty<StaticListChoiceProperty>(PropertyNames.Channels).Value)
            {
                case "YUV":
                    doY = true; doU = true; doV = true;
                    break;
                case "Y":
                    doY = true; doU = false; doV = false;
                    break;
                case "U":
                    doY = false; doU = true; doV = false;
                    break;
                case "V":
                    doY = false; doU = false; doV = true;
                    break;
                case "UV":
                    doY = false; doU = true; doV = true;
                    break;
                case "YU":
                    doY = true; doU = true; doV = false;
                    break;
                case "YV":
                    doY = true; doU = false; doV = true;
                    break;

            }

            base.OnSetRenderInfo(newToken, dstArgs, srcArgs);
        }

        private void DistortSignal(double[] signal, double sampFreq, double scFreq, int[] boundaryPoints) //Apply distortions to the signal in order to get that true analogue feeling
        {
            Random rng = new Random();
            for(int i = 0; i < signal.Length; i++)
            {
                signal[i] += (2.0 * rng.NextDouble() - 1.0) * noiseAmount;
            }
            double[] shiftSig = MathsUtil.ShiftArrayInterp(signal, (phaseError * sampFreq) / (360.0 * scFreq));
            double[] shiftPart;
            for (int i = 0; i < 69; i++) rng.NextDouble(); //advance the rng for no reason
            for (int i = 0; i < boundaryPoints.Length - 1; i++)
            {
                shiftPart = shiftSig[boundaryPoints[i]..boundaryPoints[i+1]];
                shiftPart = MathsUtil.ShiftArrayInterp(shiftPart, ((2.0 * rng.NextDouble() - 1.0) * phaseNoise * sampFreq) / (360.0 * scFreq));
                for(int j = 0; j < shiftPart.Length; j++)
                {
                    signal[j + boundaryPoints[i]] = shiftPart[j];
                }
            }
            if (distortionRamp != 0.0)
            {
                double norm = 1 / (2 * Math.Tanh(distortionRamp * 0.5));
                for (int i = 0; i < signal.Length; i++)
                {
                    signal[i] = 0.5 + Math.Tanh(distortionRamp * (signal[i] - 0.5)) * norm;
                }
            }
        }

        protected override void OnRender(Rectangle[] rois, int startIndex, int length)
        {
            AnalogueFormat format;
            int wrkWidth;
            switch (chosenFormat)
            {
                default: //Defaulting to PAL to piss off the Americans :)
                case "PAL":
                    format = new PALFormat();
                    wrkWidth = 1536;
                    break;
                case "NTSC":
                    format = new NTSCFormat();
                    wrkWidth = 1280;
                    break;
                case "SECAM":
                    format = new SECAMFormat();
                    wrkWidth = 1536;
                    break;
            }
            format.SetInterlace(interlace);
            Rectangle surrRect = rois[0];
            for (int i = 1; i < rois.Length; i++) //Determine bounding rectangle
            {
                if (rois[i].Left < surrRect.Left)
                {
                    surrRect.X = rois[i].Left;
                }

                if (rois[i].Top < surrRect.Top)
                {
                    surrRect.Y = rois[i].Top;
                }

                if (rois[i].Right > surrRect.Right)
                {
                    surrRect.Width = rois[i].Right - surrRect.X;
                }

                if (rois[i].Bottom > surrRect.Bottom)
                {
                    surrRect.Height = rois[i].Bottom - surrRect.Y;
                }
            }
            Surface inSrf = new Surface(surrRect.Size);
            inSrf.CopySurface(SrcArgs.Surface, surrRect);
            Surface wrkSrf = new Surface(wrkWidth, format.VideoScanlines);
            wrkSrf.FitSurface(ResamplingAlgorithm.AdaptiveHighQuality, inSrf);
            ImageData inIDat = new ImageData();
            inIDat.Width = wrkWidth;
            inIDat.Height = format.VideoScanlines;
            inIDat.Data = new byte[wrkWidth * format.VideoScanlines * 4];
            MemoryBlock wrkblk = wrkSrf.Scan0;
            for (int i = 0; i < inIDat.Data.Length; i++)
            {
                inIDat.Data[i] = wrkblk[i];
            }
            double[] signal = format.Encode(inIDat, monitorGamma);
            DistortSignal(signal, signal.Length * format.Framerate, format.SubcarrierFrequency, format.BoundaryPoints);
            ImageData outIDat = format.Decode(signal, wrkWidth, bandwidthMult, crosstalk, resonance, jitter, monitorGamma, (doY ? 0x1 : 0x0) | (doU ? 0x2 : 0x0) | (doV ? 0x4 : 0x0));
            Surface destSurf = new Surface(surrRect.Size);
            for (int i = 0; i < inIDat.Data.Length; i++)
            {
                wrkblk[i] = outIDat.Data[i];
            }
            destSurf.FitSurface(ResamplingAlgorithm.AdaptiveHighQuality, wrkSrf);
            if (length <= 0) return;
            for (int i = startIndex; i < startIndex + length; i++)
            {
                Render(destSurf, DstArgs.Surface, rois[i], surrRect.Location);
            }
        }

        private void Render(Surface src, Surface dst, Rectangle dstrectangle, Point rootPoint)
        {
            dst.CopySurface(src, dstrectangle);
        }
    }
}