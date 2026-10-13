using System.Globalization;
using System.Windows.Data;
using PocsagDecoder.Core.Filtering;

namespace PocsagDecoder.App.Converters;

/// <summary>Ist das Ziel (values[0]) in der Zielliste der Regel (values[1]) enthalten?</summary>
public sealed class TargetCheckedConverter : IMultiValueConverter
{
    public object Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture) =>
        values.Length == 2 && values[0] is string name && values[1] is string list &&
        FilterRule.SplitTargets(list).Contains(name, StringComparer.OrdinalIgnoreCase);

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
