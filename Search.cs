using System;
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
            string requestedSitesString = FunctionTools.GetStringParam(req, "sites", data) ?? string.Empty;
            var requestedSites = requestedSitesString.Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
            string orderBy = FunctionTools.GetStringParam(req, "orderBy", data) ?? "score";

            var cfg = new Configuration(req);
            if (!requestedSites.Any())
            {
                log.LogInformation($"Search: No sites selected");
                return new BadRequestObjectResult("No sites selected.");
            }

            var siteConfigs = ( 
                from s in await cfg.GetSiteConfigs()
                where requestedSites.Contains(s.SiteName)
                select s).ToArray();

            if (siteConfigs.Length != requestedSites.Length)
            {
                var names = string.Join(
                    ", ", 
                    from s in requestedSites where !siteConfigs.Any(c=>c.SiteName == s) select s);
                log.LogWarning($"Search: User {user.Identity.Name} specified unknown site(s) ${names}.");
                return new BadRequestObjectResult($"Unknown site(s) specified: {names}");
            }
            if (siteConfigs.Any(c => !user.CanRead(c)))
            {
                var names = string.Join(
                    ", ", 
                    from c in siteConfigs where !user.CanRead(c) select c.SiteName);
                log.LogWarning($"Search: User {user.Identity.Name} not authorized to read site(s) ${names}.");
                return new UnauthorizedResult();
            }

            var filter = $"search.in({nameof(Doc.Site)},'{string.Join(",",requestedSites)}')";
            var searchClient = IndexTools.CreateSearchClient(cfg);
            var options = new SearchOptions()
            {
                Filter = filter
            };
            options.Select.Add(nameof(Doc.Id));
            options.Select.Add(nameof(Doc.Title));
            options.Select.Add(nameof(Doc.Path));
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
