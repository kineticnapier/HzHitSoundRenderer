using System;
using UnityModManagerNet;

namespace HzHitSoundRenderer
{
    public enum DensityGainMode
    {
        None = 0,
        Sqrt = 1,
        Linear = 2
    }

    public enum RenderMode
    {
        StreamingSegments = 0,
        PreRenderSegments = 1,
        SingleClip = 2
    }

    [Serializable]
    public class Settings : UnityModManager.ModSettings
    {
        public bool EnableRenderer = false;
        public bool ReplaceVanillaHitSounds = true;
        public bool WarmupClipsBeforeReplacing = true;
        public bool ShowAdvancedSettings = false;

        // StreamingSegments = current low-latency mode.
        // PreRenderSegments = render all segments first, then schedule them. Prevents late segments.
        // SingleClip = render the whole captured hitSound track into one AudioClip. Best for removing segment clicks.
        public RenderMode RenderMode = RenderMode.StreamingSegments;
        public double LateScheduleMarginSeconds = 0.05;
        // If rendering finishes after the intended hitSound start time, skip the already-missed part instead of delaying the whole rendered audio.
        public bool SkipLateAudioInsteadOfDelaying = true;

        // Performance defaults are tuned for Hz chart listening.
        // Old saved settings will override these; use a preset button in UMM if needed.
        public double SegmentSeconds = 0.5;
        public double PreRenderAheadSeconds = 12.0;
        public double ClipTailSeconds = 0.03;
        public int RenderSampleRate = 48000;
        public int OutputChannels = 2;
        public int RenderSegmentsPerFrame = 1;

        // Copy the original hit sound PCM.
        // 0 or negative = use the whole source clip. Positive values trim the original clip for performance/clarity.
        // This is not a synthesized Hz core; the copied audio is the original hit sound.
        public double MaxSourceClipSeconds = 0.0;
        public double SourceFadeOutSeconds = 0.0;

        // Obsolete v0.5 tone-shaping fields. Kept only so old settings files still load safely. Ignored by the renderer.
        public double OriginalBodyClipSeconds = 0.0;
        public float OriginalBodyVolume = 0.0f;

        // Raw original PCM mode: no density gain, limiter, soft clip, output boost, or tone shaping.
        // MasterVolume is only a simple linear multiplier for the original hit sound, capped to 1.0 in the UI.
        public float MasterVolume = 1.0f;
        public float OutputGain = 1.0f;
        public DensityGainMode DensityGainMode = DensityGainMode.None;
        public double DensityWindowSeconds = 0.05;
        public double ReferenceKpsForFullVolume = 1000000.0;

        public bool UseLimiter = false;
        public float MaxOutputPeak = 1.0f;
        public bool UseSoftClip = false;
        public float SoftClipDrive = 1.0f;

        public bool LogSummary = true;
        public bool LogSegments = false;

        public string SegmentSecondsText = "0.5";
        public string PreRenderAheadSecondsText = "12.0";
        public string ClipTailSecondsText = "0.03";
        public string RenderSampleRateText = "48000";
        public string RenderSegmentsPerFrameText = "1";
        public string LateScheduleMarginSecondsText = "0.05";
        public string MaxSourceClipSecondsText = "0";
        public string OriginalBodyClipSecondsText = "0";
        public string OriginalBodyVolumeText = "0";
        public string SourceFadeOutSecondsText = "0";
        public string MasterVolumeText = "1.0";
        public string OutputGainText = "1.0";
        public string DensityWindowSecondsText = "0.05";
        public string ReferenceKpsForFullVolumeText = "1000000";
        public string MaxOutputPeakText = "1.0";
        public string SoftClipDriveText = "1.0";

        public override void Save(UnityModManager.ModEntry modEntry)
        {
            Save(this, modEntry);
        }
    }
}
