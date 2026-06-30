using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;

namespace JournalApp
{
    public sealed partial class MainPage : Page
    {
        private void TutorialNextButton_Click(object sender, RoutedEventArgs e)
        {
            if (TutorialFlipView == null) return;
            
            int current = TutorialFlipView.SelectedIndex;
            if (current < 3)
            {
                TutorialFlipView.SelectedIndex = current + 1;
            }
            else
            {
                CompleteOnboardingTutorial();
            }
        }

        private void TutorialBackButton_Click(object sender, RoutedEventArgs e)
        {
            if (TutorialFlipView == null) return;
            
            int current = TutorialFlipView.SelectedIndex;
            if (current > 0)
            {
                TutorialFlipView.SelectedIndex = current - 1;
            }
        }

        private void TutorialFlipView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (TutorialFlipView == null || TutorialBackButton == null || TutorialNextButton == null) return;
            
            int index = TutorialFlipView.SelectedIndex;
            TutorialBackButton.IsEnabled = index > 0;
            TutorialNextButton.Content = index == 3 ? "Get Started" : "Next";

            if (Dot1 != null) Dot1.Opacity = index == 0 ? 1.0 : 0.4;
            if (Dot2 != null) Dot2.Opacity = index == 1 ? 1.0 : 0.4;
            if (Dot3 != null) Dot3.Opacity = index == 2 ? 1.0 : 0.4;
            if (Dot4 != null) Dot4.Opacity = index == 3 ? 1.0 : 0.4;
        }

        private void TutorialDialog_CloseButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
            CompleteOnboardingTutorial();
        }

        private void CompleteOnboardingTutorial()
        {
            try
            {
                var localSettings = Windows.Storage.ApplicationData.Current.LocalSettings;
                localSettings.Values["HasCompletedOnboardingTutorial"] = true;
            }
            catch {}
            
            if (TutorialDialog != null)
            {
                TutorialDialog.Hide();
            }
        }

        private async void CheckFirstTimeTutorial()
        {
            try
            {
                var localSettings = Windows.Storage.ApplicationData.Current.LocalSettings;
                if (!localSettings.Values.ContainsKey("HasCompletedOnboardingTutorial"))
                {
                    if (TutorialDialog != null)
                    {
                        // Reset FlipView state before showing
                        if (TutorialFlipView != null) TutorialFlipView.SelectedIndex = 0;
                        await TutorialDialog.ShowAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to show tutorial: {ex.Message}");
            }
        }
    }
}
