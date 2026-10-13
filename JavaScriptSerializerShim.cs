// Compatibility shim: lets the unchanged QuietWardServer.cs (written for Windows .NET Framework)
// compile on modern cross-platform .NET. Replaces System.Web.Script.Serialization.JavaScriptSerializer.
using System;
using System.Collections.Generic;
using System.Text.Json;

namespace System.Web.Script.Serialization
{
    public sealed class JavaScriptSerializer
    {
        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            IncludeFields = true,
            PropertyNameCaseInsensitive = true
        };

        public int MaxJsonLength { get; set; }

        public JavaScriptSerializer()
        {
            MaxJsonLength = 2097152;
        }

        public string Serialize(object value)
        {
            if (value == null) return "null";
            return JsonSerializer.Serialize(value, value.GetType(), Options);
        }

        public T Deserialize<T>(string input)
        {
            if (input == null) return default(T);
            if (input.Length > MaxJsonLength)
                throw new ArgumentException("The JSON string exceeds the maximum allowed length.");

            if (typeof(T) == typeof(Dictionary<string, object>) || typeof(T) == typeof(object))
            {
                using (JsonDocument document = JsonDocument.Parse(input))
                    return (T)ToPlainObject(document.RootElement);
            }
            return JsonSerializer.Deserialize<T>(input, Options);
        }

        // Mirrors what JavaScriptSerializer returned: Dictionary<string,object>, object[], string, int/long/double, bool, null.
        private static object ToPlainObject(JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    var dictionary = new Dictionary<string, object>();
                    foreach (JsonProperty property in element.EnumerateObject())
                        dictionary[property.Name] = ToPlainObject(property.Value);
                    return dictionary;
                case JsonValueKind.Array:
                    var list = new List<object>();
                    foreach (JsonElement item in element.EnumerateArray())
                        list.Add(ToPlainObject(item));
                    return list.ToArray();
                case JsonValueKind.String:
                    return element.GetString();
                case JsonValueKind.Number:
                    int integer;
                    if (element.TryGetInt32(out integer)) return integer;
                    long bigInteger;
                    if (element.TryGetInt64(out bigInteger)) return bigInteger;
                    return element.GetDouble();
                case JsonValueKind.True:
                    return true;
                case JsonValueKind.False:
                    return false;
                default:
                    return null;
            }
        }
    }
}
