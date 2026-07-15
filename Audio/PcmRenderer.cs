using System;
using System.Collections.Generic;
using UnityEngine;

namespace HzHitSoundRenderer.Audio
{
    internal static class PcmRenderer
    {
        public static AudioClip RenderSegment(
            string name,
            double segmentStart,
            double segmentEnd,
            List<HitSoundEvent> events,
            Settings settings,
            out float peakBeforeLimit,
            out float limiterScale)
        {
            peakBeforeLimit = 0f;
            limiterScale = 1f;

            int sampleRate = Math.Max(8000, settings.RenderSampleRate);
            int channels = Math.Max(1, Math.Min(2, settings.OutputChannels));
            double duration = Math.Max(0.01, segmentEnd - segmentStart + Math.Max(0.0, settings.ClipTailSeconds));
            int totalSamples = Math.Max(1, (int)Math.Ceiling(duration * sampleRate));
            float[] output = new float[totalSamples * channels];

            if (events.Count > 0)
            {
                // Raw original PCM mode:
                // Do not apply density gain, limiter, soft clip, output boost, synthesis, or any other tone processing.
                // Each hit copies the original AudioClip PCM into the rendered buffer with ADOFAI hit volume.
                float rawGain = Math.Max(0f, Math.Min(1f, settings.MasterVolume));
                for (int i = 0; i < events.Count; i++)
                {
                    AddEvent(output, channels, sampleRate, segmentStart, events[i], settings, rawGain);
                }
            }

            peakBeforeLimit = MeasurePeak(output);
            limiterScale = 1f;

            AudioClip clip = AudioClip.Create(name, totalSamples, channels, sampleRate, false);
            clip.SetData(output, 0);
            return clip;
        }

        private static double[] BuildDensityGains(List<HitSoundEvent> events, Settings settings)
        {
            double[] gains = new double[events.Count];
            for (int i = 0; i < gains.Length; i++)
            {
                gains[i] = 1.0;
            }

            if (settings.DensityGainMode == DensityGainMode.None || events.Count <= 1)
            {
                return gains;
            }

            double window = Math.Max(0.001, settings.DensityWindowSeconds);
            double half = window * 0.5;
            double referenceKps = Math.Max(1.0, Math.Min(3000.0, settings.ReferenceKpsForFullVolume));

            int left = 0;
            int right = 0;
            for (int i = 0; i < events.Count; i++)
            {
                double t = events[i].Time;
                while (left < events.Count && events[left].Time < t - half)
                {
                    left++;
                }
                while (right < events.Count && events[right].Time <= t + half)
                {
                    right++;
                }

                int count = Math.Max(1, right - left);
                double kps = count / window;
                if (kps <= referenceKps)
                {
                    gains[i] = 1.0;
                }
                else if (settings.DensityGainMode == DensityGainMode.Sqrt)
                {
                    gains[i] = Math.Sqrt(referenceKps / kps);
                }
                else
                {
                    gains[i] = referenceKps / kps;
                }
            }

            return gains;
        }

        private static void AddEvent(float[] output, int outChannels, int outSampleRate, double segmentStart, HitSoundEvent hit, Settings settings, float gain)
        {
            ClipData clipData;
            if (!ClipDataCache.TryGet(hit.Clip, out clipData))
            {
                return;
            }

            int startSample = (int)Math.Round((hit.Time - segmentStart) * outSampleRate);
            if (startSample >= output.Length / outChannels)
            {
                return;
            }

            // Raw original PCM mode.
            // We only copy the original hit sound samples into the output buffer.
            // MaxSourceClipSeconds only decides how much of the original clip is copied;
            // it does not synthesize, distort, compress, fade, or recolor the sound.
            int sourceSamples = clipData.Samples;
            double sourceSeconds = Math.Max(0.0, settings.MaxSourceClipSeconds);
            if (sourceSeconds > 0.0)
            {
                sourceSamples = Math.Min(sourceSamples, Math.Max(1, (int)Math.Ceiling(sourceSeconds * clipData.Frequency)));
            }

            int maxOutSamples = output.Length / outChannels;
            int copySamples = Math.Min(maxOutSamples - Math.Max(0, startSample), (int)Math.Ceiling((double)sourceSamples * outSampleRate / clipData.Frequency));
            if (copySamples <= 0)
            {
                return;
            }

            for (int outSample = 0; outSample < copySamples; outSample++)
            {
                int dstSample = startSample + outSample;
                if (dstSample < 0)
                {
                    continue;
                }

                double srcPosition = (double)outSample * clipData.Frequency / outSampleRate;
                int srcSample0 = (int)srcPosition;
                if (srcSample0 < 0 || srcSample0 >= sourceSamples)
                {
                    continue;
                }
                int srcSample1 = Math.Min(sourceSamples - 1, srcSample0 + 1);
                float frac = (float)(srcPosition - srcSample0);

                for (int ch = 0; ch < outChannels; ch++)
                {
                    int srcCh = Math.Min(ch, clipData.Channels - 1);
                    float a = clipData.Data[srcSample0 * clipData.Channels + srcCh];
                    float b = clipData.Data[srcSample1 * clipData.Channels + srcCh];
                    float sample = (a + (b - a) * frac) * hit.Volume * gain;
                    output[dstSample * outChannels + ch] += sample;
                }
            }
        }

        private static float MeasurePeak(float[] data)
        {
            float peak = 0f;
            for (int i = 0; i < data.Length; i++)
            {
                float v = Math.Abs(data[i]);
                if (v > peak)
                {
                    peak = v;
                }
            }
            return peak;
        }

        private static void Multiply(float[] data, float scale)
        {
            for (int i = 0; i < data.Length; i++)
            {
                data[i] *= scale;
            }
        }

        private static void SoftClip(float[] data, float drive)
        {
            drive = Math.Max(0.1f, drive);
            double denom = Math.Tanh(drive);
            if (denom <= 0.0)
            {
                return;
            }

            for (int i = 0; i < data.Length; i++)
            {
                data[i] = (float)(Math.Tanh(data[i] * drive) / denom);
            }
        }
    }
}
