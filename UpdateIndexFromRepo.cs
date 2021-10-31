using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Azure.Search.Documents;
using Azure.Search.Documents.Models;
using Octokit;
using System.Linq;

namespace dwmuller.HomeNet
{
    public static class UpdateIndexFromRepo
    {
        [FunctionName("UpdateIndexFromRepo")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequest req,
            ILogger log)
        {
            log.LogInformation($"{nameof(UpdateIndexFromRepo)} processing HTTP request.");

            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            dynamic data = JsonConvert.DeserializeObject(requestBody);

            bool force = FunctionTools.GetBoolParam(req, "force", data) ?? false;

            var cfg = new Configuration();

            var searchClient = IndexTools.CreateSearchClient(cfg, withWritePermissions: true);
            var searchOptions = new SearchOptions()
            {
                Filter = $"{nameof(Doc.Site)} eq '{cfg.SiteName}'"
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
                log.LogInformation($"Forced site update: {hashDict.Count} documents in site {cfg.SiteName} deleted.");
                hashDict = new Dictionary<string, string>();
            }

            var githubClient = GitTools.CreateGitHubClient(cfg);
            var repo = await githubClient.Repository.Get(cfg.GitHubRepoOwner, cfg.GitHubRepoName);
            var content = githubClient.Repository.Content;
            var batch = new IndexDocumentsBatch<Doc>();

            await IndexDir(repo.Id, cfg.SiteName, cfg.GitHubIndexableDocRoot, "", content, batch, hashDict, log);

            if (batch.Actions.Any())
            {
                var response = await searchClient.IndexDocumentsAsync(batch);
            }
            return new OkObjectResult($"Indexed {batch.Actions.Count} documents.");
        }

        static async Task IndexDir(long repoId, string siteName, string rootPath, string itemPath,
                                   IRepositoryContentsClient client, IndexDocumentsBatch<Doc> batch,
                                   IDictionary<string, string> hashDict,
                                   ILogger log)
        {
            var repoDirPath = rootPath + itemPath;
            var items = await client.GetAllContents(repoId, repoDirPath);
            log.LogDebug($"Indexing content of GitHub directory {repoDirPath}");
            foreach (var item in items)
            {
                var newItemPath = $"{itemPath}/{item.Name}";
                if (item.Type == ContentType.Dir)
                    await IndexDir(repoId, siteName, rootPath, newItemPath, client, batch, hashDict, log);
                else if (item.Type == ContentType.File)
                    await IndexFile(repoId, siteName, rootPath, item, newItemPath, client, batch, hashDict, log);
            }
        }

        static async Task IndexFile(long repoId, string siteName, string rootPath, RepositoryContent item, string itemPath,
                              IRepositoryContentsClient client, IndexDocumentsBatch<Doc> batch,
                              IDictionary<string, string> hashDict, ILogger log)
        {
            log.LogDebug($"Indexing GitHub file {item.Path}.");
            var docPath = Regex.Replace(itemPath, @"\.[^/.]*$", "");

            string indexedDocPath;
            if (hashDict.TryGetValue(item.Sha, out indexedDocPath))
            {
                // This particular file version has been indexed already. If the
                // path changed, update it, otherwise we're good.
                if (docPath == indexedDocPath)
                {
                    log.LogDebug($"Document {item.Sha} at {docPath} is already up to date.");
                    return; // This version of this file is already indexed.
                }
                else
                {
                    var doc = new Doc
                    {
                        Id = item.Sha,
                        Path = docPath
                    };
                    batch.Actions.Add(IndexDocumentsAction.Merge(doc));
                    log.LogDebug($"Updating path of {item.Sha} from {indexedDocPath} to {docPath}.");
                    hashDict[item.Sha] = docPath; // Not strictly necessary.
                    return;
                }

            }
            if (itemPath.EndsWith(".md"))
            {
                var fullItems = await client.GetAllContents(repoId, rootPath + itemPath);
                var text = fullItems.First().Content;
                Trace.Assert(!fullItems.Skip(1).Any());
                Trace.Assert(!(text is null));
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
                }
                // Remove all non-word characters.
                text = Regex.Replace(text, @"\W+", " ");

                var doc = new Doc()
                {
                    Id = item.Sha,
                    Site = siteName,
                    Path = docPath,
                    Title = title,
                    Body = text
                };
                batch.Actions.Add(IndexDocumentsAction.Upload(doc));
                log.LogDebug($"Uploading new doc version {item.Sha} at {docPath}.");
                hashDict[item.Sha] = docPath; // Not strictly necessary.
            }
        }
    }
}
