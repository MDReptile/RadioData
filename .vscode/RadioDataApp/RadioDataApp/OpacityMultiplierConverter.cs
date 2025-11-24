using System;
using System.Globalization;
using System.Windows.Data;

namespace RadioDataApp
{
    public class OpacityMultiplierConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double volume)
            {
                // Map volume (0.0 - 1.0) to opacity (0.3 - 1.0)
                // If volume is very low (< 0.1), make it dim (0.3)
                // If volume is high, make it full (1.0)

                if (volume < 0.1) return 0.3;
                return 0.3 + (volume * 0.7);
            }
            return 1.0;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
