using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityModManagerNet;
using HzHitSoundRenderer.Audio;

namespace HzHitSoundRenderer
{
    public static class Main
    {
        internal static readonly string Version =
            typeof(Main).Assembly.GetName().Version.ToString(3);

        internal static UnityModManager.ModEntry ModEntry;
        internal static Settings Settings;
        internal static bool Enabled;

        private static Harmony _harmony;

        public static bool Load(UnityModManager.ModEntry modEntry)
        {
            ModEntry = modEntry;
            Settings = UnityModManager.ModSettings.Load<Settings>(modEntry);
            ClampRawSettings();
            RefreshTextFields();

            modEntry.OnGUI = OnGUI;
            modEntry.OnSaveGUI = OnSaveGUI;
            modEntry.OnToggle = OnToggle;
            modEntry.OnUnload = OnUnload;

            try
            {
                _harmony = new Harmony(modEntry.Info.Id);
                _harmony.PatchAll(Assembly.GetExecutingAssembly());
                HighKpsHitSoundScheduler.EnsureExists();
                Log("Loaded. Renderer is " + (Settings.EnableRenderer ? "ON" : "OFF") + ".");
                return true;
            }
            catch (Exception ex)
            {
                Error("Load failed:\n" + ex);
                return false;
            }
        }

        private static bool OnToggle(UnityModManager.ModEntry modEntry, bool value)
        {
            Enabled = value;
            if (!value)
            {
                HighKpsHitSoundScheduler.StopAllGeneratedAudio();
            }
            return true;
        }

        private static bool OnUnload(UnityModManager.ModEntry modEntry)
        {
            try
            {
                HighKpsHitSoundScheduler.StopAllGeneratedAudio();
                HighKpsHitSoundScheduler.DestroyInstance();
                ClipDataCache.Clear();

                if (_harmony != null)
                {
                    _harmony.UnpatchAll(modEntry.Info.Id);
                    _harmony = null;
                }
                return true;
            }
            catch (Exception ex)
            {
                Error("Unload failed:\n" + ex);
                return false;
            }
        }

        private static void OnGUI(UnityModManager.ModEntry modEntry)
        {
            if (Settings == null)
            {
                return;
            }
            ClampRawSettings();

            GUILayout.Label("HzHitSoundRenderer v" + Version + "  高KPS/Hz Charting用ヒット音レンダラー");
            GUILayout.Label("全ヒットを間引かず、元のヒット音PCMをそのまま重ねます。リミッター/ソフトクリップ/密度音量/音色加工はかけません。");

            GUILayout.Space(6f);
            Settings.EnableRenderer = GUILayout.Toggle(Settings.EnableRenderer, "有効化：高KPSヒット音をPCMレンダリングする");
            Settings.ReplaceVanillaHitSounds = GUILayout.Toggle(Settings.ReplaceVanillaHitSounds, "バニラのヒット音を置き換える（二重再生防止。基本ON）");
            Settings.WarmupClipsBeforeReplacing = GUILayout.Toggle(Settings.WarmupClipsBeforeReplacing, "音源を読めない時はバニラ再生に戻す（安全。基本ON）");

            GUILayout.Space(8f);
            GUILayout.Label("再生方式");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(GetRenderModeLabel(Settings.RenderMode), GUILayout.Width(220)))
            {
                Settings.RenderMode = NextRenderMode(Settings.RenderMode);
            }
            GUILayout.Label(GetRenderModeDescription(Settings.RenderMode));
            GUILayout.EndHorizontal();

            GUILayout.Space(8f);
            GUILayout.Label("プリセット");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("おすすめ：元音PCM"))
            {
                ApplyHzPreviewPreset();
            }
            if (GUILayout.Button("軽量"))
            {
                ApplyLightPreset();
            }
            if (GUILayout.Button("プツプツ対策：全体1本"))
            {
                ApplySingleClipPreset();
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(8f);
            GUILayout.Label("音量（無加工）");
            DrawFloat("ヒット音倍率", ref Settings.MasterVolume, ref Settings.MasterVolumeText, 0f, 1f);
            GUILayout.Label("倍率1.0でADOFAIの元ヒット音量をそのまま使います。暴力的な音量ブーストはできないようにしています。");

            GUILayout.Space(8f);
            GUILayout.Label("元音PCM");
            DrawDouble("コピーする長さ 秒（0で元音全体）", ref Settings.MaxSourceClipSeconds, ref Settings.MaxSourceClipSecondsText, 0.0, 0.2);
            DrawDouble("分割秒数", ref Settings.SegmentSeconds, ref Settings.SegmentSecondsText, 0.25, 30.0);
            DrawInt("サンプルレート", ref Settings.RenderSampleRate, ref Settings.RenderSampleRateText, 8000, 192000);
            Settings.OutputChannels = GUILayout.Toggle(Settings.OutputChannels >= 2, "ステレオ出力（OFFでMono・軽い）") ? 2 : 1;

            GUILayout.Space(8f);
            Settings.ShowAdvancedSettings = GUILayout.Toggle(Settings.ShowAdvancedSettings, "詳細設定を表示");
            if (Settings.ShowAdvancedSettings)
            {
                DrawAdvancedSettings();
            }

            GUILayout.Space(8f);
            Settings.LogSummary = GUILayout.Toggle(Settings.LogSummary, "再生ごとに概要ログを出す");
            Settings.LogSegments = GUILayout.Toggle(Settings.LogSegments, "各セグメントのログを出す（重いので普段OFF）");

            GUILayout.Space(6f);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("生成したヒット音を停止"))
            {
                HighKpsHitSoundScheduler.StopAllGeneratedAudio();
            }
            if (GUILayout.Button("音源キャッシュ削除"))
            {
                ClipDataCache.Clear();
                Log("Clip cache cleared.");
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(8f);
            GUILayout.Label(HighKpsHitSoundScheduler.StatusText);
        }

        private static void ClampRawSettings()
        {
            if (Settings == null)
            {
                return;
            }

            Settings.MasterVolume = Math.Max(0f, Math.Min(1f, Settings.MasterVolume));
            Settings.OutputGain = 1.0f;
            Settings.DensityGainMode = DensityGainMode.None;
            Settings.ReferenceKpsForFullVolume = 1000000.0;
            Settings.UseLimiter = false;
            Settings.MaxOutputPeak = 1.0f;
            Settings.UseSoftClip = false;
            Settings.SoftClipDrive = 1.0f;
            Settings.SourceFadeOutSeconds = 0.0;
        }

        private static void DrawAdvancedSettings()
        {
            GUILayout.Space(8f);
            GUILayout.Label("レンダリング詳細");
            DrawDouble("先読み秒数", ref Settings.PreRenderAheadSeconds, ref Settings.PreRenderAheadSecondsText, 0.5, 30.0);
            DrawDouble("セグメント末尾の余韻 秒", ref Settings.ClipTailSeconds, ref Settings.ClipTailSecondsText, 0.0, 2.0);
            DrawInt("1フレームの最大生成数", ref Settings.RenderSegmentsPerFrame, ref Settings.RenderSegmentsPerFrameText, 1, 16);
            DrawDouble("遅れ時の安全マージン 秒", ref Settings.LateScheduleMarginSeconds, ref Settings.LateScheduleMarginSecondsText, 0.0, 2.0);
            Settings.SkipLateAudioInsteadOfDelaying = GUILayout.Toggle(Settings.SkipLateAudioInsteadOfDelaying, "レンダリングが遅れた時は全体を遅らせず、遅れた分だけ音声を途中から鳴らす");

            GUILayout.Space(8f);
            GUILayout.Label("無加工方針");
            GUILayout.Label("v1.0.0以降ではフェードアウト、密度音量、リミッター、ソフトクリップ、最終音量ブーストをレンダラーで使用しません。");
            GUILayout.Label("音が割れる場合は、倍率を下げるか、ADOFAI側の曲音量/ヒット音量で調整してください。");
        }

        private static void ApplyHzPreviewPreset()
        {
            Settings.RenderMode = RenderMode.StreamingSegments;
            Settings.SkipLateAudioInsteadOfDelaying = true;
            Settings.SegmentSeconds = 0.5;
            Settings.PreRenderAheadSeconds = 12.0;
            Settings.ClipTailSeconds = 0.03;
            Settings.RenderSampleRate = 48000;
            Settings.OutputChannels = 2;
            Settings.RenderSegmentsPerFrame = 1;
            Settings.MaxSourceClipSeconds = 0.0;
            Settings.OriginalBodyClipSeconds = 0.0;
            Settings.OriginalBodyVolume = 0.0f;
            Settings.SourceFadeOutSeconds = 0.0;
            Settings.MasterVolume = 1.0f;
            Settings.OutputGain = 1.0f;
            Settings.DensityGainMode = DensityGainMode.None;
            Settings.DensityWindowSeconds = 0.05;
            Settings.ReferenceKpsForFullVolume = 1000000.0;
            Settings.UseLimiter = false;
            Settings.MaxOutputPeak = 1.0f;
            Settings.UseSoftClip = false;
            Settings.SoftClipDrive = 1.0f;
            Settings.LogSegments = false;
            RefreshTextFields();
            Log("Applied raw original PCM preset.");
        }

        private static void ApplyLoudPreset()
        {
            // Kept only for old builds/references. v1.0.0 and later do not provide a loud/processed preset.
            ApplyHzPreviewPreset();
            Log("Loud preset is disabled in raw PCM mode; applied raw original PCM preset instead.");
        }

        private static void ApplyLightPreset()
        {
            Settings.RenderMode = RenderMode.StreamingSegments;
            Settings.SkipLateAudioInsteadOfDelaying = true;
            Settings.SegmentSeconds = 0.35;
            Settings.PreRenderAheadSeconds = 12.0;
            Settings.ClipTailSeconds = 0.02;
            Settings.RenderSampleRate = 16000;
            Settings.OutputChannels = 1;
            Settings.RenderSegmentsPerFrame = 1;
            Settings.MaxSourceClipSeconds = 0.012;
            Settings.OriginalBodyClipSeconds = 0.0;
            Settings.OriginalBodyVolume = 0.0f;
            Settings.SourceFadeOutSeconds = 0.0;
            Settings.MasterVolume = 1.0f;
            Settings.OutputGain = 1.0f;
            Settings.DensityGainMode = DensityGainMode.None;
            Settings.DensityWindowSeconds = 0.05;
            Settings.ReferenceKpsForFullVolume = 1000000.0;
            Settings.UseLimiter = false;
            Settings.MaxOutputPeak = 1.0f;
            Settings.UseSoftClip = false;
            Settings.SoftClipDrive = 1.0f;
            Settings.LogSegments = false;
            RefreshTextFields();
            Log("Applied light preset.");
        }

        private static void ApplySingleClipPreset()
        {
            ApplyHzPreviewPreset();
            Settings.RenderMode = RenderMode.SingleClip;
            Settings.SkipLateAudioInsteadOfDelaying = true;
            Settings.SegmentSeconds = 0.5;
            Settings.PreRenderAheadSeconds = 12.0;
            Settings.ClipTailSeconds = 0.05;
            Settings.RenderSampleRate = 48000;
            Settings.OutputChannels = 2;
            Settings.RenderSegmentsPerFrame = 1;
            Settings.LateScheduleMarginSeconds = 0.05;
            Settings.MaxSourceClipSeconds = 0.0;
            Settings.OriginalBodyClipSeconds = 0.0;
            Settings.OriginalBodyVolume = 0.0f;
            Settings.SourceFadeOutSeconds = 0.0;
            Settings.MasterVolume = 1.0f;
            Settings.OutputGain = 1.0f;
            Settings.DensityGainMode = DensityGainMode.None;
            Settings.DensityWindowSeconds = 0.05;
            Settings.ReferenceKpsForFullVolume = 1000000.0;
            Settings.UseLimiter = false;
            Settings.MaxOutputPeak = 1.0f;
            Settings.UseSoftClip = false;
            Settings.SoftClipDrive = 1.0f;
            Settings.LogSegments = false;
            RefreshTextFields();
            Log("Applied single-clip anti-click preset.");
        }

        private static void RefreshTextFields()
        {
            Settings.SegmentSecondsText = Settings.SegmentSeconds.ToString("0.###");
            Settings.PreRenderAheadSecondsText = Settings.PreRenderAheadSeconds.ToString("0.###");
            Settings.ClipTailSecondsText = Settings.ClipTailSeconds.ToString("0.###");
            Settings.RenderSampleRateText = Settings.RenderSampleRate.ToString();
            Settings.RenderSegmentsPerFrameText = Settings.RenderSegmentsPerFrame.ToString();
            Settings.LateScheduleMarginSecondsText = Settings.LateScheduleMarginSeconds.ToString("0.###");
            Settings.MaxSourceClipSecondsText = Settings.MaxSourceClipSeconds.ToString("0.###");
            Settings.OriginalBodyClipSecondsText = Settings.OriginalBodyClipSeconds.ToString("0.###");
            Settings.OriginalBodyVolumeText = Settings.OriginalBodyVolume.ToString("0.###");
            Settings.SourceFadeOutSecondsText = Settings.SourceFadeOutSeconds.ToString("0.###");
            Settings.MasterVolumeText = Settings.MasterVolume.ToString("0.###");
            Settings.OutputGainText = Settings.OutputGain.ToString("0.###");
            Settings.DensityWindowSecondsText = Settings.DensityWindowSeconds.ToString("0.###");
            Settings.ReferenceKpsForFullVolumeText = Settings.ReferenceKpsForFullVolume.ToString("0.###");
            Settings.MaxOutputPeakText = Settings.MaxOutputPeak.ToString("0.###");
            Settings.SoftClipDriveText = Settings.SoftClipDrive.ToString("0.###");
        }

        private static RenderMode NextRenderMode(RenderMode mode)
        {
            if (mode == RenderMode.StreamingSegments) return RenderMode.PreRenderSegments;
            if (mode == RenderMode.PreRenderSegments) return RenderMode.SingleClip;
            return RenderMode.StreamingSegments;
        }

        private static string GetRenderModeLabel(RenderMode mode)
        {
            if (mode == RenderMode.StreamingSegments) return "分割先読み";
            if (mode == RenderMode.PreRenderSegments) return "全セグメント事前焼き";
            return "全体1本焼き";
        }

        private static string GetRenderModeDescription(RenderMode mode)
        {
            if (mode == RenderMode.StreamingSegments) return "軽い。通常はこちら。遅れると区切りでプツることがあります。";
            if (mode == RenderMode.PreRenderSegments) return "曲を予約する前に全セグメントを焼きます。開始時に焼き時間ぶん待ちますが、譜面と曲の同期を優先します。";
            return "曲を予約する前に全体を1本のAudioClipへ焼きます。開始時に焼き時間ぶん待ちますが、継ぎ目と同期に最も強い方式です。";
        }

        private static DensityGainMode NextGainMode(DensityGainMode mode)
        {
            if (mode == DensityGainMode.None) return DensityGainMode.Sqrt;
            if (mode == DensityGainMode.Sqrt) return DensityGainMode.Linear;
            return DensityGainMode.None;
        }

        private static string GetDensityGainModeLabel(DensityGainMode mode)
        {
            if (mode == DensityGainMode.None) return "なし";
            if (mode == DensityGainMode.Sqrt) return "Sqrt（おすすめ）";
            return "Linear（強め）";
        }

        private static void DrawDouble(string label, ref double value, ref string text, double min, double max)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label + ":", GUILayout.Width(260));
            text = GUILayout.TextField(text, GUILayout.Width(90));
            if (GUILayout.Button("適用", GUILayout.Width(60)))
            {
                double parsed;
                if (double.TryParse(text, out parsed))
                {
                    value = Math.Max(min, Math.Min(max, parsed));
                    text = value.ToString("0.###");
                }
            }
            GUILayout.Label(value.ToString("0.###"));
            GUILayout.EndHorizontal();
        }

        private static void DrawFloat(string label, ref float value, ref string text, float min, float max)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label + ":", GUILayout.Width(260));
            text = GUILayout.TextField(text, GUILayout.Width(90));
            if (GUILayout.Button("適用", GUILayout.Width(60)))
            {
                float parsed;
                if (float.TryParse(text, out parsed))
                {
                    value = Math.Max(min, Math.Min(max, parsed));
                    text = value.ToString("0.###");
                }
            }
            GUILayout.Label(value.ToString("0.###"));
            GUILayout.EndHorizontal();
        }

        private static void DrawInt(string label, ref int value, ref string text, int min, int max)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label + ":", GUILayout.Width(260));
            text = GUILayout.TextField(text, GUILayout.Width(90));
            if (GUILayout.Button("適用", GUILayout.Width(60)))
            {
                int parsed;
                if (int.TryParse(text, out parsed))
                {
                    value = Math.Max(min, Math.Min(max, parsed));
                    text = value.ToString();
                }
            }
            GUILayout.Label(value.ToString());
            GUILayout.EndHorizontal();
        }

        private static void OnSaveGUI(UnityModManager.ModEntry modEntry)
        {
            if (Settings != null)
            {
                Settings.Save(modEntry);
            }
        }

        internal static void Log(string message)
        {
            if (ModEntry != null && ModEntry.Logger != null)
            {
                ModEntry.Logger.Log("[HzHitSoundRenderer] " + message);
            }
        }

        internal static void Error(string message)
        {
            if (ModEntry != null && ModEntry.Logger != null)
            {
                ModEntry.Logger.Error("[HzHitSoundRenderer] " + message);
            }
        }
    }
}
