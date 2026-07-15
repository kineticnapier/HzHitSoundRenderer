using UnityEngine;

namespace HzHitSoundRenderer.Audio
{
    internal sealed class HitSoundEvent
    {
        public string Name;
        public double Time;
        public float Volume;
        public AudioClip Clip;

        public HitSoundEvent(string name, double time, float volume, AudioClip clip)
        {
            Name = name;
            Time = time;
            Volume = volume;
            Clip = clip;
        }
    }
}
