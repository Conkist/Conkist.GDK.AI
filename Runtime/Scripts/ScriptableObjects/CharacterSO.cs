using System.Collections.Generic;
using UnityEngine;

namespace Conkist.GDK.SmartAgents
{
    [CreateAssetMenu(fileName = "NewCharacter", menuName = "SmartAgents/Character")]
    public class CharacterSO : ScriptableObject
    {
        [Header("Character Info")]
        public string defaultName;
        public Sprite defaultAvatar;
        [TextArea(5, 10)]
        public string defaultPrompt;

        [Header("RAG (Knowledge Base) Settings")]
        public List<TextAsset> knowledgeFiles;
        public SerializedKeyAnimation[] expressions;
        public string ragSavePath = "CharacterKnowledge.zip";
    }
}
