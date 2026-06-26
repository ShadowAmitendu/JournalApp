using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media.Imaging;

namespace JournalApp
{
    public class BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is bool b)
            {
                return b ? Visibility.Visible : Visibility.Collapsed;
            }
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }

    public class StringToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            return string.IsNullOrEmpty(value as string) ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }

    public class ImagePathConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is string path && !string.IsNullOrEmpty(path))
            {
                try
                {
                    string absolutePath = JournalManager.Instance.GetAbsoluteMediaPath(path);
                    return new BitmapImage(new Uri(absolutePath));
                }
                catch
                {
                    return null;
                }
            }
            return null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }

    public class BoolToVisibilityInverseConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is bool b)
            {
                return b ? Visibility.Collapsed : Visibility.Visible;
            }
            return Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }

    public class HexToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is string hex && !string.IsNullOrEmpty(hex))
            {
                try
                {
                    if (hex.StartsWith("#"))
                        hex = hex.Substring(1);

                    byte a = 255;
                    byte r = 0, g = 0, b = 0;

                    if (hex.Length == 6)
                    {
                        r = System.Convert.ToByte(hex.Substring(0, 2), 16);
                        g = System.Convert.ToByte(hex.Substring(2, 2), 16);
                        b = System.Convert.ToByte(hex.Substring(4, 2), 16);
                    }
                    else if (hex.Length == 8)
                    {
                        a = System.Convert.ToByte(hex.Substring(0, 2), 16);
                        r = System.Convert.ToByte(hex.Substring(2, 2), 16);
                        g = System.Convert.ToByte(hex.Substring(4, 2), 16);
                        b = System.Convert.ToByte(hex.Substring(6, 2), 16);
                    }
                    else
                    {
                        return DependencyProperty.UnsetValue;
                    }

                    return new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(a, r, g, b));
                }
                catch
                {
                    return DependencyProperty.UnsetValue;
                }
            }
            return Microsoft.UI.Xaml.Application.Current.Resources["TextFillColorPrimaryBrush"];
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
