using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace HzHitSoundRenderer
{
    /// <summary>
    /// Handles game API signatures that changed between ADOFAI 2.9.x and 3.3.x.
    /// </summary>
    internal static class GameVersionCompat
    {
        private static readonly MethodInfo FindAudioClipMethod =
            AccessTools.Method(typeof(AudioManager), "FindOrLoadAudioClip",
                new[] { typeof(string), typeof(string), typeof(bool) }) ??
            AccessTools.Method(typeof(AudioManager), "FindOrLoadAudioClip",
                new[] { typeof(string), typeof(string) });

        internal static AudioClip FindOrLoadAudioClip(AudioManager manager, string clipName)
        {
            if (manager == null || FindAudioClipMethod == null)
            {
                return null;
            }

            object[] arguments = FindAudioClipMethod.GetParameters().Length == 3
                ? new object[] { clipName, null, false }
                : new object[] { clipName, null };
            return FindAudioClipMethod.Invoke(manager, arguments) as AudioClip;
        }
    }
}
