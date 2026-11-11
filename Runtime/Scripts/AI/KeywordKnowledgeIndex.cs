using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Conkist.GDK.AI
{
    /// <summary>
    /// Pure C# keyword retrieval (BM25). No native code, no model, no network:
    /// it runs on every platform, including WebGL, where LLMUnity's embedding RAG does not.
    /// Use it for small knowledge bases (hundreds of paragraphs). Upgrade path: vector search
    /// with embeddings pre-computed in the Editor, if meaning-based matching is needed.
    /// </summary>
    public sealed class KeywordKnowledgeIndex
    {
        private const float K1 = 1.5f;
        private const float B = 0.75f;

        private readonly List<string> _docs = new List<string>();
        private readonly List<Dictionary<string, int>> _termFreqs = new List<Dictionary<string, int>>();
        private readonly List<int> _docLengths = new List<int>();
        private readonly Dictionary<string, int> _docFreq = new Dictionary<string, int>();
        private long _totalLength;

        public int Count => _docs.Count;

        public void Clear()
        {
            _docs.Clear();
            _termFreqs.Clear();
            _docLengths.Clear();
            _docFreq.Clear();
            _totalLength = 0;
        }

        public void Add(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;

            var tf = new Dictionary<string, int>();
            int length = 0;
            foreach (string term in Tokenize(text))
            {
                tf.TryGetValue(term, out int n);
                tf[term] = n + 1;
                length++;
            }
            if (length == 0) return;

            foreach (string term in tf.Keys)
            {
                _docFreq.TryGetValue(term, out int df);
                _docFreq[term] = df + 1;
            }

            _docs.Add(text);
            _termFreqs.Add(tf);
            _docLengths.Add(length);
            _totalLength += length;
        }

        /// <summary>Returns up to <paramref name="topK"/> documents, best first. Documents with no matching term are never returned.</summary>
        public string[] Search(string query, int topK)
        {
            if (_docs.Count == 0 || topK <= 0 || string.IsNullOrWhiteSpace(query)) return Array.Empty<string>();

            var terms = new HashSet<string>(Tokenize(query));
            if (terms.Count == 0) return Array.Empty<string>();

            float avgLength = (float)_totalLength / _docs.Count;
            var scored = new List<KeyValuePair<float, int>>();

            for (int i = 0; i < _docs.Count; i++)
            {
                float score = 0f;
                foreach (string term in terms)
                {
                    if (!_termFreqs[i].TryGetValue(term, out int f)) continue;
                    int df = _docFreq[term];
                    float idf = (float)Math.Log(1.0 + (_docs.Count - df + 0.5) / (df + 0.5));
                    score += idf * (f * (K1 + 1f)) / (f + K1 * (1f - B + B * _docLengths[i] / avgLength));
                }
                if (score > 0f) scored.Add(new KeyValuePair<float, int>(score, i));
            }

            scored.Sort((a, b) => b.Key.CompareTo(a.Key));

            int count = Math.Min(topK, scored.Count);
            var result = new string[count];
            for (int i = 0; i < count; i++) result[i] = _docs[scored[i].Value];
            return result;
        }

        // Lowercase, strip accents (pt-BR: "ação" matches "acao"), split on non-alphanumerics,
        // drop 1-char tokens, fold a plural "s" so "animações" matches "animação".
        private static IEnumerable<string> Tokenize(string text)
        {
            string folded = RemoveDiacritics(text).ToLowerInvariant();
            var sb = new StringBuilder();
            for (int i = 0; i <= folded.Length; i++)
            {
                char c = i < folded.Length ? folded[i] : ' ';
                if (char.IsLetterOrDigit(c))
                {
                    sb.Append(c);
                    continue;
                }
                if (sb.Length > 1)
                {
                    if (sb.Length > 3 && sb[sb.Length - 1] == 's') sb.Length--;
                    yield return sb.ToString();
                }
                sb.Length = 0;
            }
        }

        private static string RemoveDiacritics(string text)
        {
            string decomposed = text.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(decomposed.Length);
            foreach (char c in decomposed)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
