using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Conkist.GDK.AI.Editor
{
    /// <summary>
    /// A Panel Settings asset needs a theme from the project it lives in. The ChatDemo sample cannot carry one
    /// (a theme reference only resolves in its own project), so after the sample is imported this gives
    /// "Chat Demo Panel Settings" the project's default runtime theme, creating that theme file when the project has none.
    /// </summary>
    internal sealed class ChatDemoPanelSettingsFixer : AssetPostprocessor
    {
        private const string PanelSettingsFile = "Chat Demo Panel Settings.asset";
        private const string DefaultThemeName = "UnityDefaultRuntimeTheme";
        private const string DefaultThemePath = "Assets/UI Toolkit/UnityThemes/UnityDefaultRuntimeTheme.tss";

        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            foreach (var path in imported)
            {
                if (!path.EndsWith(PanelSettingsFile)) continue;
                var settings = AssetDatabase.LoadAssetAtPath<PanelSettings>(path);
                if (settings == null || settings.themeStyleSheet != null) continue;

                // Wait for the importer to finish before touching other assets.
                EditorApplication.delayCall += () => Assign(path);
            }
        }

        private static void Assign(string path)
        {
            var settings = AssetDatabase.LoadAssetAtPath<PanelSettings>(path);
            if (settings == null || settings.themeStyleSheet != null) return;

            var theme = FindProjectTheme() ?? CreateDefaultTheme();
            if (theme == null) return;

            settings.themeStyleSheet = theme;
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssetIfDirty(settings);
            Debug.Log($"[Conkist GDK AI] Set the theme of '{path}' to '{AssetDatabase.GetAssetPath(theme)}'.");
        }

        private static ThemeStyleSheet FindProjectTheme()
        {
            return AssetDatabase.FindAssets("t:ThemeStyleSheet", new[] { "Assets" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => Path.GetFileNameWithoutExtension(p) == DefaultThemeName)
                .Select(AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>)
                .FirstOrDefault(t => t != null);
        }

        private static ThemeStyleSheet CreateDefaultTheme()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(DefaultThemePath));
            File.WriteAllText(DefaultThemePath, "@import url(\"unity-theme://default\");");
            AssetDatabase.ImportAsset(DefaultThemePath);
            return AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(DefaultThemePath);
        }
    }
}
