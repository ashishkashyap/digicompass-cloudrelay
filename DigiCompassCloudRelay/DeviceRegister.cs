using Azure;
using Azure.Data.Tables;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using System.Net;
using System.Text.Json;

namespace DigiCompassCloudRelay;

public sealed class DeviceRegisterFn
{
    private const string DevicesTable = "Devices";

    [Function("DeviceRegister")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "v1/device/register")] HttpRequestData req)
    {
        var relayKey = req.Headers.TryGetValues("X-Relay-Key", out var vals) ? vals.FirstOrDefault() : null;
        if (!SecurityHelpers.RelayKeyValid(relayKey))
            return req.CreateResponse(HttpStatusCode.Unauthorized);

        var payload = await JsonSerializer.DeserializeAsync<RegisterDto>(req.Body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (payload is null || string.IsNullOrWhiteSpace(payload.DeviceId) || string.IsNullOrWhiteSpace(payload.ParentEmail))
            return await Json(req, HttpStatusCode.BadRequest, "{\"error\":\"deviceId and parentEmail required\"}");

        var deviceId = payload.DeviceId.Trim();
        var parentEmail = payload.ParentEmail.Trim();

        var devices = TableStore.Get(DevicesTable);

        var now = DateTimeOffset.UtcNow.ToString("O");
        var entity = new TableEntity($"device:{deviceId}", "v1")
        {
            { "DeviceId", deviceId },
            { "ParentEmail", parentEmail },
            { "UpdatedUtc", now }
        };

        // Upsert so parent can change email later
        await devices.UpsertEntityAsync(entity, TableUpdateMode.Replace);

        return await Json(req, HttpStatusCode.OK, "{\"ok\":true}");
    }

    private static async Task<HttpResponseData> Json(HttpRequestData req, HttpStatusCode code, string json)
    {
        var r = req.CreateResponse(code);
        r.Headers.Add("Content-Type", "application/json");
        await r.WriteStringAsync(json);
        return r;
    }

    private sealed class RegisterDto
    {
        public string? DeviceId { get; set; }
        public string? ParentEmail { get; set; }
    }
}