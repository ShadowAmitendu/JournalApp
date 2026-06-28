// Services/TFIDFSearch.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace JournalApp
{
    public static class TFIDFSearch
    {
        private static readonly HashSet<string> StopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "i", "me", "my", "myself", "we", "our", "ours", "ourselves", "you", "your", "yours", "yourself",
            "yourselves", "he", "him", "his", "himself", "she", "her", "hers", "herself", "it", "its", "itself",
            "they", "them", "their", "theirs", "themselves", "what", "which", "who", "whom", "this", "that",
            "these", "those", "am", "is", "are", "was", "were", "be", "been", "being", "have", "has", "had",
            "having", "do", "does", "did", "doing", "a", "an", "the", "and", "but", "if", "or", "because",
            "as", "until", "while", "of", "at", "by", "for", "with", "about", "against", "between", "into",
            "through", "during", "before", "after", "above", "below", "to", "from", "up", "down", "in", "out",
            "on", "off", "over", "under", "again", "further", "then", "once", "here", "there", "when", "where",
            "why", "how", "all", "any", "both", "each", "few", "more", "most", "other", "some", "such", "no",
            "nor", "not", "only", "own", "same", "so", "than", "too", "very", "s", "t", "can", "will", "just",
            "don", "should", "now"
        };

        public static List<string> Tokenize(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return new List<string>();
            var cleanText = Regex.Replace(text.ToLowerInvariant(), @"[^\w\s]", " ");
            return cleanText.Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                            .Where(w => !StopWords.Contains(w))
                            .ToList();
        }

        public static List<(JournalNote note, double score)> RankNotes(string query, List<(JournalNote note, string text)> documents, int maxResults = 4)
        {
            var results = new List<(JournalNote note, double score)>();
            if (string.IsNullOrWhiteSpace(query) || documents == null || documents.Count == 0) return results;

            var queryTokens = Tokenize(query);
            if (queryTokens.Count == 0) return results;

            var df = new Dictionary<string, int>();
            var docTokens = new List<List<string>>();

            foreach (var doc in documents)
            {
                var titleTokens = Tokenize(doc.note.Title);
                var textTokens = Tokenize(doc.text);
                
                var combined = new List<string>();
                combined.AddRange(titleTokens);
                combined.AddRange(titleTokens); // Boost title importance by adding it twice
                combined.AddRange(textTokens);

                docTokens.Add(combined);

                var uniqueInDoc = new HashSet<string>(combined);
                foreach (var term in uniqueInDoc)
                {
                    if (!df.ContainsKey(term)) df[term] = 0;
                    df[term]++;
                }
            }

            int N = documents.Count;

            var queryTf = new Dictionary<string, double>();
            foreach (var term in queryTokens)
            {
                if (!queryTf.ContainsKey(term)) queryTf[term] = 0;
                queryTf[term]++;
            }

            var queryVector = new Dictionary<string, double>();
            double queryNorm = 0;
            foreach (var pair in queryTf)
            {
                string term = pair.Key;
                double tf = pair.Value / queryTokens.Count;
                df.TryGetValue(term, out int docFreq);
                double idf = Math.Log(1.0 + (double)N / (1.0 + docFreq));
                double tfidf = tf * idf;
                queryVector[term] = tfidf;
                queryNorm += tfidf * tfidf;
            }
            queryNorm = Math.Sqrt(queryNorm);

            for (int i = 0; i < N; i++)
            {
                var doc = documents[i].note;
                var tokens = docTokens[i];
                if (tokens.Count == 0) continue;

                var docTf = new Dictionary<string, double>();
                foreach (var term in tokens)
                {
                    if (!docTf.ContainsKey(term)) docTf[term] = 0;
                    docTf[term]++;
                }

                var docVector = new Dictionary<string, double>();
                double docNorm = 0;
                foreach (var pair in docTf)
                {
                    string term = pair.Key;
                    double tf = pair.Value / tokens.Count;
                    df.TryGetValue(term, out int docFreq);
                    double idf = Math.Log(1.0 + (double)N / (1.0 + docFreq));
                    double tfidf = tf * idf;
                    docVector[term] = tfidf;
                    docNorm += tfidf * tfidf;
                }
                docNorm = Math.Sqrt(docNorm);

                double dotProduct = 0;
                foreach (var pair in queryVector)
                {
                    string term = pair.Key;
                    if (docVector.TryGetValue(term, out double docVal))
                    {
                        dotProduct += pair.Value * docVal;
                    }
                }

                double similarity = 0;
                if (queryNorm > 0 && docNorm > 0)
                {
                    similarity = dotProduct / (queryNorm * docNorm);
                }

                // Small tag boost
                foreach (var term in queryTokens)
                {
                    if (doc.Tags != null && doc.Tags.Any(t => t.Equals(term, StringComparison.OrdinalIgnoreCase)))
                        similarity += 0.15;
                    if (doc.Category != null && doc.Category.Contains(term, StringComparison.OrdinalIgnoreCase))
                        similarity += 0.05;
                }

                if (similarity > 0)
                {
                    results.Add((doc, similarity));
                }
            }

            return results.OrderByDescending(r => r.score).Take(maxResults).ToList();
        }
    }
}
