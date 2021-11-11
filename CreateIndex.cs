using System.Threading.Tasks;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Indexes.Models;

namespace dwmuller.HomeNet
{
    public static class CreateIndex
    {
        [FunctionName("CreateIndex")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = null)] HttpRequest req,
            ILogger log)
        {
            log.LogInformation($"{nameof(CreateIndex)} processing HTTP request.");

            var user = StaticWebAppsAuth.Parse(req);
            if (!user.IsInRole("admin"))
            {
                log.LogWarning($"Non-administrator attempted to (re)create index.");
                return new UnauthorizedResult();
            }

            var cfg = new Configuration(req);
            var indexClient = IndexTools.CreateIndexClient(cfg);
            var indexFields = new FieldBuilder().Build(typeof(Doc));
            var index = new SearchIndex(cfg.SearchIndexName, indexFields);
            var indexes = indexClient.GetIndexNamesAsync();
            if (await indexes.AnyAsync(name => cfg.SearchIndexName == name))
            {
                await indexClient.DeleteIndexAsync(cfg.SearchIndexName);
                log.LogInformation($"Index {cfg.SearchIndexName} was deleted.");
            }
            indexClient.CreateIndex(index);
            log.LogInformation($"Index {cfg.SearchIndexName} was created.");
            return new OkResult();
        }

    }
}
