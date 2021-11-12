using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;

using Azure.Search.Documents;
using Azure.Search.Documents.Models;
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

            string requestedSitesString = FunctionTools.GetStringParam(req, "sites", data) ?? string.Empty;
            var requestedSites = requestedSitesString.Split(',', System.StringSplitOptions.RemoveEmptyEntries);
            bool force = FunctionTools.GetBoolParam(req, "force", data) ?? false;

            var cfg = new Configuration(req);
            if (!requestedSites.Any())
            {
                log.LogInformation($"UpdateIndexFromRepo: No sites specified");
                return new BadRequestObjectResult("No sites specified.");
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
                log.LogWarning($"UpdateIndexFromRepo: User {user.Identity.Name} specified unknown site(s) ${names}.");
                return new BadRequestObjectResult($"Unknown site(s) specified: {names}");
            }
            if (siteConfigs.Any(c => !user.CanManage(c)))
            {
                var names = string.Join(
                    ", ", 
                    from c in siteConfigs where !user.CanRead(c) select c.SiteName);
                log.LogWarning($"UpdateIndexFromRepo: User {user.Identity.Name} not authorized to admin site(s) ${names}.");
                return new UnauthorizedResult();
            }

            var searchClient = IndexTools.CreateSearchClient(cfg, withWritePermissions: true);
            var batch = new IndexDocumentsBatch<Doc>();

            foreach (var siteCfg in siteConfigs)
            {
                var siteName = siteCfg.SiteName;
                var searchOptions = new SearchOptions()
                {
                    Filter = $"{nameof(Doc.Site)} eq '{siteName}'"
                };
                searchOptions.Select.Add(nameof(Doc.Id));
                searchOptions.Select.Add(nameof(Doc.Path));
                var hashGetResponse = (await searchClient.SearchAsync<Doc>("", searchOptions)).Value;
                var hashGetResults = hashGetResponse.GetResultsAsync();
                var hashDict = await hashGetResults.ToDictionaryAsync(item => item.Document.Id, item => item.Document.Path);
                log.LogDebug($"Retrieved {hashDict.Count} doc hashes.");

                // If we're forcing a rebuild of index content, then delete all
                // documents on this site first.
                if (force && hashDict.Any())
                {
                    searchClient.DeleteDocuments(nameof(Doc.Id), hashDict.Keys);
                    log.LogInformation($"Forced site update: {hashDict.Count} documents in site {siteName} deleted.");
                    hashDict = new Dictionary<string, string>();
                }
                await GitTools.VisitRepoFiles(cfg.AppName, cfg.GitHubApiKey, siteCfg, log,
                    (id, itemPath, getContent) => ProcessFile(siteCfg, batch, hashDict, log, id, itemPath, getContent));

            }
            if (batch.Actions.Any())
            {
                var response = await searchClient.IndexDocumentsAsync(batch);
            }
            return new OkObjectResult($"Indexed {batch.Actions.Count} documents.");
        }

        static async Task ProcessFile(
            Configuration.Site siteCfg, IndexDocumentsBatch<Doc> batch, IDictionary<string, string> hashDict, ILogger log,
            string id, string itemPath, GitTools.GetFileContent getContent)
        {
            var docPath = siteCfg.DocRoot + Regex.Replace(itemPath, @"\.[^/.]*$", "") + "/";
            string indexedDocPath;
            if (hashDict.TryGetValue(id, out indexedDocPath))
            {
                // This particular file version has been indexed already. If the
                // path changed, update it, otherwise we're good.
                if (docPath == indexedDocPath)
                {
                    log.LogDebug($"Document {id} at {docPath} is already up to date.");
                    return; // This version of this file is already indexed.
                }
                else
                {
                    var doc = new Doc
                    {
                        Id = id,
                        Path = docPath,
                        Site = siteCfg.SiteName
                    };
                    batch.Actions.Add(IndexDocumentsAction.Merge(doc));
                    log.LogInformation($"Updating path of {id} from {indexedDocPath} to {docPath}.");
                    hashDict[id] = docPath; // Not strictly necessary.
                    return;
                }

            }
            if (itemPath.EndsWith(".md"))
            {
                var text = await getContent(itemPath);
                var m = Regex.Match(text, "^(---.*?^---)(.*)$", RegexOptions.Multiline | RegexOptions.Singleline);
                var title = "";
                if (m.Success)
                {
                    var fm = m.Groups[1].Value;
                    // We found front matter. Remove it from the body text.
                    text = text.Substring(fm.Length);
                    // Try to get a title from the frontmatter.
                    m = Regex.Match(fm, @"^title:\s+(.*)\s*$", RegexOptions.Multiline);
                    if (m.Success)
                    {
                        title = m.Groups[1].Value;
                    }
                    // Try to get a parent from the frontmatter.
                    m = Regex.Match(fm, @"^wiki_parent:\s+(.*)\s*$", RegexOptions.Multiline);
                    if (m.Success)
                    {
                        title = $"{m.Groups[1].Value}/{title}";
                    }
                }
                // Remove all non-word characters.
                text = Regex.Replace(text, @"\W+", " ");

                var doc = new Doc()
                {
                    Id = id,
                    Site = siteCfg.SiteName,
                    Path = docPath,
                    Title = title,
                    Body = text
                };
                batch.Actions.Add(IndexDocumentsAction.Upload(doc));
                log.LogDebug($"Uploading new doc version {id} at {docPath}.");
                hashDict[id] = docPath; // Not strictly necessary.
            }
        }
    }
}
