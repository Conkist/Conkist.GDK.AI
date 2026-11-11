using UnityEditor;
using UnityEngine;
using Conkist.GDK.AI.Providers;

namespace Conkist.GDK.AI.Editor
{
    /// <summary>Exists only while uLipSync is installed. Adds the lip sync driver to the selected avatar.</summary>
    public static class ULipSyncLinker
    {
        [MenuItem("GameObject/Conkist/AI/Add Lip Sync Driver", false, 22)]
        [MenuItem("Conkist/GDK/AI/Add Lip Sync Driver to Selection")]
        private static void Add()
        {
            var go = Selection.activeGameObject;
            if (go == null) { Debug.LogWarning("[Conkist GDK AI] Select the avatar GameObject first."); return; }

            if (!go.TryGetComponent<ULipSyncDriver>(out _)) Undo.AddComponent<ULipSyncDriver>(go);
            Debug.Log("[Conkist GDK AI] Lip sync driver added. It finds the uLipSync and uLipSyncBlendShape components on the avatar.");
        }
    }
}
