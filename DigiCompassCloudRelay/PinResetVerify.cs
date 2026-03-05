using Azure;
using Azure.Data.Tables;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using System.Net;
using System.Text.Json;

namespace DigiCompassCloudRelay;

public sealed class PinResetVerifyFn
{
    private const string PinResetCodesTable = "PinResetCodes";

    [Function("PinResetVerify")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "v1/pin-reset/verify")] HttpRequestData req)
    {
        // Auth
        var relayKey = req.Headers.TryGetValues("X-Relay-Key", out var vals) ? vals.FirstOrDefault() : null;
        if (!SecurityHelpers.RelayKeyValid(relayKey))
            return req.CreateResponse(HttpStatusCode.Unauthorized);

        // Parse
        var payload = await JsonSerializer.DeserializeAsync<VerifyDto>(req.Body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (payload is null || string.IsNullOrWhiteSpace(payload.DeviceId) || string.IsNullOrWhiteSpace(payload.Code))
            return await Json(req, HttpStatusCode.BadRequest, "{\"error\":\"deviceId and code required\"}");

        var deviceId = payload.DeviceId.Trim();
        var code = payload.Code.Trim();
        var codeHash = SecurityHelpers.Sha256Hex(code);

        var now = DateTimeOffset.UtcNow;
        var pinCodes = TableStore.Get(PinResetCodesTable);

        // Query by PartitionKey (device) and scan (low volume, OK for MVP)
        // PK used in request endpoint: "device:{deviceId}"
        var pk = $"device:{deviceId}";

        TableEntity? match = null;

        await foreach (var e in pinCodes.QueryAsync<TableEntity>(x => x.PartitionKey == pk))
        {
            var consumedUtc = e.GetString("ConsumedUtc") ?? "";
            if (!string.IsNullOrEmpty(consumedUtc)) continue;

            var expiresUtcStr = e.GetString("ExpiresUtc") ?? "";
            if (string.IsNullOrEmpty(expiresUtcStr)) continue;

            if (!DateTimeOffset.TryParse(expiresUtcStr, out var expiresUtc)) continue;
            if (expiresUtc < now) continue;

            var storedHash = e.GetString("CodeHash") ?? "";
            if (!storedHash.Equals(codeHash, StringComparison.OrdinalIgnoreCase)) continue;

            match = e;
            break;
        }

        if (match is null)
            return await Json(req, HttpStatusCode.BadRequest,
                "{\"error\":\"invalid_or_expired_code\",\"message\":\"This code is incorrect or has expired. Please request a new code from the app and try again.\"}");

        // Issue reset token (short-lived)
        var resetToken = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_'); // base64url-ish
        var resetTokenHash = SecurityHelpers.Sha256Hex(resetToken);
        var resetTokenExpiresUtc = now.AddMinutes(15).ToString("O");

        match["ConsumedUtc"] = now.ToString("O");
        match["ResetTokenHash"] = resetTokenHash;
        match["ResetTokenExpiresUtc"] = resetTokenExpiresUtc;

        await pinCodes.UpdateEntityAsync(match, match.ETag, TableUpdateMode.Replace);

        return await Json(req, HttpStatusCode.OK,
            $"{{\"resetToken\":\"{Escape(resetToken)}\",\"expiresUtc\":\"{Escape(resetTokenExpiresUtc)}\"}}");
    }

    private static async Task<HttpResponseData> Json(HttpRequestData req, HttpStatusCode code, string json)
    {
        var r = req.CreateResponse(code);
        r.Headers.Add("Content-Type", "application/json");
        await r.WriteStringAsync(json);
        return r;
    }

    private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
    private sealed record VerifyDto(string DeviceId, string Code);
}