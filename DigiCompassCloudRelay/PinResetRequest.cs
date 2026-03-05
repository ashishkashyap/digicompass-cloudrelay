using Azure;
using Azure.Data.Tables;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using System.Net;
using System.Text.Json;

namespace DigiCompassCloudRelay;

public sealed class PinResetRequestFn
{
    private const string DevicesTable = "Devices";
    private const string PinResetCodesTable = "PinResetCodes";

    [Function("PinResetRequest")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "v1/pin-reset/request")] HttpRequestData req)
    {
        // Auth (laptop/service only)
        var relayKey = req.Headers.TryGetValues("X-Relay-Key", out var vals) ? vals.FirstOrDefault() : null;
        if (!SecurityHelpers.RelayKeyValid(relayKey))
            return req.CreateResponse(HttpStatusCode.Unauthorized);

        // Parse body
        var payload = await JsonSerializer.DeserializeAsync<RequestDto>(
            req.Body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        var deviceId = payload?.deviceId?.Trim();
        if (string.IsNullOrWhiteSpace(deviceId))
            return await Json(req, HttpStatusCode.BadRequest, "{\"error\":\"deviceId required\"}");

        // Load device -> parent email
        var devices = TableStore.Get(DevicesTable);

        TableEntity device;
        try
        {
            device = (await devices.GetEntityAsync<TableEntity>($"device:{deviceId}", "v1")).Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return await Json(req, HttpStatusCode.NotFound, "{\"error\":\"device_not_registered\"}");
        }

        var parentEmail = device.GetString("ParentEmail") ?? device.GetString("parentEmail");
        if (string.IsNullOrWhiteSpace(parentEmail))
            return await Json(req, HttpStatusCode.BadRequest, "{\"error\":\"parentEmail missing on device\"}");

        // Generate code + store hash
        var code = SecurityHelpers.NewSixDigitCode();
        var codeHash = SecurityHelpers.Sha256Hex(code);
        var now = DateTimeOffset.UtcNow;
        var expires = now.AddMinutes(10);

        var pinCodes = TableStore.Get(PinResetCodesTable);

        var entity = new TableEntity($"device:{deviceId}", $"code:{now.ToUnixTimeMilliseconds()}")
        {
            { "DeviceId", deviceId },
            { "CodeHash", codeHash },
            { "ExpiresUtc", expires.ToString("O") },
            { "ConsumedUtc", "" }
        };

        await pinCodes.AddEntityAsync(entity);

        // Email parent
        var subject = "DigiKids PIN Reset Code";
        var html =
            $"<p>Your DigiKids PIN reset code is:</p>" +
            $"<h2 style=\"letter-spacing:2px\">{code}</h2>" +
            $"<p>This code expires in 10 minutes.</p>";

        await ResendEmail.SendAsync(parentEmail.Trim(), subject, html);

        return await Json(req, HttpStatusCode.OK, "{\"sent\":true}");
    }

    private static async Task<HttpResponseData> Json(HttpRequestData req, HttpStatusCode code, string json)
    {
        var r = req.CreateResponse(code);
        r.Headers.Add("Content-Type", "application/json");
        await r.WriteStringAsync(json);
        return r;
    }

    private sealed class RequestDto
    {
        public string? deviceId { get; set; }
    }
}