# Conkist.GDK.AI

<p>
  <img src="https://img.shields.io/badge/Unity-6000.3.14f1-black?logo=unity" />
  <img src="https://img.shields.io/badge/C%23-dev-239120?logo=csharp" />
  <img src="https://img.shields.io/badge/version-0.1.0-blue" />
  <img src="https://img.shields.io/badge/license-MIT-green" />
</p>

Embodied AI agents for Unity — real-time conversational avatars with on-device LLM inference, local RAG, lip-sync and voice. The package that powers **Bitzy**, Conkist's interactive mascot.

```json
"me.conkist.gdk.smart-agents": "https://github.com/Conkist/Conkist.GDK.SmartAgents.git"
```

---

## How it works

**Decoupled cognition and rendering.** The LLM handles "thinking" — generating dialogue and intent as a structured JSON payload. Unity handles everything visible: animation, lip-sync, gaze, UI. The two sides are connected by a single event (`OnFeedbackReceived`) and never reference each other directly.

**JSON contract.** The LLM is prompted to emit a small deterministic object:
```json
{ "dialogue": "Hello!", "intent": "wave" }
```
`ChatAgentWrapper` deserializes this into `AgentActions[]` and fires subscribers in parallel — animator, movement controller and UI all react independently.

**Local RAG.** Knowledge files and a generated animation-list document are embedded locally and searched per turn to augment the LLM prompt — even when using a remote provider (OpenRouter).

---

## LLM Providers

| Provider | Mode | Notes |
|---|---|---|
| [LLMUnity](https://github.com/undream-ai/LLMUnity) | On-device | No network required; not available on WebGL |
| [OpenRouter](https://openrouter.ai) | Remote | Any model via API key |

Both providers share the same RAG augmentation and JSON contract.

---

## Key Components

### `ChatAgentWrapper`
High-level agent bridge. Composes the system prompt, retrieves RAG context, sends messages to the configured provider, deserializes the response and fires `OnFeedbackReceived`.

### `AvatarAnimatorWrapper`
Consumes `OnFeedbackReceived` events. Resolves animation keys from `CharacterSO.animations` at runtime and plays clips via Unity Playables. Drives lip-sync through `uLipSync` and includes a text-based fallback for when no live audio is available.

### `CharacterSO`
ScriptableObject that defines the agent's identity:
- `defaultPrompt` — base system prompt
- `knowledgeFiles` — documents added to the RAG index
- `animations` (`SerializedKeyAnimation[]`) — key + description + clip mappings
- `ragSavePath` — pre-built embedding file path for production builds

### Speech pipeline
- **STT:** Whisper.cpp via `whisper.unity` (local, 16kHz) — disabled on WebGL
- **TTS:** ElevenLabs via `ElevenLabsVoiceGenerator`
- **Lip-sync:** `uLipSync` real-time phoneme analysis + blend-shape driver; text-based fallback for streamed/typed responses

### WebGL support
Chat is bridged to JavaScript (`WebGLBridge_PostMessage` / `OnUnityMessageSubmitted`) since local inference and Whisper are not available in-browser. STT and LLMUnity are compiled out under `UNITY_WEBGL`.

---

## Quick Start

1. Add `CharacterSO` to your project and configure `defaultPrompt`, `animations` and `knowledgeFiles`
2. Add `ChatAgentWrapper` + `LLMAgent` (or configure OpenRouter key) to a GameObject
3. Add `AvatarAnimatorWrapper` to the avatar; assign `CharacterSO`
4. Rebuild RAG embeddings via the `ChatAgentWrapper` inspector button
5. Press Play — the agent listens, thinks and responds

> For standalone builds: pre-generate the RAG save file in the Editor, or enable `forceRebuildRAG` before building.

---

## Install

Add to `Packages/manifest.json`:

```json
{
  "dependencies": {
    "me.conkist.gdk.smart-agents": "https://github.com/Conkist/Conkist.GDK.SmartAgents.git"
  }
}
```

**Third-party dependencies** (also in `manifest.json`):
```json
"ai.undream.llm": "https://github.com/undream-ai/LLMUnity.git?path=...",
"com.hecomi.ulipsync": "https://github.com/hecomi/uLipSync.git#upm",
"com.whisper.unity": "https://github.com/Macoron/whisper.unity.git?path=..."
```

---

## Part of

**[Conkist GDK](https://github.com/Conkist)** — Game Development Kit for Unity · [conkist.me](https://conkist.me)
