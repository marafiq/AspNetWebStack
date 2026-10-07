using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.ViewFeatures.Infrastructure;

namespace AspNetWebStack.Native
{
    // Typed scalar storage, not application JSON input or a replacement for the
    // JavaScriptSerializer wire contract. No CLR names or object activation.
    internal sealed class NativeTempDataSerializer : TempDataSerializer
    {
        private readonly int _bytes, _keys;
        internal NativeTempDataSerializer(int maximumSerializedBytes, int maximumKeys)
        {
            if (maximumSerializedBytes < 32 || maximumSerializedBytes > 2048) throw new ArgumentOutOfRangeException(nameof(maximumSerializedBytes));
            if (maximumKeys < 1 || maximumKeys > 64) throw new ArgumentOutOfRangeException(nameof(maximumKeys));
            _bytes = maximumSerializedBytes; _keys = maximumKeys;
        }
        public override bool CanSerializeType(Type type) => type == typeof(string) || type == typeof(int) || type == typeof(bool);
        public override byte[] Serialize(IDictionary<string, object> values)
        {
            ArgumentNullException.ThrowIfNull(values);
            var buffer = new ArrayBufferWriter<byte>();
            using var writer = new Utf8JsonWriter(buffer);
            writer.WriteStartObject();
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in values)
            {
                if (item.Key == null || item.Key.Length > _bytes || !keys.Add(item.Key) || keys.Count > _keys)
                    throw new InvalidOperationException("TempData keys exceed the bounded schema.");
                new UTF8Encoding(false, true).GetByteCount(item.Key);
                switch (item.Value)
                {
                    case null: writer.WriteNull(item.Key); break;
                    case string text when text.Length <= _bytes:
                        new UTF8Encoding(false, true).GetByteCount(text);
                        writer.WriteString(item.Key, text); break;
                    case int number: writer.WriteNumber(item.Key, number); break;
                    case bool flag: writer.WriteBoolean(item.Key, flag); break;
                    default: throw new InvalidOperationException("TempData supports only bounded string, Int32, Boolean and null values.");
                }
                writer.Flush();
                if (buffer.WrittenCount > _bytes) throw new InvalidOperationException("TempData serialized data exceeds the configured UTF-8 limit.");
            }
            writer.WriteEndObject(); writer.Flush();
            if (buffer.WrittenCount > _bytes) throw new InvalidOperationException("TempData serialized data exceeds the configured UTF-8 limit.");
            return buffer.WrittenSpan.ToArray();
        }
        public override IDictionary<string, object> Deserialize(byte[] value)
        {
            ArgumentNullException.ThrowIfNull(value);
            if (value.Length > _bytes) throw new InvalidDataException("TempData serialized data exceeds the configured UTF-8 limit.");
            using var document = JsonDocument.Parse(value, new JsonDocumentOptions { MaxDepth = 2 });
            var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (result.Count >= _keys) throw new InvalidDataException("TempData key count exceeds the configured limit.");
                object item = property.Value.ValueKind switch
                {
                    JsonValueKind.Null => null,
                    JsonValueKind.String => property.Value.GetString(),
                    JsonValueKind.Number => property.Value.GetInt32(),
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    _ => throw new InvalidDataException("TempData contains an unsupported value.")
                };
                result.Add(property.Name, item);
            }
            return result;
        }
    }
}
