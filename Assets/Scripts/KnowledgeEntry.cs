using UnityEngine;
using UnityEngine.EventSystems;

namespace ChatSystemWithSentis
{

    [CreateAssetMenu(fileName = "KnowledgeEntry", menuName = "Scriptable Objects/KnowledgeEntry")]
    public class KnowledgeEntry : ScriptableObject
    {
        public string entryId;
        public string intentLabel;        // etiqueta que el clasificador devuelve
        public int chapterRequired;       // capítulo mínimo para desbloquearse
        [TextArea] public string responseText;
        public float[] embeddingCache;    // pre-computado en editor, no en runtime
        public GameEventData[] unlocks;   // qué dispara al encontrarse
    }

    [System.Serializable]
    public class GameEventData
    {
        public enum UnlockType { Location, Person, DialogOption, ObjectInteraction };

        public UnlockType type;           // Location, Person, DialogOption, ObjectInteraction
        public string targetId;
        public string displayLabel;
    }
}
