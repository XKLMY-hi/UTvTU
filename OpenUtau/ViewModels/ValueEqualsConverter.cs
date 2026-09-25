using System;
using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace OpenUtau.App.ViewModels {
    /// <summary>
    /// 值 ↔ 布尔（分段控件的 RadioButton 用）：<c>ConverterParameter</c> 是该选项的值。
    /// 只处理 int / string 两类（本应用的分段控件就是索引与主题名）。
    /// 未选中时不写回（返回 <see cref="BindingOperations.DoNothing"/>），避免点已选项把值清掉。
    /// </summary>
    public class ValueEqualsConverter : IValueConverter {
        public static readonly ValueEqualsConverter Instance = new();

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) {
            if (value == null || parameter == null) {
                return false;
            }
            return string.Equals(value.ToString(), parameter.ToString(), StringComparison.Ordinal);
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) {
            if (value is not true || parameter == null) {
                return BindingOperations.DoNothing;
            }
            string text = parameter.ToString() ?? string.Empty;
            // 目标属性可能是 int（索引）或 string（主题名）
            if (targetType == typeof(int) || targetType == typeof(int?)) {
                return int.TryParse(text, out int index) ? index : BindingOperations.DoNothing;
            }
            return text;
        }
    }
}
