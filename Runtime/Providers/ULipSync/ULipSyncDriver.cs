using System.Collections.Generic;
using UnityEngine;

namespace Conkist.GDK.AI.Providers
{
    /// <summary>
    /// Lip sync with uLipSync. Put it on the avatar next to the uLipSync and uLipSyncBlendShape components;
    /// the references are found by itself. Exists only while uLipSync is installed (assembly define constraint).
    /// </summary>
    public class ULipSyncDriver : MonoBehaviour, ILipSyncDriver
    {
        [SerializeField] private uLipSync.uLipSync _uLipSync;
        [SerializeField] private uLipSync.uLipSyncBlendShape _blendShape;

        public bool IsAvailable => _blendShape != null;

        private void Awake()
        {
            if (_uLipSync == null) _uLipSync = GetComponentInChildren<uLipSync.uLipSync>(true);
            if (_blendShape == null) _blendShape = GetComponentInChildren<uLipSync.uLipSyncBlendShape>(true);
        }

        public void SetAudioDriven(bool enabled)
        {
            if (_uLipSync != null) _uLipSync.enabled = enabled;
        }

        public void ShowViseme(string phoneme)
        {
            if (_blendShape == null) return;

            bool silence = phoneme == "-";
            _blendShape.OnLipSyncUpdate(new uLipSync.LipSyncInfo
            {
                phoneme = phoneme,
                volume = silence ? 0f : 1f,
                rawVolume = silence ? 0f : Mathf.Pow(10f, _blendShape.maxVolume),
                phonemeRatios = new Dictionary<string, float> { { phoneme, 1f } }
            });
        }

        public void Silence() => ShowViseme("-");
    }
}
