using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace DigiCompassCloudRelay;

public static class ResendEmail
{
    public static async Task<string> SendAsync(string toEmail, string subject, string html)
    {
        using var http = new HttpClient();
        http.BaseAddress = new Uri("https://api.resend.com/");
        http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", RelayConfig.ResendApiKey);

        var payload = new
        {
            from = RelayConfig.ResendFrom,
            to = new[] { toEmail },
            subject,
            html
        };

        var json = JsonSerializer.Serialize(payload);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        using var resp = await http.PostAsync("emails", content);
        var body = await resp.Content.ReadAsStringAsync();

        if (!resp.IsSuccessStatusCode)
            throw new Exception($"Resend failed: {(int)resp.StatusCode} {body}");

        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("id").GetString() ?? "";
    }
}