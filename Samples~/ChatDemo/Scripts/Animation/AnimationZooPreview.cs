using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Animations;

namespace Conkist.AnimationUtils
{
    /// <summary>
    /// A simple component to preview a single AnimationClip on an Animator 
    /// without needing to create an AnimatorController. Perfect for Animation Zoos.
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public class AnimationZooPreview : MonoBehaviour
    {
        [Tooltip("The animation clip to play on this GameObject")]
        public AnimationClip clipToPlay;

        [Tooltip("If true, the animation will play automatically on Start.")]
        public bool playOnStart = true;
        public bool loop = true;

        private PlayableGraph _playableGraph;

        private AnimationClipPlayable _clipPlayable;

        private void Start()
        {
            if (playOnStart && clipToPlay != null)
            {
                PlayAnimation();
            }
        }

        private void Update()
        {
            // Force loop behavior natively on the playable if the clip isn't naturally looping
            if (loop && _playableGraph.IsValid() && _clipPlayable.IsValid() && clipToPlay != null)
            {
                if (!clipToPlay.isLooping && _clipPlayable.GetTime() >= clipToPlay.length)
                {
                    _clipPlayable.SetTime(_clipPlayable.GetTime() % clipToPlay.length);
                }
            }
        }

        public void PlayAnimation()
        {
            if (clipToPlay == null)
            {
                Debug.LogWarning("[AnimationZooPreview] No AnimationClip assigned to play.", this);
                return;
            }

            var animator = GetComponent<Animator>();

            // Clean up existing graph if it's already playing something
            if (_playableGraph.IsValid())
            {
                _playableGraph.Destroy();
            }

            // Create graph manually to store the clip playable reference
            _playableGraph = PlayableGraph.Create("AnimationZooGraph");
            _playableGraph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);

            var output = AnimationPlayableOutput.Create(_playableGraph, "Animation", animator);
            _clipPlayable = AnimationClipPlayable.Create(_playableGraph, clipToPlay);
            output.SetSourcePlayable(_clipPlayable);

            _playableGraph.Play();
        }

        private void OnDisable()
        {
            // Destroy the graph to free up memory when disabled or destroyed
            if (_playableGraph.IsValid())
            {
                _playableGraph.Destroy();
            }
        }
    }
}
