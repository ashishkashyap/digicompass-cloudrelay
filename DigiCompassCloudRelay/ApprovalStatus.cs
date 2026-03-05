using Azure.Data.Tables;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using System.Net;
using System.Text.Json;

namespace DigiCompassCloudRelay;

public sealed class ApprovalStatusFn
{
    private const string ApprovalRequestsTable = "ApprovalRequests";

    [Function("ApprovalStatus")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v1/approval/status")] HttpRequestData req)
    {
        var relayKey = req.Headers.TryGetValues("X-Relay-Key", out var vals) ? vals.FirstOrDefault() : null;
        if (!SecurityHelpers.RelayKeyValid(relayKey))
            return req.CreateResponse(HttpStatusCode.Unauthorized);

        var q = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        var deviceId = (q.Get("deviceId") ?? "").Trim();
        var requestId = (q.Get("requestId") ?? "").Trim();

        if (string.IsNullOrWhiteSpace(deviceId) || string.IsNullOrWhiteSpace(requestId))
            return await Json(req, HttpStatusCode.BadRequest, "{\"error\":\"deviceId and requestId required\"}");

        var reqTable = TableStore.Get(ApprovalRequestsTable);

        TableEntity e;
        try
        {
            e = (await reqTable.GetEntityAsync<TableEntity>($"device:{deviceId}", requestId)).Value;
        }
        catch
        {
            return await Json(req, HttpStatusCode.NotFound, "{\"error\":\"request_not_found\"}");
        }

        // lazy-expire
        var now = DateTimeOffset.UtcNow;
        var status = (e.GetString("Status") ?? "pending").ToLowerInvariant();
        var expiresStr = e.GetString("ExpiresUtc") ?? "";
        if (status == "pending" && DateTimeOffset.TryParse(expiresStr, out var exUtc) && exUtc < now)
        {
            e["Status"] = "expired";
            e["DecidedUtc"] = now.ToString("O");
            await reqTable.UpdateEntityAsync(e, e.ETag, TableUpdateMode.Replace);
            status = "expired";
        }

        var result = new
        {
            status,
            decisionMinutes = e.GetInt32("DecisionMinutes") ?? 0,
            decidedUtc = e.GetString("DecidedUtc") ?? ""
        };

        var json = JsonSerializer.Serialize(result);
        return await Json(req, HttpStatusCode.OK, json);
    }

    private static async Task<HttpResponseData> Json(HttpRequestData req, HttpStatusCode code, string json)
    {
        var r = req.CreateResponse(code);
        r.Headers.Add("Content-Type", "application/json");
        await r.WriteStringAsync(json);
        return r;
    }
}