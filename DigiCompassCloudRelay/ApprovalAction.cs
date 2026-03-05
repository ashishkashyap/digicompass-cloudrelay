using Azure.Data.Tables;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using System.Net;

namespace DigiCompassCloudRelay;

public sealed class ApprovalActionFn
{
    private const string ApprovalRequestsTable = "ApprovalRequests";
    private const string ApprovalTokensTable = "ApprovalTokens";

    [Function("ApprovalAction")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v1/approval/action")] HttpRequestData req)
    {
        var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        var token = (query.Get("t") ?? "").Trim();

        if (string.IsNullOrWhiteSpace(token))
            return await Html(req, HttpStatusCode.BadRequest, "<h2>Invalid link</h2>");

        var tokenHash = SecurityHelpers.Sha256Hex(token);
        var tokTable = TableStore.Get(ApprovalTokensTable);

        // MVP: find token by RowKey == tokenHash (scan is fine for low volume)
        TableEntity? tok = null;
        await foreach (var e in tokTable.QueryAsync<TableEntity>(x => x.RowKey == tokenHash))
        {
            tok = e;
            break;
        }

        if (tok is null)
            return await Html(req, HttpStatusCode.NotFound, "<h2>Link not found or expired</h2>");

        var now = DateTimeOffset.UtcNow;

        var consumed = tok.GetString("ConsumedUtc") ?? "";
        if (!string.IsNullOrEmpty(consumed))
            return await Html(req, HttpStatusCode.OK, "<h2>This link has already been used.</h2>");

        var expiresStr = tok.GetString("ExpiresUtc") ?? "";
        if (!DateTimeOffset.TryParse(expiresStr, out var expiresUtc) || expiresUtc < now)
            return await Html(req, HttpStatusCode.OK, "<h2>This link has expired.</h2>");

        tok["ConsumedUtc"] = now.ToString("O");
        await tokTable.UpdateEntityAsync(tok, tok.ETag, TableUpdateMode.Replace);

        var deviceId = tok.GetString("DeviceId") ?? "";
        var requestId = tok.GetString("RequestId") ?? "";
        var action = (tok.GetString("Action") ?? "").ToLowerInvariant();

        var reqTable = TableStore.Get(ApprovalRequestsTable);

        TableEntity reqEntity;
        try
        {
            reqEntity = (await reqTable.GetEntityAsync<TableEntity>($"device:{deviceId}", requestId)).Value;
        }
        catch
        {
            return await Html(req, HttpStatusCode.NotFound, "<h2>Request not found.</h2>");
        }

        var status = (reqEntity.GetString("Status") ?? "pending").ToLowerInvariant();
        if (status != "pending")
            return await Html(req, HttpStatusCode.OK, $"<h2>Already decided: {status}</h2>");

        var reqExpiresStr = reqEntity.GetString("ExpiresUtc") ?? "";
        if (DateTimeOffset.TryParse(reqExpiresStr, out var reqExpires) && reqExpires < now)
        {
            reqEntity["Status"] = "expired";
            reqEntity["DecidedUtc"] = now.ToString("O");
            await reqTable.UpdateEntityAsync(reqEntity, reqEntity.ETag, TableUpdateMode.Replace);
            return await Html(req, HttpStatusCode.OK, "<h2>Request expired.</h2>");
        }

        if (action == "approve")
        {
            reqEntity["Status"] = "approved";
            reqEntity["DecisionMinutes"] = reqEntity.GetInt32("RequestedMinutes") ?? 0;
            reqEntity["DecidedUtc"] = now.ToString("O");
            await reqTable.UpdateEntityAsync(reqEntity, reqEntity.ETag, TableUpdateMode.Replace);
            return await Html(req, HttpStatusCode.OK, "<h2>✅ Approved</h2><p>You can close this tab.</p>");
        }
        else
        {
            reqEntity["Status"] = "denied";
            reqEntity["DecisionMinutes"] = 0;
            reqEntity["DecidedUtc"] = now.ToString("O");
            await reqTable.UpdateEntityAsync(reqEntity, reqEntity.ETag, TableUpdateMode.Replace);
            return await Html(req, HttpStatusCode.OK, "<h2>❌ Denied</h2><p>You can close this tab.</p>");
        }
    }

    private static async Task<HttpResponseData> Html(HttpRequestData req, HttpStatusCode code, string body)
    {
        var r = req.CreateResponse(code);
        r.Headers.Add("Content-Type", "text/html; charset=utf-8");
        await r.WriteStringAsync("<!doctype html><html><body style=\"font-family:system-ui;margin:40px\">" + body + "</body></html>");
        return r;
    }
}