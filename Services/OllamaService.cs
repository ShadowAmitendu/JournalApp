using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace JournalApp
{
    /// <summary>
    /// Communicates with a locally running Ollama instance via its REST API.
    /// Default endpoint: http://localhost:11434
    /// </summary>
    public class OllamaService
    {
        private static OllamaService _instance;
        public static OllamaService Instance => _instance ??= new OllamaService();

        private readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        private HttpClient _streamHttp = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };

        public string BaseUrl { get; set; } = "http://localhost:11434";

        // ── Connection Health ─────────────────────────────────────────────────

        /// <summary>Returns true if Ollama is running and reachable at the configured BaseUrl.</summary>
        public async Task<bool> IsRunningAsync()
        {
            try
            {
                var resp = await _http.GetAsync($"{BaseUrl}/api/tags");
                return resp.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        // ── Model Discovery ───────────────────────────────────────────────────

        /// <summary>
        /// Returns the names of all locally installed Ollama models.
        /// </summary>
        public async Task<List<string>> GetAvailableModelsAsync()
        {
            try
            {
                var resp = await _http.GetAsync($"{BaseUrl}/api/tags");
                resp.EnsureSuccessStatusCode();
                var json = await resp.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                var models = new List<string>();
                if (doc.RootElement.TryGetProperty("models", out var modelsEl))
                {
                    foreach (var m in modelsEl.EnumerateArray())
                    {
                        if (m.TryGetProperty("name", out var nameEl))
                        {
                            models.Add(nameEl.GetString());
                        }
                    }
                }
                return models;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[OllamaService] GetModels error: {ex.Message}");
                return new List<string>();
            }
        }

        // ── Streaming Chat ────────────────────────────────────────────────────

        /// <summary>
        /// Sends a chat request to Ollama and streams response tokens via the <paramref name="onToken"/> callback.
        /// </summary>
        /// <param name="model">Model name, e.g. "llama3", "mistral", "phi3"</param>
        /// <param name="systemPrompt">System-level instructions for the model</param>
        /// <param name="userPrompt">The user's actual input / entry text</param>
        /// <param name="onToken">Callback invoked with each streamed text fragment</param>
        /// <param name="ct">Cancellation token</param>
        public async Task StreamChatAsync(
            string model,
            string systemPrompt,
            string userPrompt,
            Action<string> onToken,
            CancellationToken ct = default)
        {
            var payload = new
            {
                model = model,
                stream = true,
                messages = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user",   content = userPrompt   }
                }
            };

            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/api/chat") { Content = content };
            using var response = await _streamHttp.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var reader = new System.IO.StreamReader(stream, Encoding.UTF8);

            while (!ct.IsCancellationRequested)
            {
                string line = await reader.ReadLineAsync(ct);
                if (line == null) break;

                line = line.Trim();
                if (string.IsNullOrEmpty(line))
                    continue;

                try
                {
                    using var doc = JsonDocument.Parse(line);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("message", out var msgEl) &&
                        msgEl.TryGetProperty("content", out var contentEl))
                    {
                        var token = contentEl.GetString();
                        if (!string.IsNullOrEmpty(token))
                            onToken(token);
                    }

                    // Check for terminal "done" flag
                    if (root.TryGetProperty("done", out var doneEl) && doneEl.GetBoolean())
                        return;
                }
                catch (JsonException)
                {
                    // If parse fails, ignore
                }
            }
        }

        // ── Preset System Prompts ─────────────────────────────────────────────

        public static class Prompts
        {
            public const string ContinueWriting =
                "You are a thoughtful journaling assistant. Continue the user's journal entry naturally in their own writing style. Write 2–3 paragraphs that feel authentic and personal. Do not add any heading or prefix.";

            public const string Summarize =
                "Summarize the following journal entry into exactly 5 concise bullet points. Each bullet should capture a key thought, feeling, or event from the entry.";

            public const string Rewrite =
                "Rewrite the following text in a clear, thoughtful, and eloquent style. Preserve the meaning and personal voice, but improve clarity and flow. Output only the rewritten text.";

            public const string WritingPrompt =
                "Generate a single deep, introspective, and emotionally engaging journaling prompt. Output only the prompt itself — no explanation, no prefix, no quotation marks.";

            public const string AnalyzeMood =
                "Analyze the emotional tone and mood of the following journal entry. Identify the dominant mood, any secondary emotions present, and provide a brief 2–3 sentence insight into what the author might be feeling or processing.";

            public const string Translate =
                "Translate the following text into English. Output only the translated text, with no explanation or prefix.";
        }
    }
}
