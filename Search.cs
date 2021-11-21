using System.IO;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Azure;

namespace dwmuller.HomeNet
{
    public static class Search
    {
        [FunctionName("Search")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequest req,
            ILogger log)
        {
            log.LogInformation($"{nameof(Search)}: Processing HTTP request.");

            var user = StaticWebAppsAuth.Parse(req);

            if (user.Identity is null)
            {
                log.LogWarning($"Unauthenticated user attempted to search.");
                return new UnauthorizedResult();
            }

            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            dynamic data = JsonConvert.DeserializeObject(requestBody);
            string query = FunctionTools.GetStringParam(req, "query", data) ?? "*";
            string orderBy = FunctionTools.GetStringParam(req, "orderBy", data) ?? "score";
            SearchResultsOrder? searchOrder = orderBy switch 
            {
                "score" => SearchResultsOrder.Score,
                "title" => SearchResultsOrder.Title,
                _ => null
            };
            if (!searchOrder.HasValue)
            {
                log.LogWarning($"Search: Unknown orderBy value ${orderBy}.");
                return new BadRequestObjectResult("Invalid sort order.");
            }

            var cfg = new Configuration(req);
            var index = new SearchIndex(cfg, log);
            try
            {
                var results = await index.Search(query, searchOrder);
                return new OkObjectResult(results);
            }
            catch (RequestFailedException e)
            {
                if (e.Status == StatusCodes.Status400BadRequest)
                    return new BadRequestObjectResult(e.Message);
                throw;
            }
        }

    }
}
