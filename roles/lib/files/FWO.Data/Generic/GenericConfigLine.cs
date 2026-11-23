using System.Collections;
using System.Globalization;
using System.Text.Json;
using Newtonsoft.Json.Linq;

namespace FWO.Data.Generic
{
    /// <summary>
    /// Reads values of the config line of a generic config item by dot-separated key paths.
    /// The nested values are tokens of whichever JSON deserializer read the item (Newtonsoft or System.Text.Json).
    /// The config line has the same core fields for all device types (the k*Field constants);
    /// vendor-specific values are in <see cref="kAttributesField"/>, the original object of the device in <see cref="kRawField"/>.
    /// </summary>
    public static class GenericConfigLine
    {
        /// <summary>Where the item lives on the device (rule set and group path, partition, policy, ...).</summary>
        public const string kScopeField = "scope";
        /// <summary>Whether the item is in effect, including everything it inherits.</summary>
        public const string kEnabledField = "enabled";
        /// <summary>Readable form of what the item applies to.</summary>
        public const string kMatchField = "match";
        /// <summary>Readable form of what the item does.</summary>
        public const string kActionField = "action";
        /// <summary>Vendor-specific normalized values.</summary>
        public const string kAttributesField = "attributes";
        /// <summary>Original object of the device.</summary>
        public const string kRawField = "raw";

        private const char kPathSeparator = '.';
        private const string kListSeparator = ", ";
        private const int kMaxPathDepth = 100;

        /// <summary>
        /// Returns the value at a dot-separated path, or null if any part of the path is missing.
        /// </summary>
        public static object? GetValue(Dictionary<string, object?> configLine, string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }
            string[] parts = path.Split(kPathSeparator);
            if (parts.Length > kMaxPathDepth || !configLine.TryGetValue(parts[0], out object? current))
            {
                return null;
            }
            for (int index = 1; index < parts.Length && current != null; index++)
            {
                current = GetChild(current, parts[index]);
            }
            return current;
        }

        /// <summary>
        /// Returns the value at a dot-separated path as display text; missing values give an empty string.
        /// </summary>
        public static string GetDisplayValue(Dictionary<string, object?> configLine, string? path)
        {
            return ToDisplayString(GetValue(configLine, path));
        }

        /// <summary>
        /// Converts a JSON value to display text: scalars as text, lists of scalars comma separated,
        /// objects as compact JSON.
        /// </summary>
        public static string ToDisplayString(object? value)
        {
            return value switch
            {
                null => "",
                string text => text,
                bool flag => flag ? "true" : "false",
                JValue jsonValue => Convert.ToString(jsonValue.Value, CultureInfo.InvariantCulture) ?? "",
                JArray jsonArray => FormatList(jsonArray.Select(token => (object?)token)),
                JToken jsonToken => jsonToken.ToString(Newtonsoft.Json.Formatting.None),
                JsonElement element => FormatJsonElement(element),
                IDictionary dictionary => System.Text.Json.JsonSerializer.Serialize(dictionary),
                IEnumerable list => FormatList(list.Cast<object?>()),
                _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? ""
            };
        }

        /// <summary>
        /// Returns the whole config line as indented JSON for the detailed view.
        /// </summary>
        public static string ToIndentedJson(Dictionary<string, object?> configLine)
        {
            return JObject.FromObject(configLine).ToString(Newtonsoft.Json.Formatting.Indented);
        }

        private static object? GetChild(object parent, string key)
        {
            return parent switch
            {
                IDictionary<string, object?> dictionary => dictionary.TryGetValue(key, out object? child) ? child : null,
                JObject jsonObject => jsonObject.TryGetValue(key, out JToken? token) ? token : null,
                JsonElement { ValueKind: JsonValueKind.Object } element => element.TryGetProperty(key, out JsonElement child) ? child : null,
                _ => null
            };
        }

        private static string FormatJsonElement(JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.String => element.GetString() ?? "",
                JsonValueKind.Null or JsonValueKind.Undefined => "",
                JsonValueKind.Array => FormatList(element.EnumerateArray().Select(item => (object?)item)),
                _ => element.GetRawText()
            };
        }

        private static string FormatList(IEnumerable<object?> values)
        {
            return string.Join(kListSeparator, values.Select(ToDisplayString));
        }
    }
}
