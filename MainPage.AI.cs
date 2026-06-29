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
        private Microsoft.UI.Xaml.Media.Animation.Storyboard _aiPanelStoryboard;
        private DispatcherTimer _cursorBlinkTimer;
        private bool _cursorVisible = true;
        private string _activeAIText = string.Empty;

        // ── Panel Toggle ──────────────────────────────────────────────────────

        private async void AIAssistantButton_Click(object sender, RoutedEventArgs e)
        {
            _aiPanelOpen = !_aiPanelOpen;
            AnimateAIPanel(_aiPanelOpen);

            if (_aiPanelOpen)
            {
                await RefreshOllamaStatusAsync();
            }
        }

        private void AnimateAIPanel(bool open)
        {
            if (AIAssistantPanel == null) return;

            // Stop any existing animation
            if (_aiPanelStoryboard != null)
            {
                _aiPanelStoryboard.Stop();
                _aiPanelStoryboard = null;
            }

            // We animate from the current width to the target width
            double startWidth = AIAssistantPanel.ActualWidth;
            if (AIAssistantPanel.Visibility == Visibility.Collapsed)
            {
                startWidth = 0;
            }
            double endWidth = open ? 340 : 0;

            // If we are opening, ensure it is visible first and set margin to 8px
            if (open)
            {
                AIAssistantPanel.Visibility = Visibility.Visible;
                AIAssistantPanel.Margin = new Thickness(8, 0, 0, 0);
            }

            var animation = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
            {
                From = startWidth,
                To = endWidth,
                Duration = new Duration(TimeSpan.FromMilliseconds(300)),
                EasingFunction = new Microsoft.UI.Xaml.Media.Animation.CubicEase { EasingMode = Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseOut }
            };

            _aiPanelStoryboard = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
            _aiPanelStoryboard.Children.Add(animation);

            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(animation, AIAssistantPanel);
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(animation, "Width");

            if (!open)
            {
                _aiPanelStoryboard.Completed += (s, e) =>
                {
                    // Hide the panel once the close animation is done and reset margin
                    AIAssistantPanel.Visibility = Visibility.Collapsed;
                    AIAssistantPanel.Margin = new Thickness(0);
                };
            }

            _aiPanelStoryboard.Begin();
        }

        private void AddChatMessage(string text, bool isUser)
        {
            if (AIChatPanel == null) return;

            // Hide placeholder on first message
            if (AIChatPlaceholder != null)
            {
                AIChatPlaceholder.Visibility = Visibility.Collapsed;
            }

            var bubbleBorder = new Border
            {
                CornerRadius = isUser ? new CornerRadius(12, 12, 0, 12) : new CornerRadius(12, 12, 12, 0),
                Padding = new Thickness(12, 8, 12, 8),
                Margin = isUser ? new Thickness(40, 2, 4, 2) : new Thickness(4, 2, 40, 2),
                HorizontalAlignment = isUser ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                Background = isUser 
                    ? new SolidColorBrush(GetColorFromHex("#0078D4")) // User accent color
                    : GetThemeBrush("CardBackgroundFillColorDefaultBrush", "#EFEFEF"),
                BorderBrush = isUser ? null : GetThemeBrush("CardStrokeColorDefaultBrush", "#CCCCCC"),
                BorderThickness = isUser ? new Thickness(0) : new Thickness(1)
            };

            var textBlock = new TextBlock
            {
                Text = text,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 13,
                LineHeight = 18,
                Foreground = isUser 
                    ? new SolidColorBrush(Microsoft.UI.Colors.White) 
                    : GetThemeBrush("TextFillColorPrimaryBrush", "#000000"),
                IsTextSelectionEnabled = true
            };

            bubbleBorder.Child = textBlock;
            AIChatPanel.Children.Add(bubbleBorder);

            // Auto-scroll to bottom
            if (AIChatScrollViewer != null)
            {
                AIChatScrollViewer.ChangeView(null, AIChatScrollViewer.ScrollableHeight, null);
            }
        }

        private void UpdateLastChatMessage(string text, bool addCursor = false)
        {
            if (AIChatPanel == null || AIChatPanel.Children.Count == 0) return;

            var lastChild = AIChatPanel.Children.LastOrDefault();
            if (lastChild is Border bubbleBorder && bubbleBorder.Child is TextBlock textBlock)
            {
                textBlock.Text = string.IsNullOrEmpty(text) ? "Thinking..." : (text + (addCursor ? " █" : ""));
            }

            // Auto-scroll to bottom
            if (AIChatScrollViewer != null)
            {
                AIChatScrollViewer.ChangeView(null, AIChatScrollViewer.ScrollableHeight, null);
            }
        }

        private void StartCursorBlink()
        {
            if (_cursorBlinkTimer == null)
            {
                _cursorBlinkTimer = new DispatcherTimer();
                _cursorBlinkTimer.Interval = TimeSpan.FromMilliseconds(500);
                _cursorBlinkTimer.Tick += (s, e) =>
                {
                    _cursorVisible = !_cursorVisible;
                    UpdateLastChatMessage(_activeAIText, _cursorVisible);
                };
            }
            _cursorVisible = true;
            _cursorBlinkTimer.Start();
        }

        private void StopCursorBlink()
        {
            _cursorBlinkTimer?.Stop();
            UpdateLastChatMessage(_activeAIText, addCursor: false);
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
            if (AIChatPanel == null) return;

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

            // Determine the text description for user bubble
            string actionText = "";
            if (isChatMode)
            {
                actionText = userText;
            }
            else
            {
                if (systemPrompt == OllamaService.Prompts.ContinueWriting) actionText = "Continue writing the current entry";
                else if (systemPrompt == OllamaService.Prompts.Summarize) actionText = "Summarize the current entry";
                else if (systemPrompt == OllamaService.Prompts.Rewrite) actionText = "Rewrite selection/entry";
                else if (systemPrompt == OllamaService.Prompts.WritingPrompt) actionText = "Give me a writing prompt";
                else if (systemPrompt == OllamaService.Prompts.AnalyzeMood) actionText = "Analyze the mood of this entry";
                else if (systemPrompt == OllamaService.Prompts.Translate) actionText = "Translate this selection/entry";
                else actionText = "AI Action";
            }

            // Cancel any running request
            _aiCts?.Cancel();
            _aiCts = new CancellationTokenSource();

            _aiLastResponse = string.Empty;
            _activeAIText = string.Empty;

            if (!isChatMode)
            {
                _chatTranscript = string.Empty;
            }

            // Render the User bubble and the empty AI bubble
            AddChatMessage(actionText, isUser: true);
            AddChatMessage(string.Empty, isUser: false);
            StartCursorBlink();

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
                            _activeAIText = _aiLastResponse;
                            UpdateLastChatMessage(_activeAIText, _cursorVisible);
                        });
                    },
                    _aiCts.Token);
            }
            catch (OperationCanceledException)
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    StopCursorBlink();
                    if (!string.IsNullOrEmpty(_aiLastResponse))
                    {
                        _activeAIText = _aiLastResponse + " [Generation stopped]";
                        UpdateLastChatMessage(_activeAIText, addCursor: false);
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
                    StopCursorBlink();

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
            if (AIChatPanel != null)
            {
                AIChatPanel.Children.Clear();
                if (AIChatPlaceholder != null)
                {
                    AIChatPlaceholder.Visibility = Visibility.Visible;
                    AIChatPanel.Children.Add(AIChatPlaceholder);
                }
            }
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

        private async void AIChatInputBox_PreviewKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                var shift = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift);
                bool isShiftDown = (shift & Windows.UI.Core.CoreVirtualKeyStates.Down) == Windows.UI.Core.CoreVirtualKeyStates.Down;
                if (!isShiftDown)
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

            // Enclose context based on RAG setting
            string systemPrompt = "You are a helpful journal writing assistant. ";
            
            if (AIRagToggle != null && AIRagToggle.IsOn)
            {
                // Retrieve all un-deleted notes
                var activeNotes = JournalManager.Instance.Notes.Where(n => !n.IsDeleted).ToList();
                var noteDocs = new List<(JournalNote note, string text)>();
                foreach (var note in activeNotes)
                {
                    string txt = GetNotePlainText(note);
                    if (!string.IsNullOrWhiteSpace(txt))
                    {
                        noteDocs.Add((note, txt));
                    }
                }

                // Rank using custom TF-IDF matching
                var ranked = TFIDFSearch.RankNotes(userPrompt, noteDocs, maxResults: 4);

                if (ranked.Count > 0)
                {
                    systemPrompt += "Use the following highly relevant past entries from the user's journal history to answer their question, summarize themes, or analyze patterns. Refer to the entries by their date and title when mentioning them:\n\n";
                    foreach (var match in ranked)
                    {
                        systemPrompt += $"--- \n[Date: {match.note.DateCreated.ToString("yyyy-MM-dd")}, Title: {match.note.Title}]\n{GetNotePlainText(match.note)}\n---\n\n";
                    }
                }
                else
                {
                    systemPrompt += "No highly relevant historical entries were found matching the query. Answer the user generally or help them write.";
                }
            }
            else
            {
                // Current note only context
                string contextNote = GetFullNoteText();
                if (!string.IsNullOrEmpty(contextNote))
                {
                    systemPrompt += $"Below is the content of the user's current journal entry. Answer the user's questions or help them write, edit, or analyze it.\n\nJournal Entry:\n{contextNote}";
                }
                else
                {
                    systemPrompt += "Help the user write, edit, or brainstorm their journal entry.";
                }
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
