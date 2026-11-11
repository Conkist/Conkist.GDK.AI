using UnityEditor;
using UnityEngine;

namespace Conkist.GDK.AI.Editor
{
    /// <summary>
    /// Default-looking inspector for ChatAgentWrapper that hides what does not apply:
    /// the provider and model fields only appear while their override toggle is on,
    /// and the OpenRouter client only appears when the effective provider is OpenRouter.
    /// </summary>
    [CustomEditor(typeof(ChatAgentWrapper), true)]
    [CanEditMultipleObjects]
    public class ChatAgentWrapperEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            var overrideProvider = serializedObject.FindProperty("_overrideProvider");
            var provider = serializedObject.FindProperty("_provider");
            var overrideModel = serializedObject.FindProperty("_overrideModel");

            var settings = SmartMascotsSettings.Current;
            var effective = overrideProvider.boolValue
                ? (LLMProvider)provider.enumValueIndex
                : settings != null ? settings.provider : LLMProvider.LLMUnity;
            bool online = effective == LLMProvider.OpenRouter;

            var prop = serializedObject.GetIterator();
            for (bool enter = true; prop.NextVisible(enter); enter = false)
            {
                switch (prop.name)
                {
                    case "m_Script":
                        using (new EditorGUI.DisabledScope(true)) EditorGUILayout.PropertyField(prop, true);
                        break;

                    case "_provider":
                        if (!overrideProvider.boolValue) break;
                        EditorGUI.indentLevel++;
                        EditorGUILayout.PropertyField(prop, new GUIContent("Provider"));
                        EditorGUI.indentLevel--;
                        break;

                    case "_model":
                        if (!overrideModel.boolValue) break;
                        EditorGUI.indentLevel++;
                        EditorGUILayout.PropertyField(prop, new GUIContent(online ? "Model (OpenRouter id or preset)" : "Model (LLMUnity file name)"));
                        EditorGUI.indentLevel--;
                        break;

                    case "_openRouterClient":
                        if (online) EditorGUILayout.PropertyField(prop, true);
                        break;

                    default:
                        EditorGUILayout.PropertyField(prop, true);
                        break;
                }

                // After the model override, show what is used when the overrides are off.
                if (prop.name == "_model") ShowBase(settings, overrideProvider.boolValue, overrideModel.boolValue, effective);
            }

            serializedObject.ApplyModifiedProperties();
        }

        private static void ShowBase(SmartMascotsSettings settings, bool providerOverridden, bool modelOverridden, LLMProvider effective)
        {
            if (providerOverridden && modelOverridden) return;
            string text;
            if (settings == null)
            {
                text = "No SmartMascotsSettings found in the open scene: scene component values are used.";
            }
            else
            {
                string model = effective == LLMProvider.LLMUnity ? settings.offline.model : settings.online.model;
                if (string.IsNullOrEmpty(model)) model = "(scene LLM component)";
                text = "From SmartMascotsSettings: " + (providerOverridden ? "" : effective + ", ") + (modelOverridden ? "" : model);
            }
            EditorGUILayout.HelpBox(text, MessageType.None);
        }
    }
}
