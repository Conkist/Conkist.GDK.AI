using UnityEditor;
using UnityEngine;
using Conkist.GDK.AI.Providers;

namespace Conkist.GDK.AI.Editor
{
    /// <summary>Exists only while symbol CONKIST_ELEVENLABS is on. Adds the ElevenLabs provider to the selected GameObject.</summary>
    public static class ElevenLabsLinker
    {
        [MenuItem("GameObject/Conkist/AI/Add ElevenLabs Text-to-Speech", false, 21)]
        [MenuItem("Conkist/GDK/AI/Add ElevenLabs Text-to-Speech to Selection")]
        private static void Add()
        {
            var go = Selection.activeGameObject;
            if (go == null) { Debug.LogWarning("[Conkist GDK AI] Select a GameObject first."); return; }

            if (!go.TryGetComponent<ElevenLabsTextToSpeech>(out _)) Undo.AddComponent<ElevenLabsTextToSpeech>(go);
            Debug.Log("[Conkist GDK AI] ElevenLabs added. Set the API key on the component and keep an AILifetimeScope in the scene.");
        }
    }
}
