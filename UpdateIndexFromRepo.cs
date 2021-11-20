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
using System.Diagnostics;

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

            var searchClient = IndexTools.CreateSearchClient(cfg, withWritePermissions: true);

            // Get existing document paths and their repository file hashes.
            var searchOptions = new SearchOptions();
            searchOptions.Select.Add(nameof(Doc.Id));
            searchOptions.Select.Add(nameof(Doc.Path));
            var hashToPath = 
                await
                    (await searchClient.SearchAsync<Doc>("", searchOptions))
                    .Value
                    .GetResultsAsync()
                    .ToDictionaryAsync(item => item.Document.Id, item => item.Document.Path);
            log.LogDebug($"Retrieved {hashToPath.Count} doc hashes.");

            // If we're forcing a rebuild of index content, then delete all
            // documents on this site first.
            if (force && hashToPath.Any())
            {
                searchClient.DeleteDocuments(nameof(Doc.Id), hashToPath.Keys);
                log.LogInformation($"Forced site update: {hashToPath.Count} documents deleted.");
                hashToPath.Clear();
            }

            var pathToHash = hashToPath.ToDictionary(pair => pair.Value, pair => pair.Key);
            var batch = new IndexDocumentsBatch<Doc>();
            await GitTools.VisitRepoFiles(
                cfg.GitHubAppName, cfg.GitHubApiKey, cfg.GitHubRepoOwner,
                cfg.GitHubRepoName, cfg.GitHubRepoDocRoot, log,
                (id, itemPath, getContent) => ProcessFile(cfg, batch, hashToPath, pathToHash, log, id, itemPath, getContent));

            if (batch.Actions.Any())
            {
                var response = await searchClient.IndexDocumentsAsync(batch);
            }
            return new OkObjectResult($"Indexed {batch.Actions.Count} documents.");
        }

        static async Task ProcessFile(
            Configuration cfg, IndexDocumentsBatch<Doc> batch, IDictionary<string, string> hashToPath, IDictionary<string, string> pathToHash, ILogger log,
            string itemHash, string itemPath, GitTools.GetFileContent getContent)
        {
            // To form the document URL path, remove the file extension, and add
            // the site's prefix and suffix.
            var docPath = cfg.PathPrefix + Regex.Replace(itemPath, @"\.[^/.]*$", "") + cfg.PathSuffix;
            var status = GetRepoItemStatus(hashToPath, pathToHash, itemHash, docPath);
            if (status == ItemStatus.Indexed)
            {
                log.LogDebug($"Doc {itemHash} at {docPath} is already up to date.");
                return; // This version of this file is already indexed.
            }

            if (status == ItemStatus.ContentChanged)
            {
                // This doc was indexed, but its content has changed, thus its
                // old entry is obsolete.
                batch.Actions.Add(IndexDocumentsAction.Delete(new Doc { Id = itemHash }));
                log.LogInformation($"Removing doc {itemHash} as {docPath} due to stale content.");
            }

            if (itemPath.EndsWith(".md"))
            {
                // Processing a Markdown file.
                if (status == ItemStatus.PathChanged)
                {
                    UpdatePath(batch, pathToHash, hashToPath, itemHash, docPath);
                    log.LogDebug($"Updating path of doc {itemHash} at {docPath}.");
                    return;
                }
                var text = await getContent(itemPath);
                string title = ExtractFrontMatter(ref text);
                // Remove all non-word characters.
                text = Regex.Replace(text, @"\W+", " ");

                var doc = new Doc()
                {
                    Id = itemHash,
                    Path = docPath,
                    Title = title,
                    Body = text
                };
                Trace.Assert((new []{ItemStatus.ContentChanged, ItemStatus.New}).Contains(status));
                batch.Actions.Add(IndexDocumentsAction.Upload(doc));
                log.LogDebug($"Uploading new doc version {itemHash} at {docPath}.");
                hashToPath[itemHash] = docPath;
                pathToHash[docPath] = itemHash;
            }
            else if (status == ItemStatus.PathChanged)
            {
                // This item was indexed, but the item's path changed such
                // that we no longer want to index it.
                log.LogInformation($"Removing indexed doc {itemHash}, now named {itemPath}.");
                batch.Actions.Add(IndexDocumentsAction.Delete(new Doc { Id = itemHash }));
                hashToPath.Remove(itemHash);
                pathToHash.Remove(docPath);

            }
            else
            {
                Trace.Assert(status == ItemStatus.New);
                log.LogDebug($"Declined to index doc {itemHash} at {itemPath}.");
            }
        }

        enum ItemStatus
        {
            Indexed,
            New,
            ContentChanged,
            PathChanged
        }

        private static ItemStatus GetRepoItemStatus(IDictionary<string, string> hashToPath, IDictionary<string, string> pathToHash, string itemHash, string docPath)
        {
            string indexedDocPath;
            if (hashToPath.TryGetValue(itemHash, out indexedDocPath))
            {
                if (indexedDocPath == docPath)
                    return ItemStatus.Indexed;
                return ItemStatus.PathChanged;
            }
            string indexedItemHash;
            if (pathToHash.TryGetValue(docPath, out indexedItemHash))
            {
                if (indexedItemHash == itemHash)
                    return ItemStatus.Indexed;
                return ItemStatus.ContentChanged;
            }
            return ItemStatus.New;
        }

        private static void UpdatePath(
            IndexDocumentsBatch<Doc> batch, 
            IDictionary<string, string> hashToPath, IDictionary<string, string> pathToHash, 
            string itemHash, string docPath)
        {
            // This version of this file is indexed, but its path changed.
            var doc = new Doc
            {
                Id = itemHash,
                Path = docPath
            };
            batch.Actions.Add(IndexDocumentsAction.Merge(doc));
            hashToPath[itemHash] = docPath;
            pathToHash.Remove(docPath);
            pathToHash[docPath] = itemHash;
        }

        private static string ExtractFrontMatter(ref string text)
        {
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

            return title;
        }
    }
}
