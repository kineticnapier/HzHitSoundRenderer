using System;
using System.Reflection;
using HarmonyLib;

namespace HzHitSoundRenderer
{
    /// <summary>
    /// Optional integration with ADOFAIAudioSync without taking a build-time
    /// dependency on that mod.
    /// </summary>
    internal static class AudioSyncCompat
    {
        private const string HandshakeTypeName =
            "Kiner.ADOFAIAudioSync.Runtime.CheckpointStartHandshakeRuntime";

        private static PropertyInfo _isActiveProperty;
        private static bool _lookupFailedLogged;

        internal static bool IsCheckpointHandshakeActive()
        {
            try
            {
                if (_isActiveProperty == null)
                {
                    Type handshakeType = AccessTools.TypeByName(HandshakeTypeName);
                    if (handshakeType == null)
                    {
                        return false;
                    }

                    _isActiveProperty = AccessTools.Property(
                        handshakeType,
                        "IsActive");
                    if (_isActiveProperty == null)
                    {
                        LogLookupFailureOnce(
                            "AudioSync checkpoint state property was not found.");
                        return false;
                    }
                }

                object value = _isActiveProperty.GetValue(null, null);
                return value is bool && (bool)value;
            }
            catch (Exception ex)
            {
                LogLookupFailureOnce(
                    "Could not read AudioSync checkpoint state: " + ex.Message);
                return false;
            }
        }

        private static void LogLookupFailureOnce(string message)
        {
            if (_lookupFailedLogged)
            {
                return;
            }

            _lookupFailedLogged = true;
            Main.Error(message);
        }
    }
}
