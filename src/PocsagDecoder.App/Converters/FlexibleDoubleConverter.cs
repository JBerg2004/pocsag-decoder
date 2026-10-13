using System.Globalization;
using System.Windows.Data;

namespace PocsagDecoder.App.Converters;

/// <summary>Akzeptiert Komma und Punkt als Dezimaltrenner; leer ergibt null (z.B. Auto-Gain).</summary>
public sealed class FlexibleDoubleConverter : IValueConverter
{
    public string Format { get; set; } = "0.######";

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is double d ? d.ToString(Format, CultureInfo.InvariantCulture) : string.Empty;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var s = (value as string ?? string.Empty).Trim().Replace(',', '.');
        if (s.Length == 0)
            return Nullable.GetUnderlyingType(targetType) is not null ? null : Binding.DoNothing;
        return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)
            ? d
            : System.Windows.DependencyProperty.UnsetValue;
    }
}

/// <summary>Enum-Wert &lt;-&gt; RadioButton/Bool.</summary>
public sealed class EnumEqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value?.ToString() == parameter?.ToString();

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true && parameter is not null ? Enum.Parse(targetType, parameter.ToString()!) : Binding.DoNothing;
}

public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;
}
