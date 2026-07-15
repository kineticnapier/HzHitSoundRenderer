using System;
using System.Collections.Generic;
using UnityEngine;

namespace HzHitSoundRenderer.Audio
{
    internal sealed class ClipData
    {
        public AudioClip Clip;
        public float[] Data;
        public int Samples;
        public int Channels;
        public int Frequency;
        public float Length;
    }

    internal static class ClipDataCache
    {
        private static readonly Dictionary<int, ClipData> Cache = new Dictionary<int, ClipData>();

        public static void Clear()
        {
            Cache.Clear();
        }

        public static bool TryGet(AudioClip clip, out ClipData data)
        {
            data = null;
            if (clip == null)
            {
                return false;
            }

            int id = clip.GetInstanceID();
            if (Cache.TryGetValue(id, out data))
            {
                return true;
            }

            try
            {
                if (clip.loadState != AudioDataLoadState.Loaded)
                {
                    clip.LoadAudioData();
                }

                int channels = Math.Max(1, clip.channels);
                int samples = Math.Max(0, clip.samples);
                if (samples <= 0)
                {
                    return false;
                }

                float[] raw = new float[samples * channels];
                if (!clip.GetData(raw, 0))
                {
                    return false;
                }

                data = new ClipData
                {
                    Clip = clip,
                    Data = raw,
                    Samples = samples,
                    Channels = channels,
                    Frequency = Math.Max(1, clip.frequency),
                    Length = clip.length
                };

                Cache[id] = data;
                return true;
            }
            catch (Exception ex)
            {
                Main.Error("Could not read AudioClip data for " + clip.name + ": " + ex.Message);
                data = null;
                return false;
            }
        }
    }
}
