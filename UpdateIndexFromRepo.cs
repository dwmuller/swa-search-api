using System.IO;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace dwmuller.HomeNet
{
    public static class UpdateIndexFromRepo
    {
        [FunctionName("UpdateIndexFromRepo")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = null)] HttpRequest req,
            ILogger log)
        {
            log.LogInformation($"{nameof(UpdateIndexFromRepo)} processing HTTP request.");

            var user = StaticWebAppsAuth.Parse(req);

            if (!user.IsInRole("admin"))
            {
                log.LogWarning($"Non-administrator attempted to update index.");
                return new UnauthorizedResult();
            }

            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            dynamic data = JsonConvert.DeserializeObject(requestBody);

            bool force = FunctionTools.GetBoolParam(req, "force", data) ?? false;

            var cfg = new Configuration(req);

            var repo = await GitHubRepository.Create(cfg);
            var index = new SearchIndex(cfg, log);
            int count = await index.UpdateIndex(force, repo);
            return new OkObjectResult($"Indexed {count} documents.");
        }

    }
}
