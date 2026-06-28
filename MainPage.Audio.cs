using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Storage;

namespace JournalApp
{
    public sealed partial class MainPage
    {
        // ── Audio Recording & Playback Fields ──────────────────────────────────

        /// <summary>
        /// Native media capture provider used for voice recordings.
        /// </summary>
        private Windows.Media.Capture.MediaCapture _mediaCapture;

        /// <summary>
        /// Low lag recording controller session.
        /// </summary>
        private Windows.Media.Capture.LowLagMediaRecording _mediaRecording;

        /// <summary>
        /// Relative file name of the currently active recording file.
        /// </summary>
        private string _currentRecordingFile;

        /// <summary>
        /// Flag indicating whether the application is currently recording audio.
        /// </summary>
        private bool _isRecording = false;
        private Windows.Media.SpeechRecognition.SpeechRecognizer _speechRecognizer;
        private System.Text.StringBuilder _transcriptionResult;
        private bool _isTranscribing = false;

        /// <summary>
        /// Timer used to track the duration of a voice recording.
        /// </summary>
        private DispatcherTimer _recordingTimer;

        /// <summary>
        /// Number of elapsed seconds for the current voice recording.
        /// </summary>
        private int _recordingDurationSeconds = 0;

        /// <summary>
        /// Global media player instance for playbacks of attached voice memos.
        /// </summary>
        private Windows.Media.Playback.MediaPlayer _audioPlayer = new Windows.Media.Playback.MediaPlayer();

        /// <summary>
        /// Relative path of the voice memo file currently playing.
        /// </summary>
        private string _currentlyPlayingFile;

        // ── Microphone Selection Fields ────────────────────────────────────────

        /// <summary>
        /// Collection of physical audio capture devices discovered on the user's system.
        /// </summary>
        private readonly List<Windows.Devices.Enumeration.DeviceInformation> _audioDevices = new();

        /// <summary>
        /// Device identifier of the currently selected audio recording source.
        /// </summary>
        private string _selectedAudioDeviceId = "";

        // ── Audio Recording & Playback Methods ─────────────────────────────────

        /// <summary>
        /// Updates the attached voice recordings panel in the UI, drawing lists of audio memos, Play/Pause buttons, Rename pencil controls, and Delete trashcans.
        /// </summary>
        private void UpdateAttachedAudioUI()
        {
            if (AttachedAudioPanel == null) return;
            AttachedAudioPanel.Children.Clear();

            if (SelectedNote == null || SelectedNote.AttachedAudioPaths == null || SelectedNote.AttachedAudioPaths.Count == 0)
            {
                AttachedAudioPanel.Visibility = Visibility.Collapsed;
                return;
            }

            AttachedAudioPanel.Visibility = Visibility.Visible;
            foreach (var audioPath in SelectedNote.AttachedAudioPaths)
            {
                var absPath = JournalManager.Instance.GetAbsoluteMediaPath(audioPath);
                
                var row = new Grid
                {
                    Margin = new Thickness(0, 4, 0, 4),
                    ColumnSpacing = 8
                };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var playBtn = new Button
                {
                    Width = 32, Height = 32,
                    CornerRadius = new CornerRadius(16),
                    Padding = new Thickness(0),
                    Content = new FontIcon
                    {
                        Glyph = (_currentlyPlayingFile == audioPath) ? "\uE71A" : "\uE768",
                        FontFamily = (Microsoft.UI.Xaml.Media.FontFamily)Resources["SymbolThemeFontFamily"],
                        FontSize = 10
                    }
                };
                
                string currentPath = audioPath;
                string currentAbsPath = absPath;
                playBtn.Click += (sender, args) =>
                {
                    ToggleAudioPlay(currentPath, currentAbsPath);
                };
                Grid.SetColumn(playBtn, 0);
                row.Children.Add(playBtn);

                string displayName = Path.GetFileNameWithoutExtension(audioPath);
                if (displayName.StartsWith("voice_") && displayName.Length > 20)
                {
                    displayName = "Voice Memo";
                }

                var labelText = new TextBlock
                {
                    Text = displayName,
                    VerticalAlignment = VerticalAlignment.Center,
                    FontSize = 12,
                    Foreground = GetThemeBrush("TextFillColorSecondaryBrush", "#8A8886")
                };
                Grid.SetColumn(labelText, 1);
                row.Children.Add(labelText);

                var renameBtn = new Button
                {
                    Width = 32, Height = 32,
                    CornerRadius = new CornerRadius(16),
                    Padding = new Thickness(0),
                    Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                    BorderThickness = new Thickness(0),
                    Content = new FontIcon
                    {
                        Glyph = "\uE70F",
                        FontFamily = (Microsoft.UI.Xaml.Media.FontFamily)Resources["SymbolThemeFontFamily"],
                        FontSize = 11,
                        Foreground = GetThemeBrush("TextFillColorSecondaryBrush", "#8A8886")
                    }
                };
                renameBtn.Click += async (sender, args) =>
                {
                    await RenameAttachedAudioAsync(currentPath);
                };
                Grid.SetColumn(renameBtn, 2);
                row.Children.Add(renameBtn);

                var deleteBtn = new Button
                {
                    Width = 32, Height = 32,
                    CornerRadius = new CornerRadius(16),
                    Padding = new Thickness(0),
                    Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                    BorderThickness = new Thickness(0),
                    Content = new FontIcon
                    {
                        Glyph = "\uE74D",
                        FontFamily = (Microsoft.UI.Xaml.Media.FontFamily)Resources["SymbolThemeFontFamily"],
                        FontSize = 12,
                        Foreground = new SolidColorBrush(Microsoft.UI.Colors.Red)
                    }
                };
                deleteBtn.Click += (sender, args) =>
                {
                    RemoveAttachedAudio(currentPath);
                };
                Grid.SetColumn(deleteBtn, 3);
                row.Children.Add(deleteBtn);

                AttachedAudioPanel.Children.Add(row);
            }
        }

        /// <summary>
        /// Displays an input text dialog to rename the voice recording file and updates UI database pointers.
        /// </summary>
        /// <param name="relativePath">The current relative filename.</param>
        private async Task RenameAttachedAudioAsync(string relativePath)
        {
            if (SelectedNote == null) return;
            
            string currentName = Path.GetFileNameWithoutExtension(relativePath);
            if (currentName.StartsWith("voice_") && currentName.Length > 20)
            {
                currentName = "Voice Memo";
            }

            string newName = await PromptForTextInputAsync("Rename Voice Memo", "Enter a new name for the voice memo:", currentName);
            if (string.IsNullOrWhiteSpace(newName)) return;

            foreach (char c in Path.GetInvalidFileNameChars())
            {
                newName = newName.Replace(c.ToString(), "");
            }
            newName = newName.Trim();

            if (string.IsNullOrEmpty(newName)) return;

            string ext = Path.GetExtension(relativePath);
            string newFileName = $"{newName}{ext}";
            
            if (newFileName.Equals(relativePath, StringComparison.OrdinalIgnoreCase)) return;

            string oldAbsPath = JournalManager.Instance.GetAbsoluteMediaPath(relativePath);
            string newAbsPath = Path.Combine(JournalManager.Instance.MediaDir, newFileName);

            try
            {
                if (File.Exists(newAbsPath))
                {
                    await ShowAlertAsync("File Exists", $"A recording named '{newFileName}' already exists. Please choose a different name.");
                    return;
                }

                if (File.Exists(oldAbsPath))
                {
                    File.Move(oldAbsPath, newAbsPath);
                }

                int idx = SelectedNote.AttachedAudioPaths.IndexOf(relativePath);
                if (idx >= 0)
                {
                    SelectedNote.AttachedAudioPaths[idx] = newFileName;
                    JournalManager.Instance.SaveNotesMetadata();
                    UpdateAttachedAudioUI();
                    MarkDirty();
                }
            }
            catch (Exception ex)
            {
                await ShowAlertAsync("Error Renaming", $"Failed to rename the voice memo: {ex.Message}");
            }
        }

        /// <summary>
        /// Toggles playback (Play or Pause) for a specified audio file.
        /// </summary>
        /// <param name="relativePath">Relative filename.</param>
        /// <param name="absolutePath">Absolute local path.</param>
        private void ToggleAudioPlay(string relativePath, string absolutePath)
        {
            if (_currentlyPlayingFile == relativePath)
            {
                _audioPlayer.Pause();
                _currentlyPlayingFile = null;
            }
            else
            {
                try
                {
                    _audioPlayer.Source = Windows.Media.Core.MediaSource.CreateFromUri(new Uri(absolutePath));
                    _audioPlayer.Play();
                    _currentlyPlayingFile = relativePath;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to play audio: {ex.Message}");
                }
            }
            UpdateAttachedAudioUI();
        }

        /// <summary>
        /// Resets active play state trackers and updates playback buttons icon indicators.
        /// </summary>
        private void ResetAudioPlaybackUI()
        {
            _currentlyPlayingFile = null;
            UpdateAttachedAudioUI();
        }

        /// <summary>
        /// Removes an audio memo attachment, stopping playback and deleting the file from disk.
        /// </summary>
        /// <param name="relativePath">The relative path of the file to remove.</param>
        private void RemoveAttachedAudio(string relativePath)
        {
            if (SelectedNote == null || SelectedNote.AttachedAudioPaths == null) return;
            
            if (_currentlyPlayingFile == relativePath)
            {
                _audioPlayer.Pause();
                _currentlyPlayingFile = null;
            }

            SelectedNote.AttachedAudioPaths.Remove(relativePath);
            
            string absPath = JournalManager.Instance.GetAbsoluteMediaPath(relativePath);
            if (File.Exists(absPath))
            {
                try { File.Delete(absPath); } catch {}
            }
            
            JournalManager.Instance.SaveNotesMetadata();
            UpdateAttachedAudioUI();
            MarkDirty();
        }

        /// <summary>
        /// Toggle click event handler to either trigger start or stop recording.
        /// </summary>
        private async void RecordAudioButton_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedNote == null) return;

            if (_isRecording)
            {
                await StopAudioRecordingAsync();
            }
            else
            {
                await StartAudioRecordingAsync();
            }
        }

        /// <summary>
        /// Initializes MediaCapture with the selected audio device and starts recording.
        /// </summary>
        private async Task StartAudioRecordingAsync()
        {
            try
            {
                _mediaCapture = new Windows.Media.Capture.MediaCapture();
                var settings = new Windows.Media.Capture.MediaCaptureInitializationSettings
                {
                    StreamingCaptureMode = Windows.Media.Capture.StreamingCaptureMode.Audio,
                    AudioDeviceId = string.IsNullOrEmpty(_selectedAudioDeviceId) ? "" : _selectedAudioDeviceId
                };
                await _mediaCapture.InitializeAsync(settings);

                string fileName = $"voice_{Guid.NewGuid()}.m4a";
                _currentRecordingFile = fileName;

                var mediaFolder = await StorageFolder.GetFolderFromPathAsync(JournalManager.Instance.MediaDir);
                var file = await mediaFolder.CreateFileAsync(
                    fileName,
                    CreationCollisionOption.ReplaceExisting);

                var profile = Windows.Media.MediaProperties.MediaEncodingProfile.CreateM4a(Windows.Media.MediaProperties.AudioEncodingQuality.Medium);

                _mediaRecording = await _mediaCapture.PrepareLowLagRecordToStorageFileAsync(profile, file);
                await _mediaRecording.StartAsync();

                _isRecording = true;
                _recordingDurationSeconds = 0;
                
                if (RecordAudioText != null) RecordAudioText.Text = "Stop Recording";
                if (RecordAudioIcon != null) RecordAudioIcon.Glyph = "\uE71A";
                if (RecordingTimerTextBlock != null)
                {
                    RecordingTimerTextBlock.Text = "00:00";
                    RecordingTimerTextBlock.Visibility = Visibility.Visible;
                }

                _recordingTimer.Start();
                await StartSpeechTranscriptionAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to start audio recording: {ex.Message}");
                await ShowAlertAsync("Recording Failed", "Failed to start microphone recording. Please verify that microphone permissions are enabled for the application.");
                CleanupRecordingResources();
            }
        }

        /// <summary>
        /// Stops the active recording, saves note attachments metadata, and cleans up handles.
        /// </summary>
        private async Task StopAudioRecordingAsync()
        {
            _recordingTimer.Stop();

            try
            {
                if (_mediaRecording != null)
                {
                    await _mediaRecording.StopAsync();
                    await _mediaRecording.FinishAsync();
                    _mediaRecording = null;
                }

                SelectedNote.AttachedAudioPaths ??= new List<string>();
                SelectedNote.AttachedAudioPaths.Add(_currentRecordingFile);
                
                string transcribedText = await StopSpeechTranscriptionAsync();
                if (!string.IsNullOrEmpty(transcribedText))
                {
                    AppendTranscribedTextToNote(transcribedText);
                }
                
                JournalManager.Instance.SaveNotesMetadata();
                UpdateAttachedAudioUI();
                MarkDirty();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to stop audio recording: {ex.Message}");
            }
            finally
            {
                CleanupRecordingResources();
            }
        }

        private async Task StartSpeechTranscriptionAsync()
        {
            try
            {
                _transcriptionResult = new System.Text.StringBuilder();
                _speechRecognizer = new Windows.Media.SpeechRecognition.SpeechRecognizer();
                
                var dictationConstraint = new Windows.Media.SpeechRecognition.SpeechRecognitionTopicConstraint(
                    Windows.Media.SpeechRecognition.SpeechRecognitionScenario.Dictation, "dictation");
                _speechRecognizer.Constraints.Add(dictationConstraint);
                
                await _speechRecognizer.CompileConstraintsAsync();
                
                _speechRecognizer.ContinuousRecognitionSession.ResultGenerated += (s, args) =>
                {
                    if (args.Result != null && !string.IsNullOrEmpty(args.Result.Text))
                    {
                        _transcriptionResult.Append(args.Result.Text + " ");
                    }
                };
                
                await _speechRecognizer.ContinuousRecognitionSession.StartAsync();
                _isTranscribing = true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Transcription] Initialization failed: {ex.Message}");
                _speechRecognizer?.Dispose();
                _speechRecognizer = null;
                _isTranscribing = false;
            }
        }

        private async Task<string> StopSpeechTranscriptionAsync()
        {
            if (!_isTranscribing || _speechRecognizer == null) return null;
            
            try
            {
                await _speechRecognizer.ContinuousRecognitionSession.StopAsync();
            }
            catch {}
            
            string text = _transcriptionResult?.ToString().Trim();
            _speechRecognizer?.Dispose();
            _speechRecognizer = null;
            _isTranscribing = false;
            
            return text;
        }

        private void AppendTranscribedTextToNote(string text)
        {
            if (string.IsNullOrEmpty(text)) return;

            if (NativeBlockEditorScroll.Visibility == Visibility.Visible)
            {
                var newBlock = new EditorBlock { Type = "quote", Content = $"🎙️ Voice Memo Transcription: {text}" };
                _nativeBlocks.Add(newBlock);
                _currentMarkdownContent = ExportBlocksToMarkdown();
                MarkDirty();
                RenderNativeBlocks();
                UpdateWordCount();
            }
            else
            {
                string formattedText = $"\n[Transcription]: \"{text}\"\n";
                var selection = NoteRichEditBox.Document.Selection;
                selection.SetText(Microsoft.UI.Text.TextSetOptions.None, formattedText);
                MarkDirty();
                UpdateWordCount();
            }
        }

        /// <summary>
        /// Cleans up recording resources and resets UI button states.
        /// </summary>
        private void CleanupRecordingResources()
        {
            if (_mediaCapture != null)
            {
                _mediaCapture.Dispose();
                _mediaCapture = null;
            }
            _isRecording = false;

            if (RecordAudioText != null) RecordAudioText.Text = "Record Memo";
            if (RecordAudioIcon != null) RecordAudioIcon.Glyph = "\uE720";
            if (RecordingTimerTextBlock != null) RecordingTimerTextBlock.Visibility = Visibility.Collapsed;
        }

        /// <summary>
        /// Timer Tick handler tracking active recording duration and rendering text.
        /// </summary>
        private void RecordingTimer_Tick(object sender, object e)
        {
            _recordingDurationSeconds++;
            int minutes = _recordingDurationSeconds / 60;
            int seconds = _recordingDurationSeconds % 60;
            if (RecordingTimerTextBlock != null)
            {
                RecordingTimerTextBlock.Text = $"{minutes:D2}:{seconds:D2}";
            }
        }

        /// <summary>
        /// Queries all connected physical audio recording devices and updates choices dropdown.
        /// </summary>
        private async Task PopulateMicrophoneDevicesAsync()
        {
            if (MicSelectionComboBox == null) return;
            
            MicSelectionComboBox.SelectionChanged -= MicSelectionComboBox_SelectionChanged;
            MicSelectionComboBox.Items.Clear();
            _audioDevices.Clear();

            try
            {
                var devices = await Windows.Devices.Enumeration.DeviceInformation.FindAllAsync(Windows.Devices.Enumeration.DeviceClass.AudioCapture);
                foreach (var device in devices)
                {
                    _audioDevices.Add(device);
                    MicSelectionComboBox.Items.Add(device.Name);
                }

                if (_audioDevices.Count > 0)
                {
                    string savedId = GetSetting("SelectedMicrophoneId", "");
                    int selectedIndex = 0;
                    if (!string.IsNullOrEmpty(savedId))
                    {
                        var found = _audioDevices.FindIndex(d => d.Id == savedId);
                        if (found >= 0) selectedIndex = found;
                    }
                    MicSelectionComboBox.SelectedIndex = selectedIndex;
                    if (selectedIndex >= 0 && selectedIndex < _audioDevices.Count)
                    {
                        _selectedAudioDeviceId = _audioDevices[selectedIndex].Id;
                    }
                }
                else
                {
                    MicSelectionComboBox.Items.Add("No microphone detected");
                    MicSelectionComboBox.SelectedIndex = 0;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to populate microphones: {ex.Message}");
            }
            finally
            {
                MicSelectionComboBox.SelectionChanged += MicSelectionComboBox_SelectionChanged;
            }
        }

        /// <summary>
        /// ComboBox SelectionChanged event handler saving selected recording device ID.
        /// </summary>
        private void MicSelectionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (MicSelectionComboBox == null || _audioDevices.Count == 0) return;
            int idx = MicSelectionComboBox.SelectedIndex;
            if (idx >= 0 && idx < _audioDevices.Count)
            {
                var device = _audioDevices[idx];
                _selectedAudioDeviceId = device.Id;
                SaveSetting("SelectedMicrophoneId", _selectedAudioDeviceId);
            }
        }

        /// <summary>
        /// Event listener triggered when MediaPlayer finishes playing an audio clip.
        /// </summary>
        private void AudioPlayer_MediaEnded(Windows.Media.Playback.MediaPlayer sender, object args)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                ResetAudioPlaybackUI();
            });
        }
    }
}
