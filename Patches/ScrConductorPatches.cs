using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using HzHitSoundRenderer.Audio;

namespace HzHitSoundRenderer.Patches
{
    [HarmonyPatch(typeof(scrConductor), "StartMusic")]
    internal static class ScrConductorStartMusicPatch
    {
        private static void Prefix(scrConductor __instance)
        {
            Settings settings = Main.Settings;
            if (settings == null || !Main.Enabled || !settings.EnableRenderer)
            {
                return;
            }

            if (!settings.ReplaceVanillaHitSounds || settings.RenderMode == RenderMode.StreamingSegments)
            {
                return;
            }

            // This pre-render path is intentionally limited to editor playback.
            // It runs synchronously before StartMusicCo schedules the song, so the audio DSP cannot
            // advance the song while Unity is blocked rendering the hit-sound PCM.
            if (!ADOBase.isLevelEditor)
            {
                return;
            }

            ScrConductorPlayHitTimesPatch.PrepareBeforeMusic(__instance);
        }
    }

    [HarmonyPatch(typeof(scrConductor), "PlayHitTimes")]
    internal static class ScrConductorPlayHitTimesPatch
    {
        private static readonly FieldInfo HitSoundsDataField = AccessTools.Field(typeof(scrConductor), "hitSoundsData");
        private static readonly FieldInfo NextHitSoundToScheduleField = AccessTools.Field(typeof(scrConductor), "nextHitSoundToSchedule");
        private static readonly FieldInfo HoldSoundsDataField = AccessTools.Field(typeof(scrConductor), "holdSoundsData");
        private static readonly FieldInfo NextHoldSoundToScheduleField = AccessTools.Field(typeof(scrConductor), "nextHoldSoundToSchedule");
        private static readonly FieldInfo NextExtraTickToScheduleField = AccessTools.Field(typeof(scrConductor), "nextExtraTickToSchedule");
        private static readonly FieldInfo PlayedHitSoundsField = AccessTools.Field(typeof(scrConductor), "playedHitSounds");
        private static readonly FieldInfo DspTimeSongField = AccessTools.Field(typeof(scrConductor), "dspTimeSong");
        private static readonly FieldInfo HasSetMidspinHitSoundField = AccessTools.Field(typeof(scrConductor), "hasSetMidspinHitsound");

        private static readonly Type HitSoundsDataType = AccessTools.Inner(typeof(scrConductor), "HitSoundsData");
        private static readonly FieldInfo HitSoundField = HitSoundsDataType != null ? AccessTools.Field(HitSoundsDataType, "hitSound") : null;
        private static readonly FieldInfo TimeField = HitSoundsDataType != null ? AccessTools.Field(HitSoundsDataType, "time") : null;
        private static readonly FieldInfo VolumeField = HitSoundsDataType != null ? AccessTools.Field(HitSoundsDataType, "volume") : null;

        private static bool _syntheticCaptureInProgress;

        internal static void PrepareBeforeMusic(scrConductor instance)
        {
            if (instance == null || _syntheticCaptureInProgress)
            {
                return;
            }

            Settings settings = Main.Settings;
            if (settings == null || settings.RenderMode == RenderMode.StreamingSegments)
            {
                return;
            }

            if (HitSoundsDataField == null || PlayedHitSoundsField == null || DspTimeSongField == null)
            {
                Main.Error("Pre-render reflection failed: scrConductor fields were not found.");
                return;
            }

            object oldHitSoundsData = HitSoundsDataField.GetValue(instance);
            object oldHoldSoundsData = HoldSoundsDataField != null ? HoldSoundsDataField.GetValue(instance) : null;
            int oldNextHit = NextHitSoundToScheduleField != null ? Convert.ToInt32(NextHitSoundToScheduleField.GetValue(instance)) : 0;
            int oldNextHold = NextHoldSoundToScheduleField != null ? Convert.ToInt32(NextHoldSoundToScheduleField.GetValue(instance)) : 0;
            int oldNextExtra = NextExtraTickToScheduleField != null ? Convert.ToInt32(NextExtraTickToScheduleField.GetValue(instance)) : 0;
            bool oldPlayedHitSounds = Convert.ToBoolean(PlayedHitSoundsField.GetValue(instance));
            double oldDspTimeSong = Convert.ToDouble(DspTimeSongField.GetValue(instance));
            bool oldPlayCountdownHihats = instance.playCountdownHihats;
            bool oldPlayEndingCymbal = instance.playEndingCymbal;
            bool oldUseMidspinHitSound = instance.useMidspinHitSound;
            HitSound oldMidspinHitSound = instance.midspinHitSound;
            bool oldHasSetMidspinHitSound = HasSetMidspinHitSoundField != null && Convert.ToBoolean(HasSetMidspinHitSoundField.GetValue(instance));
            List<scrConductor.ExtraTickData> oldExtraTicks = instance.extraTicksCountdown != null
                ? new List<scrConductor.ExtraTickData>(instance.extraTicksCountdown)
                : new List<scrConductor.ExtraTickData>();

            try
            {
                _syntheticCaptureInProgress = true;

                // Build the same relative hit-sound timeline as the real PlayHitTimes call, but place it
                // far in the future so its "time > dspTime" filters cannot discard the beginning.
                // Countdown hats and the ending cymbal are disabled only for this synthetic capture;
                // the real PlayHitTimes call will schedule them normally after the song is scheduled.
                instance.playCountdownHihats = false;
                instance.playEndingCymbal = false;
                PlayedHitSoundsField.SetValue(instance, false);
                DspTimeSongField.SetValue(instance, Math.Max(instance.dspTime, AudioSettings.dspTime) + 3600.0);

                instance.PlayHitTimes();

                IList rawList = HitSoundsDataField.GetValue(instance) as IList;
                if (rawList == null || rawList.Count == 0)
                {
                    Main.Error("Pre-render capture produced no hitSounds. Vanilla hitSounds will be kept.");
                    return;
                }

                List<HitSoundEvent> events;
                if (!TryCaptureEvents(rawList, out events) || events.Count == 0)
                {
                    Main.Error("Pre-render capture could not read hitSound events. Vanilla hitSounds will be kept.");
                    return;
                }

                if (settings.WarmupClipsBeforeReplacing && !CanReadAllClips(events))
                {
                    Main.Error("Pre-render fallback: at least one AudioClip could not be read. Vanilla hitSounds will be kept.");
                    return;
                }

                if (!HighKpsHitSoundScheduler.TryPrepareTrack(events, instance.hitSoundGroup))
                {
                    Main.Error("Could not prepare the hitSound track before music scheduling. Vanilla hitSounds will be kept.");
                }
            }
            catch (Exception ex)
            {
                Main.Error("Pre-render before StartMusic failed:\n" + ex);
            }
            finally
            {
                DspTimeSongField.SetValue(instance, oldDspTimeSong);
                PlayedHitSoundsField.SetValue(instance, oldPlayedHitSounds);
                instance.playCountdownHihats = oldPlayCountdownHihats;
                instance.playEndingCymbal = oldPlayEndingCymbal;
                instance.useMidspinHitSound = oldUseMidspinHitSound;
                instance.midspinHitSound = oldMidspinHitSound;
                instance.extraTicksCountdown = oldExtraTicks;

                HitSoundsDataField.SetValue(instance, oldHitSoundsData);
                if (NextHitSoundToScheduleField != null)
                {
                    NextHitSoundToScheduleField.SetValue(instance, oldNextHit);
                }
                if (HoldSoundsDataField != null)
                {
                    HoldSoundsDataField.SetValue(instance, oldHoldSoundsData);
                }
                if (NextHoldSoundToScheduleField != null)
                {
                    NextHoldSoundToScheduleField.SetValue(instance, oldNextHold);
                }
                if (NextExtraTickToScheduleField != null)
                {
                    NextExtraTickToScheduleField.SetValue(instance, oldNextExtra);
                }
                if (HasSetMidspinHitSoundField != null)
                {
                    HasSetMidspinHitSoundField.SetValue(instance, oldHasSetMidspinHitSound);
                }

                _syntheticCaptureInProgress = false;
            }
        }

        private static void Postfix(scrConductor __instance)
        {
            if (_syntheticCaptureInProgress)
            {
                return;
            }

            try
            {
                Settings settings = Main.Settings;
                if (settings == null || !Main.Enabled || !settings.EnableRenderer)
                {
                    return;
                }

                // AudioSync's checkpoint handshake lets scrController.Scrub make one
                // provisional PlayHitTimes call, then rebuilds the timeline after the
                // music playhead is confirmed. Consuming a pre-rendered track during the
                // provisional call leaves nothing for the final timeline. Streaming mode
                // is already safe because it can rebuild its segments on every call.
                if (settings.RenderMode != RenderMode.StreamingSegments &&
                    AudioSyncCompat.IsCheckpointHandshakeActive())
                {
                    HighKpsHitSoundScheduler.SetWaitingForFinalTimeline();
                    return;
                }

                if (__instance == null || HitSoundsDataField == null || HitSoundField == null || TimeField == null || VolumeField == null)
                {
                    Main.Error("Reflection failed: scrConductor.HitSoundsData fields were not found.");
                    return;
                }

                IList rawList = HitSoundsDataField.GetValue(__instance) as IList;
                if (rawList == null || rawList.Count == 0)
                {
                    return;
                }

                List<HitSoundEvent> events;
                if (!TryCaptureEvents(rawList, out events))
                {
                    return;
                }

                if (events.Count == 0)
                {
                    return;
                }

                if (settings.WarmupClipsBeforeReplacing && !CanReadAllClips(events))
                {
                    Main.Error("Fallback to vanilla hitSounds because at least one AudioClip could not be read.");
                    return;
                }

                if (!HighKpsHitSoundScheduler.TryStartPlayback(events, __instance.hitSoundGroup))
                {
                    Main.Error("Could not start generated hitSound scheduler. Keeping vanilla hitSounds.");
                    return;
                }

                if (settings.ReplaceVanillaHitSounds)
                {
                    rawList.Clear();
                    if (NextHitSoundToScheduleField != null)
                    {
                        NextHitSoundToScheduleField.SetValue(__instance, 0);
                    }
                }
            }
            catch (Exception ex)
            {
                Main.Error("PlayHitTimes postfix failed:\n" + ex);
            }
        }

        private static bool TryCaptureEvents(IList rawList, out List<HitSoundEvent> events)
        {
            events = new List<HitSoundEvent>(rawList.Count);

            for (int i = 0; i < rawList.Count; i++)
            {
                object item = rawList[i];
                if (item == null)
                {
                    continue;
                }

                object hitSoundObj = HitSoundField.GetValue(item);
                double time = Convert.ToDouble(TimeField.GetValue(item));
                float volume = Convert.ToSingle(VolumeField.GetValue(item));

                if (hitSoundObj == null)
                {
                    continue;
                }

                string hitSoundName = hitSoundObj.ToString();
                if (string.Equals(hitSoundName, "None", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string clipName = "snd" + hitSoundName;
                AudioClip clip = null;
                try
                {
                    if (AudioManager.Instance != null)
                    {
                        clip = GameVersionCompat.FindOrLoadAudioClip(
                            AudioManager.Instance, clipName);
                    }
                }
                catch (Exception ex)
                {
                    Main.Error("Could not load " + clipName + ": " + ex.Message);
                    clip = null;
                }

                if (clip == null)
                {
                    Main.Error("AudioClip not found: " + clipName);
                    continue;
                }

                events.Add(new HitSoundEvent(clipName, time, volume, clip));
            }

            events.Sort(delegate (HitSoundEvent a, HitSoundEvent b)
            {
                return a.Time.CompareTo(b.Time);
            });

            return true;
        }

        private static bool CanReadAllClips(List<HitSoundEvent> events)
        {
            HashSet<int> checkedIds = new HashSet<int>();
            for (int i = 0; i < events.Count; i++)
            {
                AudioClip clip = events[i].Clip;
                if (clip == null)
                {
                    return false;
                }

                int id = clip.GetInstanceID();
                if (checkedIds.Contains(id))
                {
                    continue;
                }
                checkedIds.Add(id);

                ClipData data;
                if (!ClipDataCache.TryGet(clip, out data))
                {
                    return false;
                }
            }
            return true;
        }
    }
}
