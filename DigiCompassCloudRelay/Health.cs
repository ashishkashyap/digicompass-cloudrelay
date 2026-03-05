using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Threading.Tasks;

namespace DigiCompassCloudRelay;

public class Health
{
    private readonly ILogger _logger;

    public Health(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<Health>();
    }

    [Function("Health")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get")] HttpRequestData req)
    {
        _logger.LogInformation("Health check called.");

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteStringAsync("CloudRelay is running.");
        return response;
    }
}