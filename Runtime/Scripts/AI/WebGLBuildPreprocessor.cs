#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using System.Collections.Generic;
using System.IO;

namespace Conkist.GDK.AI
{
    public class WebGLBuildPreprocessor : IProcessSceneWithReport
    {
        public int callbackOrder => 0;

        public void OnProcessScene(UnityEngine.SceneManagement.Scene scene, BuildReport report)
        {
            // Only strip when building for WebGL
            if (report != null && report.summary.platform == BuildTarget.WebGL)
            {
                Debug.Log($"[WebGLBuildPreprocessor] Processing scene '{scene.name}' for WebGL build. Stripping LLM components...");

                var rootObjects = scene.GetRootGameObjects();
                var objectsToDestroy = new List<GameObject>();

                foreach (var root in rootObjects)
                {
                    TraverseAndStrip(root, objectsToDestroy);
                }

                // Destroy marked dedicated game objects
                foreach (var obj in objectsToDestroy)
                {
                    if (obj != null)
                    {
                        Debug.Log($"[WebGLBuildPreprocessor] Destroying dedicated LLM GameObject '{obj.name}'");
                        Object.DestroyImmediate(obj);
                    }
                }
            }
        }

        private void TraverseAndStrip(GameObject go, List<GameObject> objectsToDestroy)
        {
            if (go == null) return;

            // Retrieve components
            var components = go.GetComponents<Component>();
            int llmComponentCount = 0;
            int totalMonobehaviours = 0;

            foreach (var comp in components)
            {
                if (comp == null) continue;

                string typeName = comp.GetType().FullName ?? "";
                
                // Strip any component under LLMUnity or UndreamAI namespaces
                if (typeName.StartsWith("LLMUnity.") || typeName.StartsWith("UndreamAI."))
                {
                    Debug.Log($"[WebGLBuildPreprocessor] Stripping component '{typeName}' from '{go.name}'");
                    Object.DestroyImmediate(comp);
                    llmComponentCount++;
                }
                else if (comp is MonoBehaviour)
                {
                    totalMonobehaviours++;
                }
            }

            // If the GameObject was dedicated purely to LLM/RAG and has no other custom behaviors, mark it for destruction
            if (llmComponentCount > 0 && totalMonobehaviours == 0)
            {
                if (go.name.Contains("LLM") || go.name.Contains("RAG") || go.name.Contains("Embedder"))
                {
                    if (!objectsToDestroy.Contains(go))
                    {
                        objectsToDestroy.Add(go);
                    }
                }
            }

            // Recurse children
            for (int i = 0; i < go.transform.childCount; i++)
            {
                TraverseAndStrip(go.transform.GetChild(i).gameObject, objectsToDestroy);
            }
        }
    }

    public class WebGLStreamingAssetsPreprocessor : IPreprocessBuildWithReport
    {
        public int callbackOrder => 100; // Run late to backup after other preprocess steps (e.g. LLMBuilder setup)

        public void OnPreprocessBuild(BuildReport report)
        {
            if (report.summary.platform == BuildTarget.WebGL)
            {
                Debug.Log("[WebGLStreamingAssetsPreprocessor] Preprocessing build for WebGL. Backing up StreamingAssets...");
                WebGLStreamingAssetsBackupHelper.Backup();
            }
        }
    }

    public class WebGLStreamingAssetsPostprocessor : IPostprocessBuildWithReport
    {
        public int callbackOrder => -100; // Run early to restore before other postprocess steps (e.g. LLMBuilder reset)

        public void OnPostprocessBuild(BuildReport report)
        {
            if (report.summary.platform == BuildTarget.WebGL)
            {
                Debug.Log("[WebGLStreamingAssetsPostprocessor] Postprocessing build for WebGL. Restoring StreamingAssets...");
                WebGLStreamingAssetsBackupHelper.Restore();
            }
        }
    }

    public static class WebGLStreamingAssetsBackupHelper
    {
        public static string BackupPath => Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Temp/StreamingAssetsWebGLBackup");
        public static string StreamingAssetsPath => Path.Combine(Application.dataPath, "StreamingAssets");

        [MenuItem("Tools/Conkist/Restore WebGL StreamingAssets Backup")]
        public static void ForceRestore()
        {
            if (Directory.Exists(BackupPath))
            {
                Restore();
                EditorUtility.DisplayDialog("StreamingAssets Restore", "StreamingAssets restored successfully from backup.", "OK");
            }
            else
            {
                EditorUtility.DisplayDialog("StreamingAssets Restore", "No backup directory found to restore.", "OK");
            }
        }

        public static void Backup()
        {
            try
            {
                if (!Directory.Exists(StreamingAssetsPath)) return;

                if (Directory.Exists(BackupPath))
                {
                    Directory.Delete(BackupPath, true);
                }
                Directory.CreateDirectory(BackupPath);

                // Move directories
                foreach (var dir in Directory.GetDirectories(StreamingAssetsPath))
                {
                    string dirName = Path.GetFileName(dir);
                    string target = Path.Combine(BackupPath, dirName);
                    Directory.Move(dir, target);

                    string metaFile = dir + ".meta";
                    if (File.Exists(metaFile))
                    {
                        File.Move(metaFile, target + ".meta");
                    }
                }

                // Move files (except the StreamingAssets directory meta file itself)
                foreach (var file in Directory.GetFiles(StreamingAssetsPath))
                {
                    string fileName = Path.GetFileName(file);
                    string target = Path.Combine(BackupPath, fileName);
                    File.Move(file, target);
                }

                AssetDatabase.Refresh();
                Debug.Log("[WebGLStreamingAssetsBackupHelper] Backup completed successfully.");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[WebGLStreamingAssetsBackupHelper] Failed to backup: {ex.Message}");
            }
        }

        public static void Restore()
        {
            try
            {
                if (!Directory.Exists(BackupPath)) return;

                if (!Directory.Exists(StreamingAssetsPath))
                {
                    Directory.CreateDirectory(StreamingAssetsPath);
                }

                // Move directories back
                foreach (var dir in Directory.GetDirectories(BackupPath))
                {
                    string dirName = Path.GetFileName(dir);
                    string target = Path.Combine(StreamingAssetsPath, dirName);
                    if (Directory.Exists(target))
                    {
                        Directory.Delete(target, true);
                    }
                    Directory.Move(dir, target);

                    string metaFile = dir + ".meta";
                    if (File.Exists(metaFile))
                    {
                        string targetMeta = target + ".meta";
                        if (File.Exists(targetMeta)) File.Delete(targetMeta);
                        File.Move(metaFile, targetMeta);
                    }
                }

                // Move files back
                foreach (var file in Directory.GetFiles(BackupPath))
                {
                    string fileName = Path.GetFileName(file);
                    string target = Path.Combine(StreamingAssetsPath, fileName);
                    if (File.Exists(target))
                    {
                        File.Delete(target);
                    }
                    File.Move(file, target);
                }

                Directory.Delete(BackupPath, true);
                AssetDatabase.Refresh();
                Debug.Log("[WebGLStreamingAssetsBackupHelper] Restore completed successfully.");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[WebGLStreamingAssetsBackupHelper] Failed to restore: {ex.Message}");
            }
        }
    }
}
#endif
