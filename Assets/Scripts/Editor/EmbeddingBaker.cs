using UnityEditor;
using Unity.EditorCoroutines.Editor;
using System.Collections;
using System.Diagnostics;
using UnityEngine;

namespace ChatSystemWithSentis
{
    public static class EmbeddingBaker
    {
        public static KnowledgeEntry[] entries;
        public static AdventureQueryEngine engine;

        [MenuItem("Lab36/Bake Knowledge Embeddings")]
        static void BakeAll()
        { 
            UnityEngine.Debug.Log("Starting embedding bake...");
            // Busca todos los assets de tipo KnowledgeEntry en cualquier subcarpeta
            string[] guids = AssetDatabase.FindAssets("t:ChatSystemWithSentis.KnowledgeEntry", new[] { "Assets/IA/Sentis/Entries" });

            entries = new KnowledgeEntry[guids.Length];
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                entries[i] = AssetDatabase.LoadAssetAtPath<KnowledgeEntry>(path);
            }            
            engine = AssetDatabase.LoadAssetAtPath<AdventureQueryEngine>("Assets/IA/Sentis/Engine.prefab");

            EditorCoroutineUtility.StartCoroutine(BakeCoroutines(entries), engine);
        }

        static IEnumerator BakeCoroutines(KnowledgeEntry[] entries)
        {
            foreach (var e in entries)
            {
                yield return EditorCoroutineUtility.StartCoroutine(engine.GetEmbeddingCoroutine(e.responseText), engine);
                e.embeddingCache = engine.lastEmbedding;
                EditorUtility.SetDirty(e);
                AssetDatabase.SaveAssets();
                UnityEngine.Debug.Log("Embedding bake completed for: " + e.name);
            }


            
        }
    }
}