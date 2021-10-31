using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Azure.Search.Documents;
using Azure.Search.Documents.Models;

namespace dwmuller.HomeNet
{
    public static class Search
    {
        [FunctionName("Search")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequest req,
            ILogger log)
        {
            log.LogInformation($"{nameof(Search)} processing HTTP request.");

            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            dynamic data = JsonConvert.DeserializeObject(requestBody);
            string query = FunctionTools.GetStringParam(req, "query", data) ?? "*";

            var cfg = new Configuration();
            var searchClient = IndexTools.CreateSearchClient(cfg);
            var options = new SearchOptions()
            {
                Filter = $"{nameof(Doc.Site)} eq '{cfg.SiteName}'"
            };
            options.Select.Add(nameof(Doc.Id));
            options.Select.Add(nameof(Doc.Title));
            options.Select.Add(nameof(Doc.Path));
            var response = (await searchClient.SearchAsync<Doc>(query, options));
            var results = await response.Value.GetResultsAsync().ToListAsync();

            // TODO: Return results in a sane way.
            return new OkObjectResult(results);
        }
    }
}
