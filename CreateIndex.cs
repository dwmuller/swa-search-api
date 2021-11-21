using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

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
            var index = new SearchIndex(cfg, log);
            await index.Create();
            return new OkResult();
        }

    }
}
