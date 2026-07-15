using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.SceneManagement;

namespace HzHitSoundRenderer.Audio
{
    internal sealed class HighKpsHitSoundScheduler : MonoBehaviour
    {
        private sealed class SegmentJob
        {
            public double Start;
            public double End;
            public List<HitSoundEvent> Events = new List<HitSoundEvent>();
            public bool Rendered;
        }

        private sealed class RenderedSegment
        {
            public SegmentJob Job;
            public AudioClip Clip;
            public GameObject GameObject;
            public AudioSource Source;
            public float Peak;
            public float Scale;
        }

        private sealed class PreparedSegment
        {
            public double RelativeStart;
            public AudioClip Clip;
            public float Peak;
            public float Scale;
        }

        private sealed class PreparedTrack
        {
            public RenderMode Mode;
            public long Signature;
            public int EventCount;
            public double Duration;
            public double AvgKps;
            public double RenderMs;
            public AudioClip WholeClip;
            public readonly List<PreparedSegment> Segments = new List<PreparedSegment>();
        }

        private static HighKpsHitSoundScheduler _instance;
        private static PreparedTrack _preparedTrack;
        private static readonly List<UnityEngine.Object> GeneratedObjects = new List<UnityEngine.Object>();

        private readonly List<SegmentJob> _segments = new List<SegmentJob>();
        private AudioMixerGroup _mixerGroup;
        private int _nextSegmentIndex;
        private int _playbackId;
        private int _totalEvents;
        private double _firstTime;
        private double _lastTime;
        private int _playbackSceneHandle = -1;

        public static string StatusText = "No generated hitSound playback.";

        public static void EnsureExists()
        {
            if (_instance != null)
            {
                return;
            }

            GameObject go = new GameObject("HzHitSoundRenderer Scheduler");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<HighKpsHitSoundScheduler>();
        }

        private void OnEnable()
        {
            SceneManager.sceneUnloaded += OnSceneUnloaded;
        }

        private void OnDisable()
        {
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
        }

        private void OnDestroy()
        {
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            if (_instance == this)
            {
                _instance = null;
            }
        }

        private void OnApplicationQuit()
        {
            StopAllGeneratedAudio("application quit");
        }

        private void OnSceneUnloaded(Scene scene)
        {
            if (_playbackSceneHandle >= 0 && scene.handle == _playbackSceneHandle)
            {
                StopAllGeneratedAudio("playback scene unloaded");
            }
        }

        public static void DestroyInstance()
        {
            StopAllGeneratedAudio();
            if (_instance != null)
            {
                Destroy(_instance.gameObject);
                _instance = null;
            }
        }

        public static void StopAllGeneratedAudio()
        {
            StopAllGeneratedAudio("manual stop");
        }

        internal static void StopAllGeneratedAudio(string reason)
        {
            StopGeneratedAudioOnly();
            DiscardPreparedTrack();
            StatusText = "Generated hitSound audio stopped" +
                (string.IsNullOrEmpty(reason) ? "." : " (" + reason + ").");
        }

        private static void StopGeneratedAudioOnly()
        {
            // Destroy() is deferred until the end of the frame. Stop and deactivate every
            // generated AudioSource first so a previously scheduled clip cannot begin after
            // game over or while another scene is loading.
            for (int i = 0; i < GeneratedObjects.Count; i++)
            {
                UnityEngine.Object generated = GeneratedObjects[i];
                if (generated == null)
                {
                    continue;
                }

                GameObject go = generated as GameObject;
                if (go != null)
                {
                    AudioSource[] sources = go.GetComponents<AudioSource>();
                    for (int j = 0; j < sources.Length; j++)
                    {
                        AudioSource source = sources[j];
                        if (source != null)
                        {
                            source.Stop();
                            source.clip = null;
                        }
                    }

                    go.SetActive(false);
                    Destroy(go);
                    continue;
                }

                Destroy(generated);
            }
            GeneratedObjects.Clear();

            if (_instance != null)
            {
                _instance._segments.Clear();
                _instance._nextSegmentIndex = 0;
                _instance._totalEvents = 0;
                _instance._playbackSceneHandle = -1;
                _instance._playbackId++;
            }
        }

        private static void DiscardPreparedTrack()
        {
            if (_preparedTrack == null)
            {
                return;
            }

            if (_preparedTrack.WholeClip != null)
            {
                Destroy(_preparedTrack.WholeClip);
                _preparedTrack.WholeClip = null;
            }

            for (int i = 0; i < _preparedTrack.Segments.Count; i++)
            {
                AudioClip clip = _preparedTrack.Segments[i].Clip;
                if (clip != null)
                {
                    Destroy(clip);
                }
            }
            _preparedTrack.Segments.Clear();
            _preparedTrack = null;
        }

        public static bool TryPrepareTrack(List<HitSoundEvent> events, AudioMixerGroup mixerGroup)
        {
            EnsureExists();
            if (_instance == null)
            {
                return false;
            }

            return _instance.PrepareTrackInternal(events, mixerGroup);
        }

        public static bool TryStartPlayback(List<HitSoundEvent> events, AudioMixerGroup mixerGroup)
        {
            EnsureExists();
            if (_instance == null)
            {
                return false;
            }

            return _instance.StartPlaybackInternal(events, mixerGroup);
        }

        private bool PrepareTrackInternal(List<HitSoundEvent> events, AudioMixerGroup mixerGroup)
        {
            Settings settings = Main.Settings;
            if (settings == null || events == null || events.Count == 0)
            {
                return false;
            }

            if (settings.RenderMode == RenderMode.StreamingSegments)
            {
                return false;
            }

            StopGeneratedAudioOnly();
            DiscardPreparedTrack();

            events.Sort(delegate (HitSoundEvent a, HitSoundEvent b)
            {
                return a.Time.CompareTo(b.Time);
            });

            long startTicks = Stopwatch.GetTimestamp();
            double firstTime = events[0].Time;
            double lastTime = events[events.Count - 1].Time;
            double duration = Math.Max(0.001, lastTime - firstTime);
            double avgKps = events.Count / duration;

            PreparedTrack prepared = new PreparedTrack
            {
                Mode = settings.RenderMode,
                Signature = ComputeRelativeSignature(events),
                EventCount = events.Count,
                Duration = duration,
                AvgKps = avgKps
            };

            try
            {
                if (settings.RenderMode == RenderMode.SingleClip)
                {
                    float peak;
                    float scale;
                    prepared.WholeClip = PcmRenderer.RenderSegment(
                        "HzHitSoundPreparedWholeTrack",
                        firstTime,
                        lastTime,
                        events,
                        settings,
                        out peak,
                        out scale);
                }
                else
                {
                    List<SegmentJob> jobs = CreateSegments(events, settings);
                    for (int i = 0; i < jobs.Count; i++)
                    {
                        SegmentJob job = jobs[i];
                        float peak;
                        float scale;
                        AudioClip clip = PcmRenderer.RenderSegment(
                            "HzHitSoundPreparedSegment_" + i,
                            job.Start,
                            job.End,
                            job.Events,
                            settings,
                            out peak,
                            out scale);

                        prepared.Segments.Add(new PreparedSegment
                        {
                            RelativeStart = job.Start - firstTime,
                            Clip = clip,
                            Peak = peak,
                            Scale = scale
                        });
                    }
                }

                prepared.RenderMs = TicksToMs(Stopwatch.GetTimestamp() - startTicks);
                _preparedTrack = prepared;
                _mixerGroup = mixerGroup;

                string modeName = settings.RenderMode == RenderMode.SingleClip ? "whole track" : prepared.Segments.Count + " segments";
                StatusText = "Prepared " + modeName + " before music scheduling, " + events.Count + " hitSounds, avg " + avgKps.ToString("0.0") + " KPS, render " + prepared.RenderMs.ToString("0") + " ms.";
                if (settings.LogSummary)
                {
                    Main.Log(StatusText);
                }
                return true;
            }
            catch (Exception ex)
            {
                if (prepared.WholeClip != null)
                {
                    Destroy(prepared.WholeClip);
                }
                for (int i = 0; i < prepared.Segments.Count; i++)
                {
                    if (prepared.Segments[i].Clip != null)
                    {
                        Destroy(prepared.Segments[i].Clip);
                    }
                }

                Main.Error("Pre-render before music failed: " + ex);
                StatusText = "Pre-render failed; vanilla hitSounds will be kept.";
                return false;
            }
        }

        private bool StartPlaybackInternal(List<HitSoundEvent> events, AudioMixerGroup mixerGroup)
        {
            StopGeneratedAudioOnly();

            if (events == null || events.Count == 0)
            {
                StatusText = "No hitSound events captured.";
                return true;
            }

            Settings settings = Main.Settings;
            if (settings == null)
            {
                return false;
            }

            _playbackSceneHandle = SceneManager.GetActiveScene().handle;

            events.Sort(delegate (HitSoundEvent a, HitSoundEvent b)
            {
                return a.Time.CompareTo(b.Time);
            });

            _mixerGroup = mixerGroup;
            _playbackId++;
            _totalEvents = events.Count;
            _firstTime = events[0].Time;
            _lastTime = events[events.Count - 1].Time;
            _nextSegmentIndex = 0;
            _segments.Clear();

            double duration = Math.Max(0.001, _lastTime - _firstTime);
            double avgKps = events.Count / duration;

            if (settings.RenderMode != RenderMode.StreamingSegments)
            {
                return TrySchedulePreparedTrack(events, settings, avgKps);
            }

            BuildSegments(events, settings);
            StatusText = "Captured " + events.Count + " hitSounds, " + _segments.Count + " segments, avg " + avgKps.ToString("0.0") + " KPS.";
            if (settings.LogSummary)
            {
                Main.Log(StatusText);
            }
            return true;
        }

        private bool TrySchedulePreparedTrack(List<HitSoundEvent> events, Settings settings, double avgKps)
        {
            PreparedTrack prepared = _preparedTrack;
            if (prepared == null)
            {
                Main.Error("No pre-rendered track was ready before PlayHitTimes. Keeping vanilla hitSounds.");
                StatusText = "No prepared track; using vanilla hitSounds.";
                return false;
            }

            long actualSignature = ComputeRelativeSignature(events);
            if (prepared.Mode != settings.RenderMode || prepared.Signature != actualSignature || prepared.EventCount != events.Count)
            {
                Main.Error("Prepared hitSound track did not match the actual PlayHitTimes result. Keeping vanilla hitSounds.");
                DiscardPreparedTrack();
                StatusText = "Prepared track mismatch; using vanilla hitSounds.";
                return false;
            }

            _preparedTrack = null;
            double actualFirstTime = events[0].Time;
            double firstSkipped = 0.0;

            try
            {
                if (prepared.Mode == RenderMode.SingleClip)
                {
                    AudioClip clip = prepared.WholeClip;
                    prepared.WholeClip = null;
                    firstSkipped = SchedulePreparedClip(clip, actualFirstTime, settings, "HzHitSoundRenderer Prepared Whole Track");
                }
                else
                {
                    for (int i = 0; i < prepared.Segments.Count; i++)
                    {
                        PreparedSegment segment = prepared.Segments[i];
                        AudioClip clip = segment.Clip;
                        segment.Clip = null;
                        double skipped = SchedulePreparedClip(
                            clip,
                            actualFirstTime + segment.RelativeStart,
                            settings,
                            "HzHitSoundRenderer Prepared Segment");
                        if (i == 0)
                        {
                            firstSkipped = skipped;
                        }
                    }
                }

                _nextSegmentIndex = _segments.Count;
                string modeName = prepared.Mode == RenderMode.SingleClip ? "whole track" : prepared.Segments.Count + " segments";
                StatusText = "Scheduled pre-rendered " + modeName + ", " + events.Count + " hitSounds, avg " + avgKps.ToString("0.0") + " KPS, prepared in " + prepared.RenderMs.ToString("0") + " ms, first skipped " + firstSkipped.ToString("0.000") + " s.";
                if (settings.LogSummary)
                {
                    Main.Log(StatusText);
                }
                return true;
            }
            catch (Exception ex)
            {
                if (prepared.WholeClip != null)
                {
                    Destroy(prepared.WholeClip);
                }
                for (int i = 0; i < prepared.Segments.Count; i++)
                {
                    if (prepared.Segments[i].Clip != null)
                    {
                        Destroy(prepared.Segments[i].Clip);
                    }
                }

                Main.Error("Scheduling the pre-rendered hitSound track failed: " + ex);
                StatusText = "Prepared scheduling failed; vanilla hitSounds will be kept.";
                return false;
            }
        }

        private double SchedulePreparedClip(AudioClip clip, double desiredStartTime, Settings settings, string objectName)
        {
            if (clip == null)
            {
                throw new InvalidOperationException("Prepared AudioClip was null.");
            }

            GameObject go = new GameObject(objectName);
            DontDestroyOnLoad(go);
            AudioSource source = go.AddComponent<AudioSource>();
            ConfigureSource(source, clip);
            return ScheduleSource(go, clip, source, desiredStartTime, settings);
        }

        private void BuildSegments(List<HitSoundEvent> events, Settings settings)
        {
            _segments.Clear();
            _segments.AddRange(CreateSegments(events, settings));
        }

        private static List<SegmentJob> CreateSegments(List<HitSoundEvent> events, Settings settings)
        {
            List<SegmentJob> segments = new List<SegmentJob>();
            if (events == null || events.Count == 0)
            {
                return segments;
            }

            double segmentSeconds = Math.Max(0.25, settings.SegmentSeconds);
            double currentStart = events[0].Time;
            SegmentJob current = new SegmentJob { Start = currentStart, End = currentStart + segmentSeconds };
            segments.Add(current);

            for (int i = 0; i < events.Count; i++)
            {
                HitSoundEvent ev = events[i];
                while (ev.Time >= current.End && segments.Count < 100000)
                {
                    currentStart = current.End;
                    current = new SegmentJob { Start = currentStart, End = currentStart + segmentSeconds };
                    segments.Add(current);
                }
                current.Events.Add(ev);
            }

            return segments;
        }

        private void Update()
        {
            Settings settings = Main.Settings;
            if (settings == null || !Main.Enabled || !settings.EnableRenderer)
            {
                return;
            }

            if (settings.RenderMode != RenderMode.StreamingSegments)
            {
                return;
            }

            if (_nextSegmentIndex >= _segments.Count)
            {
                return;
            }

            double now = AudioSettings.dspTime;
            double ahead = Math.Max(0.5, settings.PreRenderAheadSeconds);
            int guard = 0;

            while (_nextSegmentIndex < _segments.Count && _segments[_nextSegmentIndex].Start <= now + ahead)
            {
                SegmentJob job = _segments[_nextSegmentIndex];
                _nextSegmentIndex++;

                if (job.Rendered)
                {
                    continue;
                }

                if (job.End + Math.Max(0.0, settings.ClipTailSeconds) < now - 0.05)
                {
                    continue;
                }

                RenderAndScheduleStreaming(job, settings, _playbackId);
                guard++;
                if (guard >= Math.Max(1, settings.RenderSegmentsPerFrame))
                {
                    break;
                }
            }
        }

        private void RenderAndScheduleStreaming(SegmentJob job, Settings settings, int playbackId)
        {
            job.Rendered = true;

            try
            {
                RenderedSegment rendered = RenderSegment(job, settings, playbackId, _nextSegmentIndex);
                double skipped = ScheduleRenderedSegment(rendered, settings);

                if (settings.LogSegments)
                {
                    Main.Log("Rendered segment start=" + job.Start.ToString("0.000") + " events=" + job.Events.Count + " peak=" + rendered.Peak.ToString("0.000") + " scale=" + rendered.Scale.ToString("0.000") + " skipped=" + skipped.ToString("0.000"));
                }

                StatusText = "Rendering hitSounds: segment " + _nextSegmentIndex + "/" + _segments.Count + ", events " + _totalEvents + ", first/last " + _firstTime.ToString("0.000") + "/" + _lastTime.ToString("0.000") + ".";
            }
            catch (Exception ex)
            {
                Main.Error("Failed to render hitSound segment: " + ex);
            }
        }

        private RenderedSegment RenderSegment(SegmentJob job, Settings settings, int playbackId, int index)
        {
            float peak;
            float scale;
            AudioClip clip = PcmRenderer.RenderSegment("HzHitSoundSegment_" + playbackId + "_" + index, job.Start, job.End, job.Events, settings, out peak, out scale);
            GameObject go = new GameObject("HzHitSoundRenderer Segment");
            DontDestroyOnLoad(go);
            AudioSource source = go.AddComponent<AudioSource>();
            ConfigureSource(source, clip);

            return new RenderedSegment
            {
                Job = job,
                Clip = clip,
                GameObject = go,
                Source = source,
                Peak = peak,
                Scale = scale
            };
        }

        private void ConfigureSource(AudioSource source, AudioClip clip)
        {
            source.clip = clip;
            source.volume = 1f;
            source.pitch = 1f;
            source.priority = 128;
            if (_mixerGroup != null)
            {
                source.outputAudioMixerGroup = _mixerGroup;
            }
        }

        private double ScheduleRenderedSegment(RenderedSegment rendered, Settings settings)
        {
            return ScheduleSource(rendered.GameObject, rendered.Clip, rendered.Source, rendered.Job.Start, settings);
        }

        private double ScheduleSource(GameObject go, AudioClip clip, AudioSource source, double desiredStartTime, Settings settings)
        {
            double now = AudioSettings.dspTime;
            double scheduleTime = desiredStartTime;
            double skippedSeconds = 0.0;
            double minTime = now + Math.Max(0.0, settings.LateScheduleMarginSeconds);

            if (scheduleTime < minTime)
            {
                if (settings.SkipLateAudioInsteadOfDelaying)
                {
                    skippedSeconds = minTime - scheduleTime;
                    scheduleTime = minTime;
                }
                else
                {
                    scheduleTime = minTime;
                }
            }

            if (skippedSeconds > 0.0)
            {
                if (skippedSeconds >= clip.length - 0.001)
                {
                    Destroy(go);
                    Destroy(clip);
                    return skippedSeconds;
                }

                int timeSamples = (int)Math.Round(skippedSeconds * clip.frequency);
                if (timeSamples < 0)
                {
                    timeSamples = 0;
                }
                if (timeSamples >= clip.samples)
                {
                    timeSamples = clip.samples - 1;
                }
                source.timeSamples = timeSamples;
            }

            source.PlayScheduled(scheduleTime);

            float remainingLength = Math.Max(0.05f, clip.length - (float)skippedSeconds);
            float destroyDelay = (float)Math.Max(0.1, scheduleTime - now + remainingLength + 1.0);
            Destroy(go, destroyDelay);
            Destroy(clip, destroyDelay);
            GeneratedObjects.Add(go);
            GeneratedObjects.Add(clip);
            return skippedSeconds;
        }

        private static long ComputeRelativeSignature(List<HitSoundEvent> events)
        {
            unchecked
            {
                long hash = 1469598103934665603L;
                if (events == null || events.Count == 0)
                {
                    return hash;
                }

                double first = events[0].Time;
                hash = (hash ^ events.Count) * 1099511628211L;
                for (int i = 0; i < events.Count; i++)
                {
                    HitSoundEvent ev = events[i];
                    long relativeMicroseconds = (long)Math.Round((ev.Time - first) * 1000000.0);
                    int clipId = ev.Clip != null ? ev.Clip.GetInstanceID() : 0;
                    int volumeMillionths = (int)Math.Round(ev.Volume * 1000000.0f);

                    hash = (hash ^ relativeMicroseconds) * 1099511628211L;
                    hash = (hash ^ clipId) * 1099511628211L;
                    hash = (hash ^ volumeMillionths) * 1099511628211L;
                }
                return hash;
            }
        }

        private static double TicksToMs(long ticks)
        {
            return ticks * 1000.0 / Stopwatch.Frequency;
        }
    }
}
