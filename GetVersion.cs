using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace YMM4FileExplorer
{
    public static class GetVersion
    {
        public static string GetPluginVersion()
        {
            return Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0";
        }

        public static async Task<string?> GetPluginVersionAsync(string pluginName)
        {
            try
            {
                using HttpClient client = new();
                string url = $"https://ymme.ymm4-info.net/api/get?q={pluginName}";
                string json = await client.GetStringAsync(url);
                using var doc = JsonDocument.Parse(json);

                if (doc.RootElement.TryGetProperty(pluginName, out var pluginElement))
                {
                    if (pluginElement.TryGetProperty("version", out var versionElement))
                    {
                        return versionElement.GetString();
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[GetVersion] バージョン取得失敗: {ex.Message}");
                return null;
            }

            return null;
        }

        public static async Task<bool> CheckVersionAsync(string pluginName)
        {
            var localVersion = new Version(GetPluginVersion());
            string? latestVersionStr = await GetPluginVersionAsync(pluginName);
            if (string.IsNullOrEmpty(latestVersionStr))
                return false;

            var latestVersion = new Version(latestVersionStr);

            return latestVersion > localVersion;
        }
    }
}
