using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Text;
using Windows.UI;

namespace JournalApp
{
    public sealed partial class MainPage
    {
        // ── AI Panel State ────────────────────────────────────────────────────

        private CancellationTokenSource _aiCts;
        private bool _aiPanelOpen = false;
        private string _aiLastResponse = string.Empty;
        private string _chatTranscript = string.Empty;

        // ── Panel Toggle ──────────────────────────────────────────────────────

        private async void AIAssistantButton_Click(object sender, RoutedEventArgs e)
        {
            _aiPanelOpen = !_aiPanelOpen;
            if (AIAssistantPanel != null)
            {
                AIAssistantPanel.Visibility = _aiPanelOpen ? Visibility.Visible : Visibility.Collapsed;
            }

            if (_aiPanelOpen)
            {
                await RefreshOllamaStatusAsync();
            }
        }

        // ── Status & Model Refresh ────────────────────────────────────────────

        private async Task RefreshOllamaStatusAsync()
        {
            if (OllamaStatusIcon == null || OllamaStatusText == null) return;

            OllamaStatusText.Text = "Checking…";

            bool running = await OllamaService.Instance.IsRunningAsync();

            if (running)
            {
                OllamaStatusIcon.Glyph = "\uE73E";   // Check mark
                OllamaStatusIcon.Foreground = new SolidColorBrush(Color.FromArgb(255, 76, 175, 80));
                OllamaStatusText.Text = "Ollama Connected";

                var models = await OllamaService.Instance.GetAvailableModelsAsync();
                if (AIModelCombo != null)
                {
                    AIModelCombo.Items.Clear();
                    if (models.Count == 0)
                    {
                        AIModelCombo.Items.Add("(no models installed)");
                    }
                    else
                    {
                        foreach (var m in models)
                            AIModelCombo.Items.Add(m);
                    }
                    AIModelCombo.SelectedIndex = 0;
                }
            }
            else
            {
                OllamaStatusIcon.Glyph = "\uE711";   // Error / X
                OllamaStatusIcon.Foreground = new SolidColorBrush(Color.FromArgb(255, 229, 57, 53));
                OllamaStatusText.Text = "Ollama Not Detected";

                if (AIModelCombo != null)
                {
                    AIModelCombo.Items.Clear();
                    AIModelCombo.Items.Add("Install Ollama first");
                    AIModelCombo.SelectedIndex = 0;
                }
            }
        }

        // ── Action Buttons ────────────────────────────────────────────────────

        private async void AIContinueButton_Click(object sender, RoutedEventArgs e)
            => await RunAIActionAsync(OllamaService.Prompts.ContinueWriting, useFullNote: true);

        private async void AISummarizeButton_Click(object sender, RoutedEventArgs e)
            => await RunAIActionAsync(OllamaService.Prompts.Summarize, useFullNote: true);

        private async void AIRewriteButton_Click(object sender, RoutedEventArgs e)
            => await RunAIActionAsync(OllamaService.Prompts.Rewrite, useFullNote: false);

        private async void AIPromptButton_Click(object sender, RoutedEventArgs e)
            => await RunAIActionAsync(OllamaService.Prompts.WritingPrompt, useFullNote: false, promptOverride: " ");

        private async void AIMoodButton_Click(object sender, RoutedEventArgs e)
            => await RunAIActionAsync(OllamaService.Prompts.AnalyzeMood, useFullNote: true);

        private async void AITranslateButton_Click(object sender, RoutedEventArgs e)
            => await RunAIActionAsync(OllamaService.Prompts.Translate, useFullNote: false);

        // ── Core Streaming Runner ─────────────────────────────────────────────

        private async Task RunAIActionAsync(string systemPrompt, bool useFullNote, string promptOverride = null, bool isChatMode = false)
        {
            if (AIResponseBox == null) return;

            string selectedModel = AIModelCombo?.SelectedItem as string ?? string.Empty;
            if (string.IsNullOrWhiteSpace(selectedModel) || selectedModel.StartsWith("(") || selectedModel.StartsWith("Install"))
            {
                await ShowAlertAsync("No Model Selected", "Please ensure Ollama is running and a model is selected.");
                return;
            }

            // Collect user text
            string userText = promptOverride;
            if (userText == null)
            {
                if (NoteRichEditBox == null)
                {
                    userText = string.Empty;
                }
                else if (!useFullNote)
                {
                    // Use selected text, fall back to full note
                    NoteRichEditBox.Document.Selection.GetText(TextGetOptions.None, out string sel);
                    userText = string.IsNullOrWhiteSpace(sel) ? GetFullNoteText() : sel;
                }
                else
                {
                    userText = GetFullNoteText();
                }
            }

            if (string.IsNullOrWhiteSpace(userText) && !isChatMode)
            {
                await ShowAlertAsync("Empty Entry", "Please write something in your journal entry first.");
                return;
            }

            // Cancel any running request
            _aiCts?.Cancel();
            _aiCts = new CancellationTokenSource();

            _aiLastResponse = string.Empty;
            if (!isChatMode)
            {
                _chatTranscript = string.Empty;
                AIResponseBox.Text = string.Empty;
            }

            AIStopButton.Visibility = Visibility.Visible;
            AIInsertButton.IsEnabled = false;

            try
            {
                await OllamaService.Instance.StreamChatAsync(
                    selectedModel,
                    systemPrompt,
                    userText,
                    token =>
                    {
                        // Must update UI on dispatcher thread
                        DispatcherQueue.TryEnqueue(() =>
                        {
                            _aiLastResponse += token;
                            if (isChatMode)
                            {
                                AIResponseBox.Text = _chatTranscript + _aiLastResponse;
                            }
                            else
                            {
                                AIResponseBox.Text = _aiLastResponse;
                            }

                            // Auto-scroll
                            if (AIResponseScrollViewer != null)
                            {
                                AIResponseScrollViewer.ChangeView(null, AIResponseScrollViewer.ScrollableHeight, null);
                            }
                        });
                    },
                    _aiCts.Token);
            }
            catch (OperationCanceledException)
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (!string.IsNullOrEmpty(_aiLastResponse))
                    {
                        if (isChatMode)
                        {
                            _chatTranscript += _aiLastResponse + "\n\n[Generation stopped]";
                            AIResponseBox.Text = _chatTranscript;
                        }
                        else
                        {
                            AIResponseBox.Text += "\n\n[Generation stopped]";
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                await ShowAlertAsync("AI Error", $"Could not connect to Ollama:\n{ex.Message}\n\nMake sure Ollama is running: ollama serve");
            }
            finally
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    AIStopButton.Visibility = Visibility.Collapsed;
                    if (isChatMode && !string.IsNullOrEmpty(_aiLastResponse))
                    {
                        _chatTranscript += _aiLastResponse;
                    }
                    AIInsertButton.IsEnabled = !string.IsNullOrEmpty(_aiLastResponse);
                });
            }
        }

        private void AIStopButton_Click(object sender, RoutedEventArgs e)
        {
            _aiCts?.Cancel();
        }

        private void AIInsertButton_Click(object sender, RoutedEventArgs e)
        {
            if (NoteRichEditBox == null || string.IsNullOrEmpty(_aiLastResponse)) return;

            // Move caret to end and append with a blank line separator
            var doc = NoteRichEditBox.Document;
            doc.GetText(TextGetOptions.None, out string current);
            var insertText = (string.IsNullOrWhiteSpace(current) ? "" : "\n\n") + _aiLastResponse;
            doc.Selection.StartPosition = current.Length;
            doc.Selection.EndPosition = current.Length;
            doc.Selection.TypeText(insertText);

            _isDirty = true;
            StatusMessageTextBlock.Text = "AI content inserted";
        }

        private void AIClearButton_Click(object sender, RoutedEventArgs e)
        {
            if (AIResponseBox != null) AIResponseBox.Text = string.Empty;
            _aiLastResponse = string.Empty;
            _chatTranscript = string.Empty;
            if (AIInsertButton != null) AIInsertButton.IsEnabled = false;
            if (AIChatInputBox != null) AIChatInputBox.Text = string.Empty;
        }

        // ── Chat Input Handlers ──────────────────────────────────────────────

        private async void AIChatSendButton_Click(object sender, RoutedEventArgs e)
        {
            await RunChatPromptAsync();
        }

        private async void AIChatInputBox_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                var shiftState = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift);
                if (shiftState == Windows.UI.Core.CoreVirtualKeyStates.None)
                {
                    e.Handled = true;
                    await RunChatPromptAsync();
                }
            }
        }

        private async Task RunChatPromptAsync()
        {
            if (AIChatInputBox == null) return;
            string userPrompt = AIChatInputBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(userPrompt)) return;

            AIChatInputBox.Text = string.Empty;

            if (string.IsNullOrEmpty(_chatTranscript))
            {
                _chatTranscript = $"User: {userPrompt}\n\nAI: ";
            }
            else
            {
                _chatTranscript += $"\n\nUser: {userPrompt}\n\nAI: ";
            }

            if (AIResponseBox != null) AIResponseBox.Text = _chatTranscript;

            // Enclose active journal context
            string contextNote = GetFullNoteText();
            string systemPrompt = "You are a helpful writing assistant. ";
            if (!string.IsNullOrEmpty(contextNote))
            {
                systemPrompt += $"Below is the content of the user's current journal entry. Answer the user's questions or help them write, edit, or analyze it.\n\nJournal Entry:\n{contextNote}";
            }
            else
            {
                systemPrompt += "Help the user write, edit, or brainstorm their journal entry.";
            }

            await RunAIActionAsync(systemPrompt, useFullNote: false, promptOverride: userPrompt, isChatMode: true);
        }

        private async void AICheckConnectionButton_Click(object sender, RoutedEventArgs e)
        {
            await RefreshOllamaStatusAsync();
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private string GetFullNoteText()
        {
            if (NoteRichEditBox == null) return string.Empty;
            NoteRichEditBox.Document.GetText(TextGetOptions.None, out string text);
            return text?.Trim() ?? string.Empty;
        }

        private static ScrollViewer FindScrollViewer(DependencyObject element)
        {
            if (element is ScrollViewer sv) return sv;
            int count = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(element);
            for (int i = 0; i < count; i++)
            {
                var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(element, i);
                var found = FindScrollViewer(child);
                if (found != null) return found;
            }
            return null;
        }

        private void OllamaUrlTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_isPageInitialized) return;
            UpdateSaveSettingsButtonState();
        }

        private async void SettingsTestOllamaButton_Click(object sender, RoutedEventArgs e)
        {
            if (SettingsOllamaStatusText == null) return;

            SettingsOllamaStatusText.Text = "Testing connection...";
            
            string currentUrl = OllamaUrlTextBox?.Text?.Trim() ?? "http://localhost:11434";
            string prevUrl = OllamaService.Instance.BaseUrl;
            try
            {
                OllamaService.Instance.BaseUrl = currentUrl;
                bool running = await OllamaService.Instance.IsRunningAsync();
                if (running)
                {
                    var models = await OllamaService.Instance.GetAvailableModelsAsync();
                    SettingsOllamaStatusText.Text = $"Success! Connected. Found {models.Count} model(s).";
                    SettingsOllamaStatusText.Foreground = new SolidColorBrush(Color.FromArgb(255, 76, 175, 80));
                    // Also refresh status on the side panel
                    await RefreshOllamaStatusAsync();
                }
                else
                {
                    SettingsOllamaStatusText.Text = "Failed. Connection refused or invalid response.";
                    SettingsOllamaStatusText.Foreground = new SolidColorBrush(Color.FromArgb(255, 229, 57, 53));
                }
            }
            catch (Exception ex)
            {
                SettingsOllamaStatusText.Text = $"Error: {ex.Message}";
                SettingsOllamaStatusText.Foreground = new SolidColorBrush(Color.FromArgb(255, 229, 57, 53));
            }
            finally
            {
                OllamaService.Instance.BaseUrl = prevUrl; // restore until saved
            }
        }
    }
}
