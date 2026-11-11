using UnityEditor;
using UnityEngine;
using Whisper;

namespace Conkist.GDK.AI.Editor
{
    /// <summary>
    /// Exists only while symbol CONKIST_WHISPER is on (assembly constraint).
    /// Adds the Whisper speech-to-text provider to the selected GameObject; AILifetimeScope then injects it as ISpeechToText.
    /// </summary>
    public static class WhisperLinker
    {
        [MenuItem("GameObject/Conkist/AI/Add Whisper Speech-to-Text", false, 20)]
        [MenuItem("Conkist/GDK/AI/Add Whisper Speech-to-Text to Selection")]
        private static void Add()
        {
            var go = Selection.activeGameObject;
            if (go == null) { Debug.LogWarning("[Conkist GDK AI] Select a GameObject first."); return; }

            if (!go.TryGetComponent<WhisperManager>(out _)) Undo.AddComponent<WhisperManager>(go);
            if (!go.TryGetComponent<Conkist.GDK.AI.Providers.WhisperSpeechToText>(out _)) Undo.AddComponent<Conkist.GDK.AI.Providers.WhisperSpeechToText>(go);
            Debug.Log("[Conkist GDK AI] Whisper added. Set the model path on the WhisperManager and keep an AILifetimeScope in the scene.");
        }
    }
}
