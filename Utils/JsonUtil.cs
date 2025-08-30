using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DJWinOptimizer.Utils
{
    public static class JsonUtil
    {
        private static readonly JsonSerializerOptions Options = new()
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNameCaseInsensitive = true
        };

        public static T? Load<T>(string path)
        {
            if (!File.Exists(path)) return default;
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<T>(json, Options);
        }

        public static void Save<T>(string path, T obj)
        {
            var json = JsonSerializer.Serialize(obj, Options);
            File.WriteAllText(path, json);
        }
    }
}
