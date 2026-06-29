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
        private bool _isResizingAIPanel = false;
        private int _aiActionRequestId = 0;
        private double _resizeStartWidth = 0;
        private double _resizeStartPointerX = 0;
        private double _aiPanelWidth = 340;

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
            double startWidth = AIAssistantPanel.Width;
            if (double.IsNaN(startWidth) || AIAssistantPanel.Visibility == Visibility.Collapsed)
            {
                startWidth = AIAssistantPanel.ActualWidth;
            }
            if (double.IsNaN(startWidth) || startWidth <= 0 || AIAssistantPanel.Visibility == Visibility.Collapsed)
            {
                startWidth = 0;
            }
            double endWidth = open ? _aiPanelWidth : 0;

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
                Duration = new Duration(TimeSpan.FromMilliseconds(180)),
                EasingFunction = new Microsoft.UI.Xaml.Media.Animation.CubicEase { EasingMode = Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseOut },
                FillBehavior = Microsoft.UI.Xaml.Media.Animation.FillBehavior.Stop
            };

            _aiPanelStoryboard = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
            _aiPanelStoryboard.Children.Add(animation);

            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(animation, AIAssistantPanel);
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(animation, "Width");

            _aiPanelStoryboard.Completed += (s, e) =>
            {
                if (open)
                {
                    AIAssistantPanel.Width = _aiPanelWidth;
                }
                else
                {
                    // Hide the panel once the close animation is done and reset margin
                    AIAssistantPanel.Visibility = Visibility.Collapsed;
                    AIAssistantPanel.Margin = new Thickness(0);
                    AIAssistantPanel.Width = 0;
                }
            };

            _aiPanelStoryboard.Begin();
        }

        private void AIPanelSplitter_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && AIAssistantPanel != null)
            {
                _isResizingAIPanel = true;
                _resizeStartWidth = AIAssistantPanel.Width;
                if (double.IsNaN(_resizeStartWidth) || _resizeStartWidth <= 0)
                {
                    _resizeStartWidth = AIAssistantPanel.ActualWidth;
                }
                if (_resizeStartWidth <= 0)
                {
                    _resizeStartWidth = _aiPanelWidth;
                }

                var pt = e.GetCurrentPoint(this);
                _resizeStartPointerX = pt.Position.X;
                fe.CapturePointer(e.Pointer);
                e.Handled = true;
            }
        }

        private void AIPanelSplitter_PointerMoved(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (_isResizingAIPanel && AIAssistantPanel != null)
            {
                var pt = e.GetCurrentPoint(this);
                double currentX = pt.Position.X;
                double deltaX = currentX - _resizeStartPointerX;

                double newWidth = _resizeStartWidth - deltaX;

                // Constrain the width between 240 and 600
                if (newWidth < 240) newWidth = 240;
                if (newWidth > 600) newWidth = 600;

                _aiPanelWidth = newWidth;
                AIAssistantPanel.Width = newWidth;
                e.Handled = true;
            }
        }

        private void AIPanelSplitter_PointerReleased(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (_isResizingAIPanel)
            {
                _isResizingAIPanel = false;
                if (sender is FrameworkElement fe)
                {
                    fe.ReleasePointerCapture(e.Pointer);
                }
                this.ProtectedCursor = null;
                e.Handled = true;
            }
        }

        private void AIPanelSplitter_PointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            this.ProtectedCursor = Microsoft.UI.Input.InputSystemCursor.Create(Microsoft.UI.Input.InputSystemCursorShape.SizeWestEast);
        }

        private void AIPanelSplitter_PointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (!_isResizingAIPanel)
            {
                this.ProtectedCursor = null;
            }
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
                TextWrapping = TextWrapping.Wrap,
                FontSize = 13,
                LineHeight = 18,
                Foreground = isUser 
                    ? new SolidColorBrush(Microsoft.UI.Colors.White) 
                    : GetThemeBrush("TextFillColorPrimaryBrush", "#000000"),
                IsTextSelectionEnabled = true
            };

            if (isUser)
            {
                textBlock.Text = text;
                bubbleBorder.Child = textBlock;
            }
            else
            {
                ParseMarkdownToInlines(text, textBlock);

                var panel = new StackPanel { Spacing = 4 };
                panel.Children.Add(textBlock);

                var insertButton = new HyperlinkButton
                {
                    Content = "⬇ Insert into Note",
                    FontSize = 11,
                    Padding = new Thickness(0),
                    Margin = new Thickness(0, 4, 0, 0),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Tag = text
                };

                insertButton.Click += (s, e) =>
                {
                    if (s is HyperlinkButton btn && btn.Tag is string t && !string.IsNullOrEmpty(t))
                    {
                        InsertTextIntoEditor(t);
                    }
                };

                panel.Children.Add(insertButton);
                bubbleBorder.Child = panel;
            }

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
            if (lastChild is Border bubbleBorder)
            {
                TextBlock textBlock = null;
                HyperlinkButton insertButton = null;

                if (bubbleBorder.Child is TextBlock tb)
                {
                    textBlock = tb;
                }
                else if (bubbleBorder.Child is StackPanel sp)
                {
                    textBlock = sp.Children.FirstOrDefault() as TextBlock;
                    insertButton = sp.Children.LastOrDefault() as HyperlinkButton;
                }

                if (textBlock != null)
                {
                    if (string.IsNullOrEmpty(text))
                    {
                        textBlock.Inlines.Clear();
                        textBlock.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = "Thinking..." });
                        if (addCursor)
                        {
                            textBlock.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = " █" });
                        }
                    }
                    else
                    {
                        ParseMarkdownToInlines(text, textBlock);
                        if (addCursor)
                        {
                            textBlock.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = " █" });
                        }
                    }

                    if (insertButton != null)
                    {
                        insertButton.Tag = text;
                    }
                }
            }

            // Auto-scroll to bottom
            if (AIChatScrollViewer != null)
            {
                AIChatScrollViewer.ChangeView(null, AIChatScrollViewer.ScrollableHeight, null);
            }
        }

        private void ParseMarkdownToInlines(string markdownText, TextBlock textBlock)
        {
            textBlock.Inlines.Clear();
            if (string.IsNullOrEmpty(markdownText)) return;

            int index = 0;
            int len = markdownText.Length;

            while (index < len)
            {
                // Check code block / inline code
                if (markdownText[index] == '`')
                {
                    int nextBacktick = markdownText.IndexOf('`', index + 1);
                    if (nextBacktick > index)
                    {
                        string codeText = markdownText.Substring(index + 1, nextBacktick - index - 1);
                        var run = new Microsoft.UI.Xaml.Documents.Run
                        {
                            Text = codeText,
                            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
                            Foreground = GetThemeBrush("AccentTextFillColorPrimaryBrush", "#0078D4")
                        };
                        textBlock.Inlines.Add(run);
                        index = nextBacktick + 1;
                        continue;
                    }
                }

                // Check bold + italic (***)
                if (index + 2 < len && markdownText.Substring(index, 3) == "***")
                {
                    int nextTriple = markdownText.IndexOf("***", index + 3);
                    if (nextTriple > index)
                    {
                        string innerText = markdownText.Substring(index + 3, nextTriple - index - 3);
                        var span = new Microsoft.UI.Xaml.Documents.Span();
                        var bold = new Microsoft.UI.Xaml.Documents.Bold();
                        var italic = new Microsoft.UI.Xaml.Documents.Italic();
                        italic.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = innerText });
                        bold.Inlines.Add(italic);
                        span.Inlines.Add(bold);
                        textBlock.Inlines.Add(span);
                        index = nextTriple + 3;
                        continue;
                    }
                }

                // Check bold + italic (___)
                if (index + 2 < len && markdownText.Substring(index, 3) == "___")
                {
                    int nextTriple = markdownText.IndexOf("___", index + 3);
                    if (nextTriple > index)
                    {
                        string innerText = markdownText.Substring(index + 3, nextTriple - index - 3);
                        var span = new Microsoft.UI.Xaml.Documents.Span();
                        var bold = new Microsoft.UI.Xaml.Documents.Bold();
                        var italic = new Microsoft.UI.Xaml.Documents.Italic();
                        italic.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = innerText });
                        bold.Inlines.Add(italic);
                        span.Inlines.Add(bold);
                        textBlock.Inlines.Add(span);
                        index = nextTriple + 3;
                        continue;
                    }
                }

                // Check bold (**)
                if (index + 1 < len && markdownText.Substring(index, 2) == "**")
                {
                    int nextDouble = markdownText.IndexOf("**", index + 2);
                    if (nextDouble > index)
                    {
                        string innerText = markdownText.Substring(index + 2, nextDouble - index - 2);
                        var bold = new Microsoft.UI.Xaml.Documents.Bold();
                        bold.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = innerText });
                        textBlock.Inlines.Add(bold);
                        index = nextDouble + 2;
                        continue;
                    }
                }

                // Check bold (__)
                if (index + 1 < len && markdownText.Substring(index, 2) == "__")
                {
                    int nextDouble = markdownText.IndexOf("__", index + 2);
                    if (nextDouble > index)
                    {
                        string innerText = markdownText.Substring(index + 2, nextDouble - index - 2);
                        var bold = new Microsoft.UI.Xaml.Documents.Bold();
                        bold.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = innerText });
                        textBlock.Inlines.Add(bold);
                        index = nextDouble + 2;
                        continue;
                    }
                }

                // Check italic (*)
                if (markdownText[index] == '*')
                {
                    int nextSingle = markdownText.IndexOf('*', index + 1);
                    if (nextSingle > index)
                    {
                        string innerText = markdownText.Substring(index + 1, nextSingle - index - 1);
                        var italic = new Microsoft.UI.Xaml.Documents.Italic();
                        italic.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = innerText });
                        textBlock.Inlines.Add(italic);
                        index = nextSingle + 1;
                        continue;
                    }
                }

                // Check italic (_)
                if (markdownText[index] == '_')
                {
                    int nextSingle = markdownText.IndexOf('_', index + 1);
                    if (nextSingle > index)
                    {
                        string innerText = markdownText.Substring(index + 1, nextSingle - index - 1);
                        var italic = new Microsoft.UI.Xaml.Documents.Italic();
                        italic.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = innerText });
                        textBlock.Inlines.Add(italic);
                        index = nextSingle + 1;
                        continue;
                    }
                }

                // Default text character
                int nextSpecial = markdownText.IndexOfAny(new char[] { '*', '_', '`', '\n' }, index);
                if (nextSpecial == -1)
                {
                    textBlock.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = markdownText.Substring(index) });
                    break;
                }
                else
                {
                    if (nextSpecial > index)
                    {
                        textBlock.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = markdownText.Substring(index, nextSpecial - index) });
                    }
                    if (markdownText[nextSpecial] == '\n')
                    {
                        textBlock.Inlines.Add(new Microsoft.UI.Xaml.Documents.LineBreak());
                        index = nextSpecial + 1;
                    }
                    else
                    {
                        index = nextSpecial;
                    }
                }
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

            if (_aiCts != null) return; // A generation is already running!

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
            if (_aiCts != null)
            {
                _aiCts.Cancel();
                _aiCts.Dispose();
                _aiCts = null;
            }
            _aiCts = new CancellationTokenSource();
            int requestId = ++_aiActionRequestId;

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

            if (AIStopButton != null) AIStopButton.Visibility = Visibility.Visible;

            // Toggle side panel Send/Stop buttons
            if (AIChatSendButton != null) AIChatSendButton.Visibility = Visibility.Collapsed;
            if (AIChatSideStopButton != null) AIChatSideStopButton.Visibility = Visibility.Visible;

            var sw = System.Diagnostics.Stopwatch.StartNew();
            long lastUpdateMs = 0;
            string accumulated = string.Empty;

            try
            {
                await OllamaService.Instance.StreamChatAsync(
                    selectedModel,
                    systemPrompt,
                    userText,
                    token =>
                    {
                        accumulated += token;
                        long now = sw.ElapsedMilliseconds;
                        if (now - lastUpdateMs > 80)
                        {
                            lastUpdateMs = now;
                            string currentText = accumulated;
                            DispatcherQueue.TryEnqueue(() =>
                            {
                                if (requestId != _aiActionRequestId) return;
                                _aiLastResponse = currentText;
                                _activeAIText = _aiLastResponse;
                                UpdateLastChatMessage(_activeAIText, _cursorVisible);
                            });
                        }
                    },
                    _aiCts.Token);

                if (requestId != _aiActionRequestId) return;

                // Final render
                _aiLastResponse = accumulated;
                _activeAIText = _aiLastResponse;
                UpdateLastChatMessage(_activeAIText, addCursor: false);
            }
            catch (OperationCanceledException)
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (requestId != _aiActionRequestId) return;
                    StopCursorBlink();
                    _aiLastResponse = accumulated;
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
                    if (requestId != _aiActionRequestId) return;

                    if (AIStopButton != null) AIStopButton.Visibility = Visibility.Collapsed;
                    StopCursorBlink();

                    // Restore side panel Send/Stop buttons
                    if (AIChatSendButton != null) AIChatSendButton.Visibility = Visibility.Visible;
                    if (AIChatSideStopButton != null) AIChatSideStopButton.Visibility = Visibility.Collapsed;

                    if (isChatMode && !string.IsNullOrEmpty(_aiLastResponse))
                    {
                        _chatTranscript += _aiLastResponse;
                    }

                    if (_aiCts != null)
                    {
                        _aiCts.Dispose();
                        _aiCts = null;
                    }
                });
            }
        }

        private void AIStopButton_Click(object sender, RoutedEventArgs e)
        {
            _aiCts?.Cancel();
        }

        private void InsertTextIntoEditor(string textToInsert)
        {
            if (NoteRichEditBox == null || string.IsNullOrEmpty(textToInsert)) return;

            try
            {
                var doc = NoteRichEditBox.Document;
                doc.GetText(TextGetOptions.None, out string current);
                var insertText = (string.IsNullOrWhiteSpace(current) ? "" : "\n\n") + textToInsert;
                
                doc.Selection.TypeText(insertText);

                _isDirty = true;
                if (StatusMessageTextBlock != null) StatusMessageTextBlock.Text = "AI content inserted";
            }
            catch (Exception ex)
            {
                _ = ShowAlertAsync("Insert Error", $"Failed to insert text into editor: {ex.Message}");
            }
        }

        private void AIClearButton_Click(object sender, RoutedEventArgs e)
        {
            _aiActionRequestId++; // Invalidate any running streams
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
