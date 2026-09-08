// QueryResult.cs

namespace ChatSystemWithSentis
{
    public readonly struct QueryResult
    {
        public readonly bool HasMatch;
        public readonly KnowledgeEntry Entry;
        public readonly float Score;
        public readonly MatchSource Source; // cómo se encontró la coincidencia

        private QueryResult(bool hasMatch, KnowledgeEntry entry, float score, MatchSource source)
        {
            HasMatch = hasMatch;
            Entry = entry;
            Score = score;
            Source = source;
        }

        // Factory para coincidencia encontrada
        public static QueryResult Match(KnowledgeEntry entry, float score, MatchSource source)
            => new QueryResult(true, entry, score, source);

        // Factory para sin coincidencia
        public static QueryResult NoMatch()
            => new QueryResult(false, null, 0f, MatchSource.None);

        // Cuánta confianza tenemos — útil para decidir si mostrar pista parcial
        public bool IsHighConfidence => Score >= 0.80f;
        public bool IsLowConfidence => Score < 0.60f;

        public override string ToString()
            => HasMatch
               ? $"[{Source}] {Entry.entryId} (score: {Score:F2})"
               : "[NoMatch]";
    }

    public enum MatchSource
    {
        None,
        SemanticOnly,    // solo embedding ganó
        IntentOnly,      // solo clasificador ganó
        Combined         // ambos contribuyeron
    }
}
