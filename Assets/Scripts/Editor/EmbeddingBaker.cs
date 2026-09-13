using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Unity.EditorCoroutines.Editor;
using UnityEditor;
using UnityEngine;

namespace ChatSystemWithSentis
{
    public static class EmbeddingBaker
    {
        public static KnowledgeEntry[] entries;
        public static AdventureQueryEngine engine;


        [MenuItem("Lab36/Load All Questions")]
        static void LoadAllQuestions() 
        {
            // Busca todos los assets de tipo KnowledgeEntry en cualquier subcarpeta
            string[] guids = AssetDatabase.FindAssets("t:ChatSystemWithSentis.KnowledgeEntry", new[] { "Assets/Entries" });

            entries = new KnowledgeEntry[guids.Length];
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                entries[i] = AssetDatabase.LoadAssetAtPath<KnowledgeEntry>(path);
            }
            engine = AssetDatabase.LoadAssetAtPath<AdventureQueryEngine>("Assets/Prefabs/Engine.prefab");

            const string CsvPath = "Assets/Preguntas/intent_data.csv";

            string[] lines = File.ReadAllLines(CsvPath);

            Dictionary<string, List<string>> questionsByLabel = new Dictionary<string, List<string>>();

            for (int i = 1; i < lines.Length; i++)
            {
                string[] parts = lines[i].Split(',');
                if (parts.Length < 2) continue;
                string label = parts[1].Trim();
                string question = parts[0].Trim();
                if (!questionsByLabel.ContainsKey(label))
                {
                    questionsByLabel[label] = new List<string>();
                }
                questionsByLabel[label].Add(question);
            }

            foreach (var entry in entries)
            {
                if (questionsByLabel.TryGetValue(entry.entryId, out var questions))
                {
                    
                    entry.questions = new TrainingQuestion[questions.Count];
                    for (int j = 0; j < questions.Count; j++)
                    {
                        entry.questions[j] = new TrainingQuestion { text = questions[j] };
                    }
                    EditorUtility.SetDirty(entry);
                }
            }
        }



        [MenuItem("Lab36/Bake Knowledge Embeddings")]
        static void BakeAll()
        { 
            UnityEngine.Debug.Log("Starting embedding bake...");
            // Busca todos los assets de tipo KnowledgeEntry en cualquier subcarpeta
            string[] guids = AssetDatabase.FindAssets("t:ChatSystemWithSentis.KnowledgeEntry", new[] { "Assets/Entries" });

            entries = new KnowledgeEntry[guids.Length];
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                entries[i] = AssetDatabase.LoadAssetAtPath<KnowledgeEntry>(path);
            }            
            engine = AssetDatabase.LoadAssetAtPath<AdventureQueryEngine>("Assets/Prefabs/Engine.prefab");

            EditorCoroutineUtility.StartCoroutine(BakeCoroutines(entries), engine);
        }

        static IEnumerator BakeCoroutines(KnowledgeEntry[] entries)
        {
            foreach (var e in entries)
            {
                yield return EditorCoroutineUtility.StartCoroutine(engine.GetEmbeddingCoroutine(e.responseText), engine);
                e.embeddingCache = (float[])engine.lastEmbedding.Clone();

                foreach (var question in e.questions)
                {
                    yield return EditorCoroutineUtility.StartCoroutine(
                        engine.GetEmbeddingCoroutine(question.text),
                        engine
                    );

                    question.embeddingCache =
                        (float[])engine.lastEmbedding.Clone();
                }

                EditorUtility.SetDirty(e);
                AssetDatabase.SaveAssets();
                UnityEngine.Debug.Log("Embedding bake completed for: " + e.name);
            }


            
        }
    }
}