using System;
using System.Collections;
using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Web;

namespace AspNetWebStack.Native;

// Internal MultiSelectList dependency, using native descriptors/indexers. The
// expression grammar follows Framework DataBinder.Eval (Reference Source
// ec9fa9ae770d522a5b5f0607898044b7478574a3, System.Web/UI/DataBinder.cs).
// This does not introduce a public WebForms DataBinder compatibility surface.
internal static class NativeSelectListEvaluator
{
    internal static object Eval(object value, string expression)
    {
        if (String.IsNullOrWhiteSpace(expression)) throw new ArgumentNullException(nameof(expression));
        foreach (string part in expression.Trim().Split('.'))
        {
            if (value == null) break;
            int start = part.IndexOfAny(new[] { '[', '(' });
            if (start < 0) { value = Property(value, part); continue; }
            int end = part.IndexOfAny(new[] { ']', ')' }, start + 1);
            if (end < 0 || end == start + 1) throw new ArgumentException("Invalid indexed expression.");
            string token = part.Substring(start + 1, end - start - 1).Trim();
            if (token.Length == 0) throw new ArgumentException("Invalid indexed expression.");
            object index = token;
            bool integer = false;
            if ((token[0] == '"' && token[token.Length - 1] == '"') || (token[0] == '\'' && token[token.Length - 1] == '\''))
                index = token.Substring(1, token.Length - 2);
            else if (Char.IsDigit(token[0]) && Int32.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number))
            { index = number; integer = true; }
            if (start != 0) value = Property(value, part.Substring(0, start));
            if (value == null) continue;
            if (integer && value is Array array) value = array.GetValue((int)index);
            else if (integer && value is IList list) value = list[(int)index];
            else
            {
                var item = value.GetType().GetProperty("Item", BindingFlags.Public | BindingFlags.Instance, null, null, new[] { index.GetType() }, null);
                if (item == null) throw new ArgumentException("The selected property has no matching indexed accessor.");
                value = item.GetValue(value, new[] { index });
            }
        }
        return value;
    }

    private static object Property(object value, string name)
    {
        if (String.IsNullOrEmpty(name)) throw new ArgumentNullException("propName");
        var properties = value is ICustomTypeDescriptor ? TypeDescriptor.GetProperties(value) : TypeDescriptor.GetProperties(value.GetType());
        var property = properties.Find(name, ignoreCase: true);
        if (property == null) throw new HttpException("The select-list data field was not found: " + name);
        return property.GetValue(value);
    }
}
