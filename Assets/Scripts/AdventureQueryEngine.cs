// AdventureQueryEngine.cs
using System.Collections;
using System.Collections.Generic;
using Unity.InferenceEngine;
using UnityEngine;

namespace ChatSystemWithSentis
{
    public class AdventureQueryEngine : MonoBehaviour
    {

        //generador de intent tardo 25 minutos

        private WordPieceTokenizer _tokenizer;
        [SerializeField] private TextAsset vocabFile;

        [SerializeField] private ModelAsset embeddingModel;
        [SerializeField] private ModelAsset intentModel;
        [SerializeField] private KnowledgeEntry[] allEntries;
        [SerializeField] private GameProgressSO gameProgress;

        [Header("Tuning")]
        [SerializeField] private float semanticWeight = 0.6f;
        [SerializeField] private float intentWeight = 0.4f;
        [SerializeField] private float minimumScore = 0.55f;

        private Worker _embeddingWorker;
        private Worker _intentWorker;

        public float[] lastEmbedding;
        public bool _embeddingReady;

        private string _lastIntentLabel;
        private float _lastIntentScore;
        [SerializeField] private string[] _intentLabels;


        void Awake()
        {
            Initialize();
        }

        public void Initialize()
        {
            if (_tokenizer == null)
                _tokenizer = new WordPieceTokenizer(vocabFile.text);
            if (_embeddingWorker == null)
                _embeddingWorker = new Worker(ModelLoader.Load(embeddingModel), BackendType.CPU);
            if (_intentWorker == null)
                _intentWorker = new Worker(ModelLoader.Load(intentModel), BackendType.CPU);
        }

        public IEnumerator QueryCoroutine(string userInput, System.Action<QueryResult> onComplete)
        {
            Initialize();

            string normalized = NormalizeText(userInput);
            Debug.Log($"Input normalizado: '{normalized}'");

            // 1. Embedding de la pregunta
            yield return StartCoroutine(GetEmbeddingCoroutine(normalized));
            float[] queryEmbedding = lastEmbedding;

            // Verificá que el embedding no sea todo zeros
            float embSum = 0f;
            foreach (var v in queryEmbedding) embSum += Mathf.Abs(v);
            Debug.Log($"Suma del query embedding: {embSum}");


            // 2. Clasificación de intención
            yield return StartCoroutine(ClassifyIntentCoroutine(normalized));
            string label = _lastIntentLabel;
            float intentScore = _lastIntentScore;

            Debug.Log($"Intent: {_lastIntentLabel} (score: {_lastIntentScore:F3})");

            if (_lastIntentLabel == "otro")
            {
                _lastIntentLabel = null;
                _lastIntentScore = 0f;
            }

            Debug.Log($"Intent: {_lastIntentLabel} (score: {_lastIntentScore:F3})");


            // 3. Buscar la mejor entrada
            KnowledgeEntry best = null;
            float bestScore = 0f;

            foreach (var entry in allEntries)
            {
                if (entry.chapterRequired > gameProgress.CurrentChapter)
                    continue;

                float semantic = CosineSimilarity(queryEmbedding, entry.embeddingCache);
                float intent = (entry.intentLabel == label) ? intentScore : 0f;
                float combined = semantic * semanticWeight + intent * intentWeight;


                float cacheSum = 0f;
                foreach (var v in entry.embeddingCache) cacheSum += Mathf.Abs(v);

                Debug.Log($"[{entry.entryId}] cacheSum: {cacheSum:F3} | semantic: {semantic:F3} | intent: {intent:F3} | combined: {combined:F3}");

                if (combined > bestScore)
                {
                    bestScore = combined;
                    best = entry;
                }
            }

            QueryResult result = (best == null || bestScore < minimumScore)
                ? QueryResult.NoMatch()
                : QueryResult.Match(best, bestScore, MatchSource.Combined);

            onComplete?.Invoke(result);
        }

        private string NormalizeText(string text)
        {
            text = text.ToLowerInvariant().Trim();

            // Eliminar signos de puntuación dobles del castellano
            text = text.Replace("¿", "")
                       .Replace("¡", "")
                       .Replace("«", "")
                       .Replace("»", "");

            // Reemplazos explícitos antes de quitar diacríticos
            text = text.Replace("ñ", "n")
                       .Replace("ü", "u");

            // Quitar diacríticos
            text = text.Normalize(System.Text.NormalizationForm.FormD);

            var result = new System.Text.StringBuilder();
            foreach (char c in text)
            {
                var category = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c);
                if (category != System.Globalization.UnicodeCategory.NonSpacingMark)
                    result.Append(c);
            }

            return result.ToString().Normalize(System.Text.NormalizationForm.FormC);
        }

        private float CosineSimilarity(float[] a, float[] b)
        {
            float dot = 0f, magA = 0f, magB = 0f;
            for (int i = 0; i < a.Length; i++)
            {
                dot += a[i] * b[i];
                magA += a[i] * a[i];
                magB += b[i] * b[i];
            }
            return dot / (Mathf.Sqrt(magA) * Mathf.Sqrt(magB) + 1e-8f);
        }



        public IEnumerator ClassifyIntentCoroutine(string text)
        {
            _lastIntentLabel = null;
            _lastIntentScore = 0f;

            List<int> tokens = _tokenizer.Tokenize(text, maxLength: 128);
            int seqLen = tokens.Count;

            int[] inputIds = tokens.ToArray();
            int[] attentionMask = new int[seqLen];
            for (int i = 0; i < seqLen; i++)
                attentionMask[i] = 1;

            using var tInputIds = new Tensor<int>(new TensorShape(1, seqLen), inputIds);
            using var tAttentionMask = new Tensor<int>(new TensorShape(1, seqLen), attentionMask);

            _intentWorker.SetInput("input_ids", tInputIds);
            _intentWorker.SetInput("attention_mask", tAttentionMask);
            _intentWorker.Schedule();

            // Shape de salida: (1, numClasses) — un logit por etiqueta
            var output = _intentWorker.PeekOutput("logits") as Tensor<float>;
            output.ReadbackRequest();
            yield return new WaitUntil(() => output.IsReadbackRequestDone());

            // Softmax manual para convertir logits en probabilidades
            int numClasses = output.shape[1];
            float[] probs = Softmax(output, numClasses);

            // Buscar la clase con mayor probabilidad
            int bestIndex = 0;
            float bestProb = 0f;
            for (int i = 0; i < numClasses; i++)
            {
                if (probs[i] > bestProb)
                {
                    bestProb = probs[i];
                    bestIndex = i;
                }
            }

            _lastIntentLabel = _intentLabels[bestIndex];
            _lastIntentScore = bestProb;

            output.Dispose();
        }

        private float[] Softmax(Tensor<float> logits, int numClasses)
        {
            float max = float.MinValue;
            for (int i = 0; i < numClasses; i++)
                if (logits[0, i] > max) max = logits[0, i];

            float sum = 0f;
            float[] result = new float[numClasses];
            for (int i = 0; i < numClasses; i++)
            {
                result[i] = Mathf.Exp(logits[0, i] - max); // restar max para estabilidad numérica
                sum += result[i];
            }
            for (int i = 0; i < numClasses; i++)
                result[i] /= sum;

            return result;
        }




        public IEnumerator GetEmbeddingCoroutine(string text)
        {
            Initialize();

            _embeddingReady = false;

            string normalized = NormalizeText(text);
            List<int> tokens = _tokenizer.Tokenize(normalized, maxLength: 128);


            const int FIXED_LEN = 128;
            int[] inputIds = new int[FIXED_LEN];
            int[] attentionMask = new int[FIXED_LEN];

            for (int i = 0; i < FIXED_LEN; i++)
            {
                if (i < tokens.Count)
                {
                    inputIds[i] = tokens[i];
                    attentionMask[i] = 1;
                }
                else
                {
                    inputIds[i] = 0; // PAD token
                    attentionMask[i] = 0; // ignorar en mean pooling
                }
            }

            using var tInputIds = new Tensor<int>(new TensorShape(1, FIXED_LEN), inputIds);
            using var tAttentionMask = new Tensor<int>(new TensorShape(1, FIXED_LEN), attentionMask);

            _embeddingWorker.SetInput("input_ids", tInputIds);
            _embeddingWorker.SetInput("attention_mask", tAttentionMask);
            _embeddingWorker.Schedule();

            // En WebGL hay que esperar que la GPU termine — iniciar la solicitud de readback y esperar hasta que esté listo
            var output = _embeddingWorker.PeekOutput("last_hidden_state") as Tensor<float>;
            output.ReadbackRequest();
            yield return new WaitUntil(() => output.IsReadbackRequestDone());

            lastEmbedding = MeanPool(output, attentionMask);
            output.Dispose();
            _embeddingReady = true;
        }


        private float[] MeanPool(Tensor<float> hiddenState, int[] attentionMask)
        {
            int seqLen, embedDim;

            // Manejar ambos casos: (1, seqLen, embedDim) o (seqLen, embedDim)
            if (hiddenState.shape.rank == 3)
            {
                seqLen = hiddenState.shape[1];
                embedDim = hiddenState.shape[2];
            }
            else if (hiddenState.shape.rank == 2)
            {
                seqLen = hiddenState.shape[0];
                embedDim = hiddenState.shape[1];
            }
            else
            {
                Debug.LogError($"Shape inesperado: rank {hiddenState.shape.rank}");
                return new float[768];
            }

            var result = new float[embedDim];
            int realTokenCount = 0;

            for (int t = 0; t < seqLen; t++)
            {
                if (attentionMask[t] == 0) continue;

                realTokenCount++;
                for (int d = 0; d < embedDim; d++)
                {
                    // Acceso según rank
                    float val = hiddenState.shape.rank == 3
                        ? hiddenState[0, t, d]
                        : hiddenState[t, d];

                    result[d] += val;
                }
            }

            // Promediar
            float magnitude = 0f;
            for (int d = 0; d < embedDim; d++)
            {
                result[d] /= realTokenCount;
                magnitude += result[d] * result[d];
            }
            magnitude = Mathf.Sqrt(magnitude);

            for (int d = 0; d < embedDim; d++)
                result[d] /= magnitude + 1e-8f;

            return result;
        }
    }
}