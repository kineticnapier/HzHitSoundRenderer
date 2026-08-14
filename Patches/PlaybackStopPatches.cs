using System;
using System.Reflection;
using HarmonyLib;
using HzHitSoundRenderer.Audio;

namespace HzHitSoundRenderer.Patches
{
    // ADOFAI's generated hit sounds are not owned by AudioManager, so the game's
    // normal StopAllSounds calls cannot stop them. Mirror the important playback
    // lifecycle events here.

    [HarmonyPatch(typeof(scrController), "FailAction")]
    internal static class ScrControllerFailActionStopPatch
    {
        private static void Postfix(scrController __instance)
        {
            if (__instance == null)
            {
                return;
            }

            // FailAction has several early-return/no-fail paths. Only stop after the
            // controller really entered a game-over state.
            if (__instance.currentState == States.Fail || __instance.currentState == States.Fail2)
            {
                HighKpsHitSoundScheduler.StopAllGeneratedAudio("game over");
            }
        }
    }

    [HarmonyPatch(typeof(scrConductor), "KillAllSounds")]
    internal static class ScrConductorKillAllSoundsStopPatch
    {
        private static void Prefix()
        {
            HighKpsHitSoundScheduler.StopAllGeneratedAudio("conductor stopped sounds");
        }
    }

    [HarmonyPatch(typeof(scrController), "Restart")]
    internal static class ScrControllerRestartStopPatch
    {
        private static void Prefix()
        {
            HighKpsHitSoundScheduler.StopAllGeneratedAudio("restart");
        }
    }

    [HarmonyPatch(typeof(scnEditor), "QuitToMenu")]
    internal static class ScnEditorQuitToMenuStopPatch
    {
        private static void Prefix()
        {
            HighKpsHitSoundScheduler.StopAllGeneratedAudio("leaving level editor");
        }
    }

    [HarmonyPatch(typeof(scrController), "QuitToMainMenu")]
    internal static class ScrControllerQuitToMainMenuStopPatch
    {
        private static void Prefix()
        {
            HighKpsHitSoundScheduler.StopAllGeneratedAudio("leaving scene");
        }
    }

    [HarmonyPatch]
    internal static class SceneLoadStopPatch
    {
        private static MethodBase TargetMethod()
        {
            MethodInfo legacy = AccessTools.Method(
                typeof(ADOBase), "LoadScene", new[] { typeof(string) });
            if (legacy != null)
            {
                return legacy;
            }

            // v3.3.1 moved scene loading from ADOBase to scrLoader.
            Type loaderType = AccessTools.TypeByName("scrLoader");
            return loaderType == null
                ? null
                : AccessTools.Method(loaderType, "LoadScene", new[] { typeof(string) });
        }

        private static void Prefix()
        {
            HighKpsHitSoundScheduler.StopAllGeneratedAudio("scene change");
        }
    }
}
