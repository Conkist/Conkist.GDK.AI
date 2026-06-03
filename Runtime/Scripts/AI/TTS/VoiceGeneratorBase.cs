using System.Threading.Tasks;
using UnityEngine;
using Conkist.GDK;

namespace Conkist.GDK.SmartAgents.TTS
{
    /// <summary>
    /// Abstract base class for Text-To-Speech generation.
    /// Inherits from SingletonBehaviour so it can be accessed globally and managed automatically.
    /// </summary>
    public abstract class VoiceGeneratorBase : SingletonBehaviour<VoiceGeneratorBase>
    {
        /// <summary>
        /// Generates an AudioClip from the given text asynchronously.
        /// </summary>
        /// <param name="text">The text to synthesize.</param>
        /// <returns>A Task that resolves to an AudioClip containing the synthesized voice.</returns>
        public abstract Task<AudioClip> TextToSpeech(string text);
    }
}
