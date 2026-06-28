using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

namespace JournalApp
{
    public sealed partial class MainPage
    {
        // ── Stats, Charts & Writing Streak Methods ─────────────────────────────

        /// <summary>
        /// Populates the mood statistics progress bars and count labels based on the user's active journal entries.
        /// </summary>
        private void PopulateMoodStats()
        {
            var notes = JournalManager.Instance.Notes.Where(n => !n.IsDeleted).ToList();
            int total = notes.Count;

            int happy = notes.Count(n => n.Mood == "😊 Happy");
            int neutral = notes.Count(n => n.Mood == "😐 Neutral");
            int sad = notes.Count(n => n.Mood == "😢 Sad");
            int stressed = notes.Count(n => n.Mood == "😫 Stressed");
            int angry = notes.Count(n => n.Mood == "😡 Angry");
            int none = notes.Count(n => string.IsNullOrEmpty(n.Mood) || n.Mood == "None");

            if (total == 0)
            {
                if (MoodHappyCountText != null) MoodHappyCountText.Text = "0 entries (0%)";
                if (MoodHappyProgress != null) MoodHappyProgress.Value = 0;
                
                if (MoodNeutralCountText != null) MoodNeutralCountText.Text = "0 entries (0%)";
                if (MoodNeutralProgress != null) MoodNeutralProgress.Value = 0;

                if (MoodSadCountText != null) MoodSadCountText.Text = "0 entries (0%)";
                if (MoodSadProgress != null) MoodSadProgress.Value = 0;

                if (MoodStressedCountText != null) MoodStressedCountText.Text = "0 entries (0%)";
                if (MoodStressedProgress != null) MoodStressedProgress.Value = 0;

                if (MoodAngryCountText != null) MoodAngryCountText.Text = "0 entries (0%)";
                if (MoodAngryProgress != null) MoodAngryProgress.Value = 0;

                if (MoodNoneCountText != null) MoodNoneCountText.Text = "0 entries (0%)";
                if (MoodNoneProgress != null) MoodNoneProgress.Value = 0;
                return;
            }

            double p(int c) => (double)c / total * 100;

            if (MoodHappyCountText != null) MoodHappyCountText.Text = $"{happy} entries ({p(happy):F0}%)";
            if (MoodHappyProgress != null) MoodHappyProgress.Value = p(happy);

            if (MoodNeutralCountText != null) MoodNeutralCountText.Text = $"{neutral} entries ({p(neutral):F0}%)";
            if (MoodNeutralProgress != null) MoodNeutralProgress.Value = p(neutral);

            if (MoodSadCountText != null) MoodSadCountText.Text = $"{sad} entries ({p(sad):F0}%)";
            if (MoodSadProgress != null) MoodSadProgress.Value = p(sad);

            if (MoodStressedCountText != null) MoodStressedCountText.Text = $"{stressed} entries ({p(stressed):F0}%)";
            if (MoodStressedProgress != null) MoodStressedProgress.Value = p(stressed);

            if (MoodAngryCountText != null) MoodAngryCountText.Text = $"{angry} entries ({p(angry):F0}%)";
            if (MoodAngryProgress != null) MoodAngryProgress.Value = p(angry);

            if (MoodNoneCountText != null) MoodNoneCountText.Text = $"{none} entries ({p(none):F0}%)";
            if (MoodNoneProgress != null) MoodNoneProgress.Value = p(none);
        }

        /// <summary>
        /// Computes the longest consecutive writing streak (in days) in the user's active journal entries.
        /// </summary>
        /// <returns>Length of the longest streak in days.</returns>
        private int ComputeLongestWritingStreak()
        {
            var activityDates = JournalManager.Instance.Notes
                .Where(n => !n.IsDeleted)
                .SelectMany(n => new[] { n.DateCreated.Date, n.DateModified.Date })
                .Distinct()
                .OrderBy(d => d)
                .ToList();

            if (activityDates.Count == 0) return 0;

            int maxStreak = 0;
            int currentStreak = 1;

            for (int i = 1; i < activityDates.Count; i++)
            {
                if (activityDates[i] == activityDates[i - 1].AddDays(1))
                {
                    currentStreak++;
                }
                else if (activityDates[i] != activityDates[i - 1])
                {
                    maxStreak = Math.Max(maxStreak, currentStreak);
                    currentStreak = 1;
                }
            }

            maxStreak = Math.Max(maxStreak, currentStreak);
            return maxStreak;
        }

        /// <summary>
        /// Dynamically builds the contribution grid (GitHub-like commit box visualizer) for the last 365 days.
        /// </summary>
        private void PopulateContributionGraph()
        {
            if (ContributionGrid == null) return;

            ContributionGrid.Children.Clear();
            ContributionGrid.RowDefinitions.Clear();
            ContributionGrid.ColumnDefinitions.Clear();

            for (int r = 0; r < 7; r++)
            {
                ContributionGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            }
            for (int c = 0; c < 53; c++)
            {
                ContributionGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            }

            var noteCounts = JournalManager.Instance.Notes
                .Where(n => !n.IsDeleted)
                .GroupBy(n => n.DateCreated.Date)
                .ToDictionary(g => g.Key, g => g.Count());

            DateTime today = DateTime.Today;
            DateTime startDate = today.AddDays(-370);
            int startDayOfWeek = (int)startDate.DayOfWeek;
            DateTime gridStartDate = startDate.AddDays(-startDayOfWeek);

            for (int col = 0; col < 53; col++)
            {
                for (int row = 0; row < 7; row++)
                {
                    DateTime cellDate = gridStartDate.AddDays(col * 7 + row);
                    
                    bool isFuture = cellDate > today;
                    bool isBeforeRange = cellDate < startDate;
                    
                    int count = 0;
                    if (!isFuture && !isBeforeRange)
                    {
                        noteCounts.TryGetValue(cellDate, out count);
                    }

                    string hexColor = "#15FFFFFF";
                    if (this.ActualTheme == ElementTheme.Light)
                    {
                        hexColor = "#F0F0F0";
                    }

                    if (!isFuture && !isBeforeRange)
                    {
                        if (count == 1) hexColor = "#FF1b4c2b";
                        else if (count == 2) hexColor = "#FF2e6930";
                        else if (count == 3) hexColor = "#FF398b3f";
                        else if (count >= 4) hexColor = "#FF4dca57";
                    }

                    var rect = new Microsoft.UI.Xaml.Shapes.Rectangle
                    {
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        VerticalAlignment = VerticalAlignment.Stretch,
                        RadiusX = 2,
                        RadiusY = 2,
                        Fill = GetBrushFromHex(hexColor),
                        Opacity = (isFuture || isBeforeRange) ? 0.0 : 1.0
                    };

                    if (!isFuture && !isBeforeRange)
                    {
                        var toolTipText = $"{cellDate.ToString("MMMM d, yyyy")}: {count} {(count == 1 ? "entry" : "entries")}";
                        ToolTipService.SetToolTip(rect, toolTipText);
                    }

                    Grid.SetRow(rect, row);
                    Grid.SetColumn(rect, col);
                    ContributionGrid.Children.Add(rect);
                }
            }

            if (CurrentStreakStatsText != null)
            {
                CurrentStreakStatsText.Text = $"{ComputeWritingStreak()} days";
            }
            if (LongestStreakStatsText != null)
            {
                LongestStreakStatsText.Text = $"{ComputeLongestWritingStreak()} days";
            }
            if (TotalEntriesStatsText != null)
            {
                TotalEntriesStatsText.Text = JournalManager.Instance.Notes.Count(n => !n.IsDeleted).ToString();
            }
        }

        /// <summary>
        /// Populates the tag cloud visualizer with tags sorted by counts and sized dynamically based on usage frequency.
        /// </summary>
        private void PopulateTagCloud()
        {
            if (TagCloudPanel == null) return;
            TagCloudPanel.Children.Clear();

            var notes = JournalManager.Instance.Notes.Where(n => !n.IsDeleted).ToList();
            var tagCounts = notes.SelectMany(n => n.Tags ?? new List<string>())
                                 .GroupBy(t => t.ToLowerInvariant().Trim())
                                 .Select(g => new { Tag = g.Key, Count = g.Count() })
                                 .OrderByDescending(x => x.Count)
                                 .ToList();

            if (tagCounts.Count == 0)
            {
                var emptyText = new TextBlock
                {
                    Text = "No tags added to entries yet.",
                    FontSize = 13,
                    Foreground = GetThemeBrush("TextFillColorSecondaryBrush", "#8A8886"),
                    FontStyle = Windows.UI.Text.FontStyle.Italic
                };
                TagCloudPanel.Children.Add(emptyText);
                return;
            }

            int maxCount = tagCounts.Max(x => x.Count);
            foreach (var tc in tagCounts)
            {
                double fontSize = 12;
                if (maxCount > 1)
                {
                    fontSize = 12 + ((double)(tc.Count - 1) / (maxCount - 1)) * 10;
                }

                var btn = new Button
                {
                    Content = $"#{tc.Tag} ({tc.Count})",
                    FontSize = fontSize,
                    Margin = new Thickness(4),
                    Padding = new Thickness(10, 6, 10, 6),
                    CornerRadius = new CornerRadius(12)
                };

                string tagToFilter = tc.Tag;
                btn.Click += (s, e) =>
                {
                    FilterNotesByTag(tagToFilter);
                };

                TagCloudPanel.Children.Add(btn);
            }
        }

        /// <summary>
        /// Re-entrant safe method to filter the entries sidebar by a selected tag.
        /// </summary>
        /// <param name="tag">Target tag string to filter by.</param>
        private void FilterNotesByTag(string tag)
        {
            if (_isFilteringByTag) return;
            _isFilteringByTag = true;
            try
            {
                _selectedCategory = $"Tag:{tag}";
                CategoriesNavView.SelectedItem = null;
                ShowGrid(MainEditorGrid);
                RefreshNotesList();
                
                if (NotesListView.Items.Count > 0)
                {
                    NotesListView.SelectedIndex = 0;
                }
                else
                {
                    SelectedNote = null;
                }
            }
            finally
            {
                this.DispatcherQueue.TryEnqueue(() =>
                {
                    _isFilteringByTag = false;
                });
            }
        }

        /// <summary>
        /// Renders and displays active streak details on the categories navigation header.
        /// </summary>
        private void UpdateStreakUI()
        {
            try
            {
                int streak = ComputeWritingStreak();
                if (StreakBadgeText != null)
                {
                    if (streak > 0)
                    {
                        StreakBadgeText.Text = streak == 1
                            ? "🔥 1 day streak"
                            : $"🔥 {streak} day streak";
                        StreakBadgeText.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        StreakBadgeText.Visibility = Visibility.Collapsed;
                    }
                }
            }
            catch { }
        }

        /// <summary>
        /// Computes the current consecutive writing streak (in days) ending today or yesterday.
        /// </summary>
        /// <returns>Number of days in current consecutive streak.</returns>
        private int ComputeWritingStreak()
        {
            var activityDates = JournalManager.Instance.Notes
                .Where(n => !n.IsDeleted)
                .SelectMany(n => new[] { n.DateCreated.Date, n.DateModified.Date })
                .Distinct()
                .ToHashSet();

            var today = DateTime.Today;
            int streak = 0;
            var check = activityDates.Contains(today) ? today : today.AddDays(-1);
            if (!activityDates.Contains(check)) return 0;

            while (activityDates.Contains(check))
            {
                streak++;
                check = check.AddDays(-1);
            }
            return streak;
        }
    }
}
