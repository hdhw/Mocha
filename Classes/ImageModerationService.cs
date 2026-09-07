using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Mocha2023.Classes
{
    // Best-effort automated screen for explicit photographic content on image
    // uploads, using Sightengine's nudity model (https://sightengine.com).
    //
    // IMPORTANT LIMITATION: this and every mainstream NSFW-detection API are
    // trained almost entirely on photographs of real people. They are known
    // to perform poorly on stylized/anime/furry-style art. This check is real
    // protection against photographic explicit content, but it is not a
    // guarantee against stylized explicit content - that still relies on the
    // existing Discord upload alert and human moderation as a backstop.
    //
    // Off by default: if SIGHTENGINE_API_USER/SIGHTENGINE_API_SECRET aren't
    // configured, this fails open (no check performed) rather than blocking
    // the upload feature entirely, matching VpnDetectionService's pattern for
    // optional external checks elsewhere in this codebase.
    public static class ImageModerationService
    {
        private static readonly HttpClient Http = new()
        {
            Timeout = TimeSpan.FromSeconds(8)
        };

        // Matched by substring against the response's property names so this
        // isn't locked to one exact response shape/model version - Sightengine
        // documents these as the nudity-2.1 intensity classes.
        private static readonly string[] ExplicitScoreKeys =
        {
            "sexual_activity",
            "sexual_display",
            "erotica",
            "very_suggestive",
            "porn",
            "raw"
        };

        private const double RejectThreshold = 0.5;

        public static bool IsConfigured =>
            !string.IsNullOrWhiteSpace(Program.LoadLocalSetting("SIGHTENGINE_API_USER")) &&
            !string.IsNullOrWhiteSpace(Program.LoadLocalSetting("SIGHTENGINE_API_SECRET"));

        public static async Task<(bool isExplicit, double score, string? error)> CheckAsync(
            byte[] imageBytes,
            string fileName,
            CancellationToken cancellationToken)
        {
            string apiUser = Program.LoadLocalSetting("SIGHTENGINE_API_USER").Trim();
            string apiSecret = Program.LoadLocalSetting("SIGHTENGINE_API_SECRET").Trim();
            if (string.IsNullOrWhiteSpace(apiUser) || string.IsNullOrWhiteSpace(apiSecret))
                return (false, 0, null);

            try
            {
                using var form = new MultipartFormDataContent();
                form.Add(new ByteArrayContent(imageBytes), "media", fileName);
                form.Add(new StringContent("nudity-2.1"), "models");
                form.Add(new StringContent(apiUser), "api_user");
                form.Add(new StringContent(apiSecret), "api_secret");

                using HttpResponseMessage response = await Http.PostAsync(
                    "https://api.sightengine.com/1.0/check.json",
                    form,
                    cancellationToken);

                string body = await response.Content.ReadAsStringAsync(cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine(
                        $"[IMAGE MODERATION] API returned {(int)response.StatusCode}: {Truncate(body, 500)}");
                    return (false, 0, "moderation_api_error");
                }

                using JsonDocument document = JsonDocument.Parse(body);
                double maxScore = FindMaxExplicitScore(document.RootElement, null);

                // Logged every time (not just on a hit) so the setup can be
                // verified against a real test image - if this always prints
                // maxScore=0.000 for an obviously explicit test upload, the
                // response schema has drifted and this check needs revisiting.
                Console.WriteLine(
                    $"[IMAGE MODERATION] file={fileName} maxScore={maxScore:0.000} " +
                    $"flagged={(maxScore >= RejectThreshold)} raw={Truncate(body, 800)}");

                return (maxScore >= RejectThreshold, maxScore, null);
            }
            catch (Exception ex) when (
                ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                Console.WriteLine(
                    $"[IMAGE MODERATION] Check failed, allowing upload through: " +
                    $"{ex.GetType().Name}: {ex.Message}");
                return (false, 0, "moderation_check_failed");
            }
        }

        private static double FindMaxExplicitScore(JsonElement element, string? propertyName)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                double max = 0;
                foreach (JsonProperty property in element.EnumerateObject())
                    max = Math.Max(max, FindMaxExplicitScore(property.Value, property.Name));
                return max;
            }

            if (element.ValueKind == JsonValueKind.Number &&
                propertyName != null &&
                ExplicitScoreKeys.Any(key =>
                    propertyName.Contains(key, StringComparison.OrdinalIgnoreCase)) &&
                element.TryGetDouble(out double value))
            {
                return value;
            }

            return 0;
        }

        private static string Truncate(string value, int length) =>
            value.Length > length ? value[..length] + "..." : value;
    }
}
