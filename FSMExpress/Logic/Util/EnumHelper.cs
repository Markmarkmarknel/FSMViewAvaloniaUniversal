using System;
using System.Linq;
using Avalonia.Data.Converters;

namespace FSMExpress.Logic.Util;

public static class EnumHelper
{
    public static readonly IValueConverter GetValues = new FuncValueConverter<Type, Array>(type =>
    {
        if (type?.IsEnum == true)
            return Enum.GetValues(type);
        return Array.Empty<object>();
    });
}