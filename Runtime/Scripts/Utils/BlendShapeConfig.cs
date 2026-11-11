using System;
using UnityEngine;

namespace Conkist.GDK.AI.Animation
{
    [Serializable]
    public class BlendShapeConfig
    {
        public int blendShapeIndex;
        public float startValue;
        public float endValue;

        public void UpdateBlends(SkinnedMeshRenderer mesh, BlendShapeConfig[] blends, float value)
        {
            foreach(var blend in blends)
            {
                float relativeValue = blend.GetRelativeValue(value);
                mesh.SetBlendShapeWeight(blend.blendShapeIndex, relativeValue);
            }
        }

        public float GetRelativeValue(float value, bool inverse = false)
        {
            return Mathf.Lerp(inverse ? endValue : startValue, inverse ? startValue : endValue, Mathf.Clamp01(value));
        }
    }
}
