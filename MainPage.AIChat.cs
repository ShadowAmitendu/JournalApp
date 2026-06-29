using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace JournalApp
{
    public sealed partial class MainPage
    {
        // ── AI Chat Page State ────────────────────────────────────────────────
        private ObservableCollection<AIChatSession> _aiChatSessions = new ObservableCollection<AIChatSession>();
        private AIChatSession? _currentChatSession;
        private CancellationTokenSource? _aiChatPageCts;
        private DispatcherTimer? _chatPageCursorBlinkTimer;
        private bool _chatPageCursorVisible = true;
        private string _chatPageActiveAIText = string.Empty;

        // ── Initialize ────────────────────────────────────────────────────────
        private void InitializeAIChatPage()
        {
            if (AIChatSessionListView != null)
            {
                AIChatSessionListView.ItemsSource = _aiChatSessions;
            }

            // Populate models in the chat page model combo
            PopulateChatPageModels();

            // Select the most recent session if available
            if (_aiChatSessions.Count > 0)
            {
                if (AIChatSessionListView != null)
                {
                    AIChatSessionListView.SelectedIndex = 0;
                }
            }
            else
            {
                UpdateChatPageUIForNoSession();
            }
        }

        private async void PopulateChatPageModels()
        {
            if (AIChatPageModelCombo == null) return;

            AIChatPageModelCombo.Items.Clear();
            var models = await OllamaService.Instance.GetAvailableModelsAsync();
            foreach (var m in models)
            {
                AIChatPageModelCombo.Items.Add(m);
            }

            if (AIChatPageModelCombo.Items.Count > 0)
            {
                // Try to match Settings or side panel selection
                string defaultModel = AIModelCombo?.SelectedItem as string ?? string.Empty;
                if (!string.IsNullOrEmpty(defaultModel) && AIChatPageModelCombo.Items.Contains(defaultModel))
                {
                    AIChatPageModelCombo.SelectedItem = defaultModel;
                }
                else
                {
                    AIChatPageModelCombo.SelectedIndex = 0;
                }
            }
        }

        private void LoadAIChatSessions()
        {
            string path = System.IO.Path.Combine(JournalManager.Instance.DataDir, "aic_chats.json");
            if (System.IO.File.Exists(path))
            {
                try
                {
                    string json = System.IO.File.ReadAllText(path);
                    var list = System.Text.Json.JsonSerializer.Deserialize<List<AIChatSession>>(json);
                    if (list != null)
                    {
                        _aiChatSessions = new ObservableCollection<AIChatSession>(list.OrderByDescending(s => s.DateCreated));
                    }
                }
                catch
                {
                    _aiChatSessions = new ObservableCollection<AIChatSession>();
                }
            }
            else
            {
                _aiChatSessions = new ObservableCollection<AIChatSession>();
            }
        }

        private void SaveAIChatSessions()
        {
            try
            {
                string path = System.IO.Path.Combine(JournalManager.Instance.DataDir, "aic_chats.json");
                string json = System.Text.Json.JsonSerializer.Serialize(_aiChatSessions.ToList(), new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                System.IO.File.WriteAllText(path, json);
            }
            catch { }
        }

        // ── Navigation & Selection changed ───────────────────────────────────
        private void AIChatSessionListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (AIChatSessionListView == null) return;

            var selected = AIChatSessionListView.SelectedItem as AIChatSession;
            if (selected != null)
            {
                _currentChatSession = selected;
                LoadActiveChatSession();
            }
        }

        private void LoadActiveChatSession()
        {
            if (_currentChatSession == null || AIChatPageHistoryPanel == null || AIChatSessionTitleTextBox == null) return;

            // Stop any running generation first
            CancelActiveChatPageGeneration();

            AIChatSessionTitleTextBox.Text = _currentChatSession.Title;

            // Clear dynamic messages
            AIChatPageHistoryPanel.Children.Clear();
            
            if (AIChatPageWelcomePlaceholder != null)
            {
                AIChatPageWelcomePlaceholder.Visibility = _currentChatSession.Messages.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                if (AIChatPageWelcomePlaceholder.Visibility == Visibility.Visible)
                {
                    AIChatPageHistoryPanel.Children.Add(AIChatPageWelcomePlaceholder);
                }
            }

            // Render existing messages as bubbles
            foreach (var msg in _currentChatSession.Messages)
            {
                AddChatPageBubble(msg.Text, msg.IsUser);
            }

            // Scroll to bottom
            ScrollChatPageToBottom();
        }

        private void UpdateChatPageUIForNoSession()
        {
            _currentChatSession = null;
            if (AIChatSessionTitleTextBox != null)
            {
                AIChatSessionTitleTextBox.Text = "No active chat";
            }
            if (AIChatPageHistoryPanel != null)
            {
                AIChatPageHistoryPanel.Children.Clear();
                if (AIChatPageWelcomePlaceholder != null)
                {
                    AIChatPageWelcomePlaceholder.Visibility = Visibility.Visible;
                    AIChatPageHistoryPanel.Children.Add(AIChatPageWelcomePlaceholder);
                }
            }
        }

        // ── Controls & Bubble Rendering ──────────────────────────────────────
        private void AddChatPageBubble(string text, bool isUser)
        {
            if (AIChatPageHistoryPanel == null) return;

            // Hide placeholder on first message
            if (AIChatPageWelcomePlaceholder != null)
            {
                AIChatPageWelcomePlaceholder.Visibility = Visibility.Collapsed;
                AIChatPageHistoryPanel.Children.Remove(AIChatPageWelcomePlaceholder);
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
            }
            else
            {
                ParseMarkdownToInlines(text, textBlock);
            }

            bubbleBorder.Child = textBlock;
            AIChatPageHistoryPanel.Children.Add(bubbleBorder);
            ScrollChatPageToBottom();
        }

        private void UpdateLastChatPageBubble(string text, bool addCursor = false)
        {
            if (AIChatPageHistoryPanel == null || AIChatPageHistoryPanel.Children.Count == 0) return;

            var lastChild = AIChatPageHistoryPanel.Children.LastOrDefault();
            if (lastChild is Border bubbleBorder && bubbleBorder.Child is TextBlock textBlock)
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
            }
            ScrollChatPageToBottom();
        }

        private void ScrollChatPageToBottom()
        {
            if (AIChatPageHistoryScrollViewer != null)
            {
                AIChatPageHistoryScrollViewer.ChangeView(null, AIChatPageHistoryScrollViewer.ScrollableHeight, null);
            }
        }

        // ── Blinking Cursor Timer ─────────────────────────────────────────────
        private void StartChatPageCursorBlink()
        {
            if (_chatPageCursorBlinkTimer == null)
            {
                _chatPageCursorBlinkTimer = new DispatcherTimer();
                _chatPageCursorBlinkTimer.Interval = TimeSpan.FromMilliseconds(500);
                _chatPageCursorBlinkTimer.Tick += (s, e) =>
                {
                    _chatPageCursorVisible = !_chatPageCursorVisible;
                    UpdateLastChatPageBubble(_chatPageActiveAIText, _chatPageCursorVisible);
                };
            }
            _chatPageCursorVisible = true;
            _chatPageCursorBlinkTimer.Start();
        }

        private void StopChatPageCursorBlink()
        {
            _chatPageCursorBlinkTimer?.Stop();
            UpdateLastChatPageBubble(_chatPageActiveAIText, addCursor: false);
        }

        // ── Chat Prompts & Generation ─────────────────────────────────────────
        private void AIChatNewSessionButton_Click(object sender, RoutedEventArgs e)
        {
            CreateNewChatSession();
        }

        private void CreateNewChatSession()
        {
            var session = new AIChatSession();
            _aiChatSessions.Insert(0, session);
            if (AIChatSessionListView != null)
            {
                AIChatSessionListView.SelectedItem = session;
            }
            SaveAIChatSessions();
            if (AIChatPageInputBox != null)
            {
                AIChatPageInputBox.Focus(FocusState.Programmatic);
            }
        }

        private void AIChatDeleteSessionButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is AIChatSession session)
            {
                _aiChatSessions.Remove(session);
                SaveAIChatSessions();

                if (_currentChatSession == session)
                {
                    if (_aiChatSessions.Count > 0)
                    {
                        if (AIChatSessionListView != null) AIChatSessionListView.SelectedIndex = 0;
                    }
                    else
                    {
                        UpdateChatPageUIForNoSession();
                    }
                }
            }
        }

        private async void AIChatPageSendButton_Click(object sender, RoutedEventArgs e)
        {
            await RunAIChatPagePromptAsync();
        }

        private async void AIChatPageInputBox_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                var shift = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift);
                bool isShiftDown = (shift & Windows.UI.Core.CoreVirtualKeyStates.Down) == Windows.UI.Core.CoreVirtualKeyStates.Down;
                if (!isShiftDown)
                {
                    e.Handled = true;
                    await RunAIChatPagePromptAsync();
                }
            }
        }

        private async Task RunAIChatPagePromptAsync()
        {
            if (AIChatPageInputBox == null) return;

            string userText = AIChatPageInputBox.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(userText)) return;

            AIChatPageInputBox.Text = string.Empty;

            // If no active session, create one
            if (_currentChatSession == null)
            {
                var session = new AIChatSession();
                string firstMsg = userText.Length > 25 ? userText.Substring(0, 25) + "..." : userText;
                session.Title = firstMsg;
                _aiChatSessions.Insert(0, session);
                _currentChatSession = session;
                if (AIChatSessionListView != null) AIChatSessionListView.SelectedItem = session;
            }

            // Add user message to session models
            var userMsg = new AIChatMessage { Text = userText, IsUser = true };
            _currentChatSession.Messages.Add(userMsg);
            
            // Add user bubble to UI
            AddChatPageBubble(userText, isUser: true);

            // Re-label session title if it was default
            if (_currentChatSession.Title == "New Chat")
            {
                string newTitle = userText.Length > 25 ? userText.Substring(0, 25) + "..." : userText;
                _currentChatSession.Title = newTitle;
                if (AIChatSessionTitleTextBox != null)
                {
                    AIChatSessionTitleTextBox.Text = newTitle;
                }
                SaveAIChatSessions();
                // Auto-refresh via INotifyPropertyChanged
            }

            string selectedModel = AIChatPageModelCombo?.SelectedItem as string ?? string.Empty;
            if (string.IsNullOrWhiteSpace(selectedModel) || selectedModel.StartsWith("(") || selectedModel.StartsWith("Install"))
            {
                AddChatPageBubble("Please select a valid Ollama model first.", isUser: false);
                return;
            }

            // Build full context prompt from message history
            string systemPrompt = "You are a helpful AI journal writing assistant. Engage in a friendly, helpful chat conversation with the user. Help them explore their ideas, thoughts, or write entries.\n\nContext History:\n";
            var contextMsgs = _currentChatSession.Messages.TakeLast(15).ToList();
            foreach (var m in contextMsgs)
            {
                systemPrompt += m.IsUser ? $"User: {m.Text}\n" : $"AI: {m.Text}\n";
            }

            // Start generation
            CancelActiveChatPageGeneration();
            _aiChatPageCts = new CancellationTokenSource();
            _chatPageActiveAIText = string.Empty;

            // Render AI bubble with cursor
            AddChatPageBubble(string.Empty, isUser: false);
            StartChatPageCursorBlink();

            if (AIChatPageProgressGrid != null) AIChatPageProgressGrid.Visibility = Visibility.Visible;
            if (AIChatPageStopButton != null) AIChatPageStopButton.Visibility = Visibility.Visible;

            try
            {
                await OllamaService.Instance.StreamChatAsync(
                    selectedModel,
                    systemPrompt,
                    userText,
                    token =>
                    {
                        DispatcherQueue.TryEnqueue(() =>
                        {
                            _chatPageActiveAIText += token;
                            UpdateLastChatPageBubble(_chatPageActiveAIText, _chatPageCursorVisible);
                        });
                    },
                    _aiChatPageCts.Token);
            }
            catch (OperationCanceledException)
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    StopChatPageCursorBlink();
                    if (!string.IsNullOrEmpty(_chatPageActiveAIText))
                    {
                        _chatPageActiveAIText += " [Generation stopped]";
                        UpdateLastChatPageBubble(_chatPageActiveAIText, addCursor: false);
                    }
                });
            }
            catch (Exception ex)
            {
                AddChatPageBubble($"Could not connect to Ollama: {ex.Message}. Make sure Ollama is running.", isUser: false);
            }
            finally
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    StopChatPageCursorBlink();
                    if (AIChatPageProgressGrid != null) AIChatPageProgressGrid.Visibility = Visibility.Collapsed;
                    if (AIChatPageStopButton != null) AIChatPageStopButton.Visibility = Visibility.Collapsed;

                    // Add AI response message to session models
                    if (!string.IsNullOrEmpty(_chatPageActiveAIText))
                    {
                        var aiMsg = new AIChatMessage { Text = _chatPageActiveAIText, IsUser = false };
                        _currentChatSession.Messages.Add(aiMsg);
                        SaveAIChatSessions();
                    }
                });
            }
        }

        private void AIChatPageStopButton_Click(object sender, RoutedEventArgs e)
        {
            CancelActiveChatPageGeneration();
        }

        private void AIChatPageModelCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Sync with side panel if appropriate
        }

        private void CancelActiveChatPageGeneration()
        {
            if (_aiChatPageCts != null)
            {
                _aiChatPageCts.Cancel();
                _aiChatPageCts = null;
            }
            StopChatPageCursorBlink();
        }

        // ── Title Editing ─────────────────────────────────────────────────────
        private void AIChatSessionTitleTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            SaveActiveChatTitle();
        }

        private void AIChatSessionTitleTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                FocusManager.TryMoveFocus(FocusNavigationDirection.Next); // Triggers LostFocus
                e.Handled = true;
            }
        }

        private void SaveActiveChatTitle()
        {
            if (_currentChatSession != null && AIChatSessionTitleTextBox != null)
            {
                string newTitle = AIChatSessionTitleTextBox.Text?.Trim() ?? string.Empty;
                if (!string.IsNullOrEmpty(newTitle) && newTitle != _currentChatSession.Title)
                {
                    _currentChatSession.Title = newTitle;
                    SaveAIChatSessions();
                    // Auto-refresh via INotifyPropertyChanged
                }
            }
        }
    }
}
