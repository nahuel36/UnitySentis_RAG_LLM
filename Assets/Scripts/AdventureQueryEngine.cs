// AdventureQueryEngine.cs
using System.Collections;
using System.Collections.Generic;
using Unity.InferenceEngine;
using UnityEngine;
using UnityEngine.Networking;
using System.Text;


namespace ChatSystemWithSentis
{
    [System.Serializable]
    public class EmbeddingRequest
    {
        public string inputs;
    }


    public class AdventureQueryEngine : MonoBehaviour
    {

        [SerializeField]
        private const string EmbeddingApiUrl = "https://withered-brook-0991.nahuel36gg.workers.dev";
        //generador de intent tardo 25 minutos

        private WordPieceTokenizer _tokenizer;
        [SerializeField] private TextAsset vocabFile;

        [SerializeField] private ModelAsset embeddingModel;
        [SerializeField] private KnowledgeEntry[] allEntries;
        [SerializeField] private GameProgressSO gameProgress;

        [Header("Tuning")]
        [SerializeField] private float questionWeight = 0.8f;
        [SerializeField] private float responseWeight = 0.2f;
        [SerializeField] private float minimumScore = 0.55f;

        private Worker _embeddingWorker;

        public float[] lastEmbedding;
        public bool _embeddingReady;

        void Awake()
        {
            Initialize();
        }

        public void Initialize()
        {
            //if (_tokenizer == null)
              //  _tokenizer = new WordPieceTokenizer(vocabFile.text);
           // if (_embeddingWorker == null)
             //   _embeddingWorker = new Worker(ModelLoader.Load(embeddingModel), BackendType.CPU);
        }


        class QuestionCompared
        {
            public KnowledgeEntry matchedEntry;
            public float questionScore;
            public float responseScore;
        }

        public IEnumerator QueryCoroutine(string userInput, System.Action<QueryResult> onComplete)
        {
            Initialize();

            string normalized = NormalizeText(userInput);
            Debug.Log($"Input normalizado: '{normalized}'");

            // 1. Embedding de la pregunta
            yield return StartCoroutine(GetEmbeddingFromApiCoroutine(normalized));

            if (lastEmbedding == null)
            {
                Debug.LogError(
                    "Embedding API failed."
                );

                onComplete?.Invoke(
                    QueryResult.NoMatch()
                );

                yield break;
            }

            if (lastEmbedding.Length != 384)
            {
                Debug.LogError(
                    $"Unexpected embedding size: " +
                    $"{lastEmbedding.Length}. Expected 384."
                );

                onComplete?.Invoke(
                    QueryResult.NoMatch()
                );

                yield break;
            }

            float[] queryEmbedding = lastEmbedding;




            // Verificá que el embedding no sea todo zeros
            float embSum = 0f;
            foreach (var v in queryEmbedding) embSum += Mathf.Abs(v);
            Debug.Log($"Suma del query embedding: {embSum}");


            // 3. Buscar la mejor entrada
            KnowledgeEntry best = null;
            float bestScore = 0f;


            List<QuestionCompared> questionsOrdered = new List<QuestionCompared>();

            foreach (var entry in allEntries)
            {
                if (entry.chapterRequired > gameProgress.CurrentChapter)
                    continue;

                float responseSimilarity =
                    CosineSimilarity(
                        queryEmbedding,
                        entry.embeddingCache
                    );


                foreach (var question in entry.questions)
                {
                    float similarity =
                        CosineSimilarity(
                            queryEmbedding,
                            question.embeddingCache
                        );

                    questionsOrdered.Add(new QuestionCompared
                    {
                        matchedEntry = entry,
                        questionScore = similarity,
                        responseScore = responseSimilarity
                    });
                }

                /*

                    float combined =
                        bestQuestionSimilarity * questionWeight +
                        responseSimilarity * responseWeight;

                    float cacheSum = 0f;
                    foreach (var v in entry.embeddingCache) cacheSum += Mathf.Abs(v);

                    Debug.Log($"[{entry.entryId}] cacheSum: {cacheSum:F3} | question: {bestQuestionSimilarity:F3} | response: {responseSimilarity:F3} | combined: {combined:F3}");

                    if (combined > bestScore)
                    {
                        bestScore = combined;
                        best = entry;
                    }
                }
                */
            }

            questionsOrdered.Sort((a, b) => b.questionScore.CompareTo(a.questionScore));

            bestScore = questionsOrdered.Count > 0 ? questionsOrdered[0].questionScore * questionWeight + questionsOrdered[0].responseScore * responseWeight : 0f;
            best = questionsOrdered.Count > 0 ? questionsOrdered[0].matchedEntry : null;

            Debug.Log("question score: " + questionsOrdered[0].questionScore);
            Debug.Log("response score: " + questionsOrdered[0].responseScore);
            Debug.Log("best score: " + bestScore);

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




        public IEnumerator GetEmbeddingFromApiCoroutine(
    string text)
        {
            _embeddingReady = false;
            lastEmbedding = null;

            string normalized =
                NormalizeText(text);

            EmbeddingRequest requestData =
                new EmbeddingRequest
                {
                    inputs = normalized
                };

            string json =
                JsonUtility.ToJson(requestData);

            byte[] bodyRaw =
                Encoding.UTF8.GetBytes(json);

            using UnityWebRequest request =
                new UnityWebRequest(
                    EmbeddingApiUrl,
                    UnityWebRequest.kHttpVerbPOST
                );

            request.uploadHandler =
                new UploadHandlerRaw(bodyRaw);

            request.downloadHandler =
                new DownloadHandlerBuffer();

            request.SetRequestHeader(
                "Content-Type",
                "application/json"
            );

            yield return request.SendWebRequest();


            if (
                request.result !=
                UnityWebRequest.Result.Success)
            {
                Debug.LogError(
                    "Embedding API error: " +
                    request.responseCode +
                    "\n" +
                    request.error +
                    "\n" +
                    request.downloadHandler.text
                );

                yield break;
            }


            string responseJson =
                request.downloadHandler.text;

            lastEmbedding =
                ParseEmbedding(responseJson);


            if (lastEmbedding == null)
            {
                Debug.LogError(
                    "Could not parse embedding."
                );

                yield break;
            }


            Debug.Log(
                $"Remote embedding dimensions: " +
                $"{lastEmbedding.Length}"
            );

            _embeddingReady = true;
        }
        private float[] ParseEmbedding(string json)
        {
            json = json.Trim();

            // Algunas respuestas pueden venir como [[...]]
            if (json.StartsWith("[["))
            {
                json = json.Substring(1, json.Length - 2);
            }

            if (!json.StartsWith("[") ||
                !json.EndsWith("]"))
            {
                Debug.LogError(
                    "Unexpected embedding JSON: " + json
                );

                return null;
            }

            json =
                json.Substring(
                    1,
                    json.Length - 2
                );

            string[] values =
                json.Split(',');

            float[] embedding =
                new float[values.Length];

            for (int i = 0; i < values.Length; i++)
            {
                if (!float.TryParse(
                    values[i],
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out embedding[i]))
                {
                    Debug.LogError(
                        $"Could not parse embedding value {i}: " +
                        values[i]
                    );

                    return null;
                }
            }

            return embedding;
        }
    }
}