using Azure.Data.Tables;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using System.Net;
using System.Text.Json;

namespace DigiCompassCloudRelay;

public sealed class ApprovalRequestFn
{
    private const string DevicesTable = "Devices";
    private const string ApprovalRequestsTable = "ApprovalRequests";
    private const string ApprovalTokensTable = "ApprovalTokens";

    [Function("ApprovalRequest")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "v1/approval/request")] HttpRequestData req)
    {
        var relayKey = req.Headers.TryGetValues("X-Relay-Key", out var vals) ? vals.FirstOrDefault() : null;
        if (!SecurityHelpers.RelayKeyValid(relayKey))
            return req.CreateResponse(HttpStatusCode.Unauthorized);

        var payload = await JsonSerializer.DeserializeAsync<ApprovalDto>(req.Body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (payload is null ||
            string.IsNullOrWhiteSpace(payload.DeviceId) ||
            string.IsNullOrWhiteSpace(payload.RequestId) ||
            string.IsNullOrWhiteSpace(payload.ChildId) ||
            string.IsNullOrWhiteSpace(payload.Domain) ||
            payload.RequestedMinutes <= 0)
        {
            return await Json(req, HttpStatusCode.BadRequest,
                "{\"error\":\"deviceId, requestId, childId, domain, requestedMinutes required\"}");
        }

        var deviceId = payload.DeviceId.Trim();
        var requestId = payload.RequestId.Trim();
        var childId = payload.ChildId.Trim();
        var domain = payload.Domain.Trim();

        // Lookup parent email
        var devices = TableStore.Get(DevicesTable);
        TableEntity device;
        try
        {
            device = (await devices.GetEntityAsync<TableEntity>($"device:{deviceId}", "v1")).Value;
        }
        catch
        {
            return await Json(req, HttpStatusCode.NotFound, "{\"error\":\"device_not_registered\"}");
        }

        var parentEmail = device.GetString("ParentEmail");
        if (string.IsNullOrWhiteSpace(parentEmail))
            return await Json(req, HttpStatusCode.BadRequest, "{\"error\":\"parent_email_missing\"}");

        var now = DateTimeOffset.UtcNow;
        var expires = now.AddMinutes(15);

        // Store request
        var reqTable = TableStore.Get(ApprovalRequestsTable);
        var reqEntity = new TableEntity($"device:{deviceId}", requestId)
        {
            { "DeviceId", deviceId },
            { "RequestId", requestId },
            { "ChildId", childId },
            { "Domain", domain },
            { "RequestedMinutes", payload.RequestedMinutes },
            { "Status", "pending" },
            { "DecisionMinutes", 0 },
            { "CreatedUtc", now.ToString("O") },
            { "ExpiresUtc", expires.ToString("O") },
            { "DecidedUtc", "" }
        };
        await reqTable.UpsertEntityAsync(reqEntity, TableUpdateMode.Replace);

        // Tokens
        var approveToken = NewToken();
        var denyToken = NewToken();

        var approveHash = SecurityHelpers.Sha256Hex(approveToken);
        var denyHash = SecurityHelpers.Sha256Hex(denyToken);

        var tokTable = TableStore.Get(ApprovalTokensTable);

        // Partition by req so lookup is easy later (we’ll scan by RowKey in action handler for MVP)
        var tokPk = $"req:{deviceId}:{requestId}";
        await TryAdd(tokTable, new TableEntity(tokPk, approveHash)
        {
            { "DeviceId", deviceId },
            { "RequestId", requestId },
            { "Action", "approve" },
            { "ExpiresUtc", expires.ToString("O") },
            { "ConsumedUtc", "" }
        });

        await TryAdd(tokTable, new TableEntity(tokPk, denyHash)
        {
            { "DeviceId", deviceId },
            { "RequestId", requestId },
            { "Action", "deny" },
            { "ExpiresUtc", expires.ToString("O") },
            { "ConsumedUtc", "" }
        });

        // Email links (no relay key needed for parent click); include port when non-default (e.g. 7071 for local)
        var port = req.Url.Port;
        var defaultPort = string.Equals(req.Url.Scheme, "https", StringComparison.OrdinalIgnoreCase) ? 443 : 80;
        var baseUrl = port == defaultPort
            ? $"{req.Url.Scheme}://{req.Url.Host}"
            : $"{req.Url.Scheme}://{req.Url.Host}:{port}";
        var approveUrl = $"{baseUrl}/api/v1/approval/action?t={Uri.EscapeDataString(approveToken)}";
        var denyUrl = $"{baseUrl}/api/v1/approval/action?t={Uri.EscapeDataString(denyToken)}";

        var subject = $"DigiKids approval needed: {domain}";
        var html =
            $"<p>Child requested <b>{payload.RequestedMinutes} minutes</b> for <b>{domain}</b>.</p>" +
            $"<p><a href=\"{approveUrl}\">✅ Approve</a></p>" +
            $"<p><a href=\"{denyUrl}\">❌ Deny</a></p>" +
            $"<p>This link expires in 15 minutes.</p>";

        await ResendEmail.SendAsync(parentEmail.Trim(), subject, html);

        return await Json(req, HttpStatusCode.OK, "{\"created\":true}");
    }

    private static string NewToken()
    {
        var b = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static async Task TryAdd(TableClient t, TableEntity e)
    {
        try { await t.AddEntityAsync(e); } catch { /* ignore conflicts */ }
    }

    private static async Task<HttpResponseData> Json(HttpRequestData req, HttpStatusCode code, string json)
    {
        var r = req.CreateResponse(code);
        r.Headers.Add("Content-Type", "application/json");
        await r.WriteStringAsync(json);
        return r;
    }

    private sealed class ApprovalDto
    {
        public string? DeviceId { get; set; }
        public string? RequestId { get; set; }
        public string? ChildId { get; set; }
        public string? Domain { get; set; }
        public int RequestedMinutes { get; set; }
    }
}