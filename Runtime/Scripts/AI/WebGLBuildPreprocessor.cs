#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using System.Collections.Generic;

namespace Conkist.GDK.SmartAgents
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
}
#endif
