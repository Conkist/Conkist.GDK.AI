# Conkist GDK SmartAgents (Unity Package)

This package provides the SmartAgents runtime used by Project SmartAvatar (Bitzy). It includes LLM integration adapters, a local RAG (retrieval-augmented generation) helper, avatar animation adapters, and sample components for building embodied conversational agents in Unity.

## Package Contents

- Runtime/Scripts/AI
	- `ChatAgentWrapper.cs` — high-level agent bridge that composes system prompt, optional local RAG context, and sends/receives messages from either LLMUnity or OpenRouter.
	- `OpenRouterClient.cs` — simple client wrapper for OpenRouter API with history management and `ChatAsync`.
	- RAG integration: local embedding store used for retrieval and prompt augmentation.

- Runtime/Scripts/Animation
	- `AvatarAnimatorWrapper.cs` — adapter that maps `CharacterSO` animation keys to clips and plays via Playables; reacts to `OnFeedbackReceived` events.

- Runtime/Scripts/ScriptableObjects
	- `CharacterSO.cs` — character configuration including `defaultPrompt`, `knowledgeFiles`, `animations` (type `SerializedKeyAnimation[]`), and `ragSavePath`.

- Runtime/Scripts/Utils
	- `SerializedKeyAnimation.cs` — serializable struct with `key`, `description` and `clip` used to declare animations and context descriptions.

- Assets/Samples
	- Sample scenes and knowledge files used in demos (see `Assets/Samples/Conkist.GDK.SmartAgents/0.1.0/ChatDemo/Bitzy/Knowledge/`).

## Key Concepts

- Decoupled cognition and rendering: LLM and audio services handle "thinking" while Unity handles rendering and animation scheduling.
- JSON contract: the LLM is prompted to emit a small, deterministic JSON payload (dialogue + intent fields) that Unity parses to orchestrate blendshapes, gestures, and gaze.
- Local RAG: documents (knowledge files and generated documents such as the animation list) are embedded locally and searched per-turn to provide retrieval-augmented context to the LLM.

## How Animation Mapping Works

- Animation keys and optional `description` fields are authored on `CharacterSO.animations` (array of `SerializedKeyAnimation`).
- During RAG rebuild, `ChatAgentWrapper` will generate an "animation list" document (key, clip name, and description) and add it to the RAG embeddings so the LLM can consult semantic context about when to trigger each key.
- `AvatarAnimatorWrapper` populates its runtime dictionary from `CharacterSO.animations` on `Start()` and uses that dictionary to resolve keys to `AnimationClip`s.

## Event Flow (Feedback Processing)

- `ChatAgentWrapper` deserializes the LLM response into `AgentActions[]` and immediately invokes `OnFeedbackReceived` for each action, allowing multiple subscribers (animator, movement, UI) to react in parallel.
- `ManageFeedbackTiming` coroutine handles durations, typing indicators, and sequencing concerns separately from event dispatch.

## Supported Providers

- LLMUnity (local/native integration) — via `LLMAgent` on the same GameObject.
- OpenRouter (remote) — via `OpenRouterClient` (assign API key in inspector).

Note: The RAG is local; using a remote LLM provider (OpenRouter) still supports local RAG prompt augmentation.

## Samples and Knowledge

- Demo knowledge and research documents are stored in `Assets/Samples/.../Knowledge/`. These can be included in `CharacterSO.knowledgeFiles` to be embedded into the RAG.
- The sample `smartavatars.txt` contains an overview and the latest updates; it is useful to include in the RAG as a project-level knowledge document.

## Tips

- Rebuild RAG embeddings in the Editor after adding or editing knowledge files or `CharacterSO.animations` to ensure embeddings are up-to-date.
- For standalone builds, pre-generate and include the RAG save file (default `ragSavePath`) or enable `forceRebuildRAG` in Editor before building.
- Use the `InjectRawFeedbackJson` helper for testing animation/event flows in the Editor.

## Contributing

- Design discussions, issues, and feature requests should be tracked in the main project repository.

## License & Authors

Authors: Renato Lima, Leonardo Estevam, Marcos Seruffo

