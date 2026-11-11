# Conkist.GDK.AI

<p>
  <img src="https://img.shields.io/badge/Unity-6000.3.14f1-black?logo=unity" />
  <img src="https://img.shields.io/badge/C%23-dev-239120?logo=csharp" />
  <img src="https://img.shields.io/badge/version-0.1.0-blue" />
  <img src="https://img.shields.io/badge/license-MIT-green" />
</p>

Embodied AI agents for Unity — real-time conversational avatars with on-device LLM inference, local RAG, lip-sync and voice. The package that powers **Bitzy**, Conkist's interactive mascot.

```json
"me.conkist.gdk.ai": "https://github.com/Conkist/Conkist.GDK.AI.git"
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
- **TTS:** ElevenLabs via `ElevenLabsTextToSpeech` (`ITextToSpeech`)
- **Lip-sync:** `uLipSync` real-time phoneme analysis + blend-shape driver; text-based fallback for streamed/typed responses

### Providers (STT, TTS, lip sync)
Components never name an engine. They use `ISpeechToText`, `ITextToSpeech` and `ILipSyncDriver` (`Conkist.GDK.AI.Providers`).
Each engine sits in its own assembly that exists only while its symbol or package is present: `CONKIST_WHISPER`, `CONKIST_ELEVENLABS`, uLipSync installed.
Put an `AILifetimeScope` in each scene that uses AI. It injects the enabled providers (VContainer). With no provider enabled, the null providers do nothing and the UI shows the feature as unavailable.

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
    "me.conkist.gdk.ai": "https://github.com/Conkist/Conkist.GDK.AI.git"
  }
}
```

**Third-party dependencies.**

| Package | Status | How it arrives |
|---|---|---|
| LLMUnity (`ai.undream.llm`) | Required | Package dependency from OpenUPM. The project `manifest.json` needs `ai.undream.llm` in the OpenUPM scoped registry scopes. |
| uLipSync (`com.hecomi.ulipsync`) | Required | UPM does not accept git URLs in `package.json`. Open **Conkist > GDK > AI > Setup Lipsync** and click Install. |
| whisper.unity (`com.whisper.unity`) | Optional | **Conkist > GDK > AI > Setup Whisper** installs it. Its Enable button installs the package and sets `CONKIST_WHISPER`; Disable clears the symbol and removes the package. Use the **Whisper model** row in the same window to download a model (tiny, base or small) into `Assets/StreamingAssets/Whisper/`. Then select a GameObject and run **Conkist > GDK > AI > Add Whisper Speech-to-Text to Selection**. |
| ElevenLabs | Optional | **Conkist > GDK > AI > Setup ElevenLabs** (Enable button) sets `CONKIST_ELEVENLABS`. No package is needed. |

`me.conkist.gdk.core` also needs UniTask and VContainer. Add the OpenUPM scoped registry with the scopes `com.cysharp.unitask` and `jp.hadashikick.vcontainer`, or install them by Git URL.

---

## Part of

**[Conkist GDK](https://github.com/Conkist)** — Game Development Kit for Unity · [conkist.me](https://conkist.me)

## Whisper tuning (WhisperManager)

Change these values on the `WhisperManager` component to make speech to text faster or more accurate. Measure the time from the stop button to the text before and after each change. The result depends on the machine.

| Field | Effect | Advice |
|---|---|---|
| Model Path | Largest effect on speed and accuracy. | `tiny` is the fastest. `base` and `small` are slower and more accurate. `.en` models understand English only. On low-end phones, use `tiny`. |
| Language | Fixes the language and skips auto-detection. | Set the language of your users (for example `pt`). Do not leave `en` for Portuguese speech. |
| Initial Prompt | Helps with proper nouns. | Write the character name and game terms (for example `Bitzy, Conkist`). It works with the `[SPEECH RECOGNITION]` rule of the system prompt prefix. |
| Use Gpu | Runs the model on the GPU when one is available. | Turn on and test. The log line `no GPU found` means the CPU is used. |
| Flash Attention | Faster inference on the GPU. | Turn on and test. It can have no effect on the CPU. |
| Single Segment | One segment for each recording. | Turn on for short chat sentences. |
| No Context | Does not reuse the previous text as a prompt. | Keep on (default). |
| Translate To English | Translates the speech to English. | Keep off, unless you want a translation. |
| Audio Ctx (experimental) | Smaller audio context for short audio. | Change only after you measure. A low value can reduce accuracy. |

The stream fields (`Step`, `Keep`, `Length`, `Update Prompt`, `Drop Old Buffer`, `Use Vad`) are for real-time streaming. `WhisperSpeechToText` records first and transcribes after the stop button, so they have no effect.

Download a model with **Conkist > GDK > AI > Setup Whisper > Whisper model**. Keep model files out of git (`Assets/StreamingAssets/Whisper/*.bin`).

## SmartMascotsSettings

One asset (`Conkist > GDK > AI > Settings`, assigned to the `ApplicationManager`) holds the **base** configuration. Order of precedence: scene components (defaults) -> `SmartMascotsSettings` (base) -> `ChatAgentWrapper` "Agent Overrides" (per character: provider and model).

| Group | Fields |
|---|---|
| Provider | LLMUnity (offline) or OpenRouter (online). The Inspector shows only the matching group below. |
| Offline | Base model, context size, GPU layers, threads, thinking mode, temperature, max tokens. "Apply to scene LLM" off leaves the `LLM` and `LLMAgent` components untouched. Applied at startup, before the model loads. |
| Online | OpenRouter model and API key. |
| RAG | Local only. On/off, results per question, use embeddings when available (LLMUnity `RAG`, not WebGL), rebuild on start. Without embeddings a keyword index (BM25) is used. |
| Speech to text | Show or hide the record button. "Send transcription automatically" on = the text is sent as a message; off = it is written in the input for review. The Whisper provider needs `CONKIST_WHISPER`. |
| Text to speech | ElevenLabs on/off, API key, voice, model. Off means text-only answers. |
| Lip sync | Turns the uLipSync driver on or off. Off uses the animator fallback. |

The chat component is `LLMAgent` (system prompt, history, `Chat()`). `LLMCharacter` is deprecated and `LLMClient` is stateless.

API keys are stored in the asset. Do not commit it and do not ship it in a player build.
