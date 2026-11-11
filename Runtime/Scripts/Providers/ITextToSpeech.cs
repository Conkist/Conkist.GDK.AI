using System.Threading.Tasks;
using UnityEngine;

namespace Conkist.GDK.AI.Providers
{
    /// <summary>Text to speech. The engine (ElevenLabs, ...) lives in its own provider assembly.</summary>
    public interface ITextToSpeech
    {
        /// <summary>A real engine is present. False for the null provider.</summary>
        bool IsAvailable { get; }

        /// <summary>Returns the spoken clip, or null when nothing could be generated.</summary>
        Task<AudioClip> TextToSpeech(string text, string voiceId = null);
    }
}
