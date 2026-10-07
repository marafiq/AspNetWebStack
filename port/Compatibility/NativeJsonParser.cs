using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Web;

namespace AspNetWebStack.Native
{
    // Native JSON syntax parsing; only dictionaries, lists and primitive values
    // reach original JsonValueProviderFactory. No model/type activation here.
    internal static class NativeJsonParser
    {
        internal static object Parse(string text, int maximumDepth = 16, int maximumValues = 256,
            Action<string, string> validate = null, Action check = null)
        {
            ArgumentNullException.ThrowIfNull(text);
            try
            {
                if (new UTF8Encoding(false, true).GetByteCount(text) > 65536)
                    throw new HttpException(413, "JSON body limit exceeded.");
                if (text.Length == 0 || text.Trim(' ', '\t', '\r', '\n').Length == 0) return null;
                using var document = JsonDocument.Parse(text, new JsonDocumentOptions
                    { MaxDepth = maximumDepth, CommentHandling = JsonCommentHandling.Disallow, AllowTrailingCommas = false });
                int count = 0;
                return Read(document.RootElement, String.Empty);

                object Read(JsonElement element, string path)
                {
                    check?.Invoke();
                    if (++count > maximumValues) throw new HttpException(400, "JSON value limit exceeded.");
                    switch (element.ValueKind)
                    {
                        case JsonValueKind.Object:
                            var dictionary = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                            foreach (var property in element.EnumerateObject())
                            {
                                string name = DecodeString(() => property.Name);
                                if (name.Length == 0 || name.IndexOfAny(new[] { '.', '[', ']' }) >= 0 ||
                                    String.Equals(name, "__type", StringComparison.OrdinalIgnoreCase) || dictionary.ContainsKey(name))
                                    throw new HttpException(400, "JSON names must be unique and unambiguous MVC property names without type metadata.");
                                string childPath = path.Length == 0 ? name : path + "." + name;
                                Validate(childPath, null);
                                dictionary.Add(name, Read(property.Value, childPath));
                            }
                            return dictionary;
                        case JsonValueKind.Array:
                            var list = new ArrayList();
                            foreach (var item in element.EnumerateArray())
                                list.Add(Read(item, path + "[" + list.Count.ToString(CultureInfo.InvariantCulture) + "]"));
                            return list;
                        case JsonValueKind.String:
                            string value = DecodeString(element.GetString);
                            if (value.StartsWith("/Date(", StringComparison.Ordinal))
                                throw new HttpException(400, "Legacy JSON date tokens are outside this input boundary; use an explicit date string.");
                            Validate(path, value);
                            return value;
                        case JsonValueKind.Number:
                            string token = element.GetRawText();
                            // Match the original serializer's numeric preference:
                            // exponent -> Double; otherwise Int32/Int64/Decimal/Double.
                            if (token.IndexOfAny(new[] { 'e', 'E' }) < 0)
                            {
                                if (!token.Contains('.'))
                                {
                                    if (Int32.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out int integer)) return integer;
                                    if (Int64.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out long large)) return large;
                                }
                                if (Decimal.TryParse(token, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal precise)) return precise;
                            }
                            if (Double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double floating) && Double.IsFinite(floating)) return floating;
                            throw new HttpException(400, "JSON number is outside the finite supported range.");
                        case JsonValueKind.True: return true;
                        case JsonValueKind.False: return false;
                        case JsonValueKind.Null: return null;
                        default: throw new HttpException(400, "Unsupported JSON value.");
                    }
                }
                void Validate(string name, string value)
                { validate?.Invoke(name, value); check?.Invoke(); }
            }
            catch (JsonException) { throw new HttpException(400, "Malformed or excessively nested JSON."); }
            catch (EncoderFallbackException) { throw new HttpException(400, "Malformed JSON Unicode."); }
        }
        private static string DecodeString(Func<string> read)
        {
            // JsonDocument accepts escaped code units before GetString checks
            // surrogate pairing. Translate only that parser operation's error.
            try { string value = read(); new UTF8Encoding(false, true).GetByteCount(value); return value; }
            catch (InvalidOperationException) { throw new HttpException(400, "Malformed JSON Unicode."); }
        }
    }
}
