using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

using Azure.Search.Documents;
using Newtonsoft.Json;

namespace dwmuller.HomeNet
{
    public static class GetSiteNames
    {
        [FunctionName("GetSiteNames")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequest req,
            ILogger log)
        {
            log.LogInformation($"{nameof(Search)} processing HTTP request.");

            var principal = StaticWebAppsAuth.Parse(req);
            if (!principal.IsInRole("admin"))
            {
                log.LogWarning($"Non-administrator attempted to retrieve site name list.");
                return new UnauthorizedResult();
            }

            var cfg = new Configuration(req);
            var siteNames = (from s in await cfg.GetSiteConfigs() select s.SiteName).ToArray();
            return new OkObjectResult(siteNames);
        }
    }
}