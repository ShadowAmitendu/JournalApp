using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;

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

        private void MoodChartCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            RenderMoodChartNative();
        }

        private Task LoadMoodChartAsync()
        {
            RenderMoodChartNative();
            return Task.CompletedTask;
        }

        private void RenderMoodChartNative()
        {
            if (MoodChartCanvas == null) return;

            MoodChartCanvas.Children.Clear();

            // Get notes in the last 30 days containing a valid mood, sorted oldest to newest
            var cutoffDate = DateTime.Now.AddDays(-30);
            var chartData = JournalManager.Instance.Notes
                .Where(n => !n.IsDeleted && n.DateCreated >= cutoffDate && !string.IsNullOrEmpty(n.Mood) && n.Mood != "None")
                .OrderBy(n => n.DateCreated)
                .Select(n => new
                {
                    DateLabel = n.DateCreated.ToString("MMM d"),
                    Mood = n.Mood,
                    Value = MapMoodToValue(n.Mood)
                })
                .ToList();

            if (chartData.Count == 0)
            {
                if (MoodChartEmptyText != null) MoodChartEmptyText.Visibility = Visibility.Visible;
                return;
            }
            else
            {
                if (MoodChartEmptyText != null) MoodChartEmptyText.Visibility = Visibility.Collapsed;
            }

            double width = MoodChartCanvas.ActualWidth;
            double height = MoodChartCanvas.ActualHeight;

            if (width <= 0 || height <= 0) return;

            // Dimensions and layout
            double leftMargin = 120; // Room for y-axis labels
            double rightMargin = 20;
            double topMargin = 20;
            double bottomMargin = 40; // Room for x-axis labels

            double plotWidth = width - leftMargin - rightMargin;
            double plotHeight = height - topMargin - bottomMargin;

            // Y position calculation: Value 5 is top, Value 1 is bottom
            Func<double, double> getY = (val) =>
            {
                double normalized = (val - 1.0) / 4.0; // 0 to 1
                return topMargin + plotHeight * (1.0 - normalized);
            };

            // Draw Y-axis grid lines and labels
            var yTicks = new Dictionary<double, string>
            {
                { 5.0, "😊 Happy" },
                { 3.0, "😐 Neutral" },
                { 2.0, "😢 Sad / 😫 Stressed" },
                { 1.0, "😡 Angry" }
            };

            var gridBrush = GetThemeBrush("CardStrokeColorDefaultBrush", "#E5E5E5");
            var textBrush = GetThemeBrush("TextFillColorPrimaryBrush", "#000000");

            foreach (var kvp in yTicks)
            {
                double yVal = kvp.Key;
                string labelText = kvp.Value;
                double yPos = getY(yVal);

                // Grid line
                var line = new Line
                {
                    X1 = leftMargin,
                    Y1 = yPos,
                    X2 = width - rightMargin,
                    Y2 = yPos,
                    Stroke = gridBrush,
                    StrokeThickness = 1,
                    StrokeDashArray = new DoubleCollection { 4, 4 }
                };
                MoodChartCanvas.Children.Add(line);

                // Label
                var tb = new TextBlock
                {
                    Text = labelText,
                    FontSize = 11,
                    Foreground = textBrush
                };
                tb.Measure(new Windows.Foundation.Size(leftMargin - 10, 20));
                double textWidth = tb.DesiredSize.Width;
                double textHeight = tb.DesiredSize.Height;
                Canvas.SetLeft(tb, leftMargin - textWidth - 10);
                Canvas.SetTop(tb, yPos - textHeight / 2);
                MoodChartCanvas.Children.Add(tb);
            }

            // X-axis baseline
            double xAxisY = getY(1.0);
            var xAxisLine = new Line
            {
                X1 = leftMargin,
                Y1 = xAxisY,
                X2 = width - rightMargin,
                Y2 = xAxisY,
                Stroke = gridBrush,
                StrokeThickness = 1.5
            };
            MoodChartCanvas.Children.Add(xAxisLine);

            // Y-axis baseline
            var yAxisLine = new Line
            {
                X1 = leftMargin,
                Y1 = topMargin,
                X2 = leftMargin,
                Y2 = xAxisY,
                Stroke = gridBrush,
                StrokeThickness = 1.5
            };
            MoodChartCanvas.Children.Add(yAxisLine);

            // Data Points coordinates
            int pointCount = chartData.Count;
            double xStep = pointCount > 1 ? plotWidth / (pointCount - 1) : plotWidth;

            var points = new List<Windows.Foundation.Point>();
            for (int i = 0; i < pointCount; i++)
            {
                double x = leftMargin + (pointCount > 1 ? i * xStep : plotWidth / 2);
                double y = getY(chartData[i].Value);
                points.Add(new Windows.Foundation.Point(x, y));
            }

            var accentColor = (Windows.UI.Color)Application.Current.Resources["SystemAccentColor"];
            var lineBrush = new SolidColorBrush(accentColor);

            // Area fill brush: semi-transparent gradient
            var fillBrush = new LinearGradientBrush
            {
                StartPoint = new Windows.Foundation.Point(0, 0),
                EndPoint = new Windows.Foundation.Point(0, 1)
            };
            fillBrush.GradientStops.Add(new GradientStop { Color = Windows.UI.Color.FromArgb(40, accentColor.R, accentColor.G, accentColor.B), Offset = 0.0 });
            fillBrush.GradientStops.Add(new GradientStop { Color = Windows.UI.Color.FromArgb(0, accentColor.R, accentColor.G, accentColor.B), Offset = 1.0 });

            // Area path geometry
            var fillGeometry = new PathGeometry();
            var fillFigure = new PathFigure
            {
                StartPoint = new Windows.Foundation.Point(points[0].X, xAxisY),
                IsClosed = true,
                IsFilled = true
            };
            fillFigure.Segments.Add(new LineSegment { Point = points[0] });

            if (points.Count > 1)
            {
                for (int i = 0; i < points.Count - 1; i++)
                {
                    var p0 = points[i];
                    var p1 = points[i + 1];
                    double ctrlX1 = p0.X + (p1.X - p0.X) / 3.0;
                    double ctrlY1 = p0.Y;
                    double ctrlX2 = p0.X + 2.0 * (p1.X - p0.X) / 3.0;
                    double ctrlY2 = p1.Y;

                    fillFigure.Segments.Add(new BezierSegment
                    {
                        Point1 = new Windows.Foundation.Point(ctrlX1, ctrlY1),
                        Point2 = new Windows.Foundation.Point(ctrlX2, ctrlY2),
                        Point3 = p1
                    });
                }
            }

            fillFigure.Segments.Add(new LineSegment { Point = new Windows.Foundation.Point(points.Last().X, xAxisY) });
            fillGeometry.Figures.Add(fillFigure);

            var fillPath = new Path
            {
                Data = fillGeometry,
                Fill = fillBrush
            };
            MoodChartCanvas.Children.Add(fillPath);

            // Curve path geometry
            var lineGeometry = new PathGeometry();
            var lineFigure = new PathFigure
            {
                StartPoint = points[0],
                IsClosed = false,
                IsFilled = false
            };

            if (points.Count > 1)
            {
                for (int i = 0; i < points.Count - 1; i++)
                {
                    var p0 = points[i];
                    var p1 = points[i + 1];
                    double ctrlX1 = p0.X + (p1.X - p0.X) / 3.0;
                    double ctrlY1 = p0.Y;
                    double ctrlX2 = p0.X + 2.0 * (p1.X - p0.X) / 3.0;
                    double ctrlY2 = p1.Y;

                    lineFigure.Segments.Add(new BezierSegment
                    {
                        Point1 = new Windows.Foundation.Point(ctrlX1, ctrlY1),
                        Point2 = new Windows.Foundation.Point(ctrlX2, ctrlY2),
                        Point3 = p1
                    });
                }
            }
            lineGeometry.Figures.Add(lineFigure);

            var curvePath = new Path
            {
                Data = lineGeometry,
                Stroke = lineBrush,
                StrokeThickness = 3
            };
            MoodChartCanvas.Children.Add(curvePath);

            // Labels and points
            int labelStep = Math.Max(1, pointCount / 6);

            for (int i = 0; i < pointCount; i++)
            {
                var pt = points[i];
                var data = chartData[i];

                // X-axis date labels
                if (i % labelStep == 0 || i == pointCount - 1)
                {
                    var xLabel = new TextBlock
                    {
                        Text = data.DateLabel,
                        FontSize = 10,
                        Foreground = textBrush
                    };
                    xLabel.Measure(new Windows.Foundation.Size(80, 20));
                    double textW = xLabel.DesiredSize.Width;
                    Canvas.SetLeft(xLabel, pt.X - textW / 2);
                    Canvas.SetTop(xLabel, xAxisY + 8);
                    MoodChartCanvas.Children.Add(xLabel);

                    // X tick
                    var tick = new Line
                    {
                        X1 = pt.X,
                        Y1 = xAxisY,
                        X2 = pt.X,
                        Y2 = xAxisY + 4,
                        Stroke = gridBrush,
                        StrokeThickness = 1
                    };
                    MoodChartCanvas.Children.Add(tick);
                }

                // Hoverable data point circles
                var circle = new Ellipse
                {
                    Width = 8,
                    Height = 8,
                    Fill = lineBrush,
                    Stroke = GetThemeBrush("CardBackgroundFillColorDefaultBrush", "#FFFFFF"),
                    StrokeThickness = 1.5
                };
                Canvas.SetLeft(circle, pt.X - 4);
                Canvas.SetTop(circle, pt.Y - 4);

                ToolTipService.SetToolTip(circle, $"{data.DateLabel}\nMood: {data.Mood}");

                circle.PointerEntered += (s, e) =>
                {
                    if (s is Ellipse ell)
                    {
                        ell.Width = 12;
                        ell.Height = 12;
                        Canvas.SetLeft(ell, pt.X - 6);
                        Canvas.SetTop(ell, pt.Y - 6);
                        ell.StrokeThickness = 2;
                    }
                };
                circle.PointerExited += (s, e) =>
                {
                    if (s is Ellipse ell)
                    {
                        ell.Width = 8;
                        ell.Height = 8;
                        Canvas.SetLeft(ell, pt.X - 4);
                        Canvas.SetTop(ell, pt.Y - 4);
                        ell.StrokeThickness = 1.5;
                    }
                };

                MoodChartCanvas.Children.Add(circle);
            }
        }

        private int MapMoodToValue(string mood)
        {
            if (string.IsNullOrEmpty(mood)) return 0;
            if (mood.Contains("Happy")) return 5;
            if (mood.Contains("Neutral")) return 3;
            if (mood.Contains("Sad")) return 2;
            if (mood.Contains("Stressed")) return 2;
            if (mood.Contains("Angry")) return 1;
            return 0;
        }
    }
}
