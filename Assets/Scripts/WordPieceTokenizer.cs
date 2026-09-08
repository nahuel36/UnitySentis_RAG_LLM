using System.Collections.Generic;

public class WordPieceTokenizer
{
    private readonly Dictionary<string, int> _vocab;
    private const int ClsTokenId = 101;
    private const int SepTokenId = 102;
    private const int UnkTokenId = 100;

    public WordPieceTokenizer(string vocabText)
    {
        _vocab = new Dictionary<string, int>();
        var lines = vocabText.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            var token = lines[i].Trim();
            if (!string.IsNullOrEmpty(token))
                _vocab[token] = i;
        }
    }

    public List<int> Tokenize(string text, int maxLength = 128)
    {
        var result = new List<int> { ClsTokenId };

        // Normalizar: minúsculas, quitar acentos básicos
        text = text.ToLowerInvariant().Trim();

        // Separar por espacios y puntuación
        var words = SplitOnPunctuation(text);

        foreach (var word in words)
        {
            var wordPieceIds = TokenizeWord(word);
            foreach (var id in wordPieceIds)
            {
                if (result.Count >= maxLength - 1) break;
                result.Add(id);
            }
            if (result.Count >= maxLength - 1) break;
        }

        result.Add(SepTokenId);
        return result;
    }

    private List<int> TokenizeWord(string word)
    {
        var subTokens = new List<int>();
        int start = 0;

        while (start < word.Length)
        {
            int end = word.Length;
            string curSubstr = null;
            bool found = false;

            while (start < end)
            {
                string substr = word.Substring(start, end - start);
                if (start > 0) substr = "##" + substr;

                if (_vocab.TryGetValue(substr, out int id))
                {
                    curSubstr = substr;
                    subTokens.Add(id);
                    found = true;
                    break;
                }
                end--;
            }

            if (!found)
            {
                subTokens.Add(UnkTokenId);
                break;
            }

            start += (start > 0 ? curSubstr.Length - 2 : curSubstr.Length);
        }

        return subTokens;
    }

    private List<string> SplitOnPunctuation(string text)
    {
        var words = new List<string>();
        var current = new System.Text.StringBuilder();

        foreach (char c in text)
        {
            if (char.IsWhiteSpace(c))
            {
                if (current.Length > 0)
                {
                    words.Add(current.ToString());
                    current.Clear();
                }
            }
            else if (char.IsPunctuation(c) || char.IsSymbol(c))
            {
                if (current.Length > 0)
                {
                    words.Add(current.ToString());
                    current.Clear();
                }
                words.Add(c.ToString());
            }
            else
            {
                current.Append(c);
            }
        }

        if (current.Length > 0)
            words.Add(current.ToString());

        return words;
    }
}
