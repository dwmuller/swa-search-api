using System.IO;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

using Azure.Search.Documents;
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

            var cfg = new Configuration(req);

            var searchClient = IndexTools.CreateSearchClient(cfg);
            var options = new SearchOptions();
            options.Select.Add(nameof(Doc.RepoHash));
            options.Select.Add(nameof(Doc.Title));
            options.Select.Add(nameof(Doc.DocPath));
            switch (orderBy)
            {
                case "score":
                    options.OrderBy.Add("search.score() desc");
                    break;
                case "title":
                    options.OrderBy.Add(nameof(Doc.Title) + " asc");
                    break;
                default:
                    log.LogWarning($"Search: User {user.Identity.Name} specified unknown orderBy value ${orderBy}.");
                    return new BadRequestObjectResult("Invalid sort order.");
            }
            options.QueryType = Azure.Search.Documents.Models.SearchQueryType.Full;
            options.SearchMode = Azure.Search.Documents.Models.SearchMode.All;
            log.LogDebug("Search: Starting search.");
            try
            {
                var response = (await searchClient.SearchAsync<Doc>(query, options));
                var results = await response.Value.GetResultsAsync().ToListAsync();
                return new OkObjectResult(results);
            }
            catch (RequestFailedException e)
            {
                return new BadRequestObjectResult(e.Message);
            }
        }
    }
}
