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
using System.Globalization;

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
            var repoClient = await SourceRepository.GetGitHubRepository(
                cfg.GitHubAppName, cfg.GitHubApiKey, cfg.GitHubRepoOwner, cfg.GitHubRepoName);

            // Get existing document paths and their repository file hashes.
            var searchOptions = new SearchOptions();
            searchOptions.Select.Add(nameof(Doc.DocPath));
            searchOptions.Select.Add(nameof(Doc.RepoHash));
            searchOptions.Select.Add(nameof(Doc.RepoPath));
            var docPathToDoc = 
                await
                    (await searchClient.SearchAsync<Doc>("", searchOptions))
                    .Value
                    .GetResultsAsync()
                    .ToDictionaryAsync(item => item.Document.DocPath, item => item.Document);
            log.LogDebug($"Found {docPathToDoc.Count} docs.");

            // Get existing repository item paths and their file hashes.
            // We're only interested in Markdown files right now.
            var docPathToRepoFile = await repoClient.GetFiles(cfg.GitHubRepoDocRoot)
                .Where( item => item.Path.EndsWith(".md", ignoreCase: true, CultureInfo.InvariantCulture))
                .ToDictionaryAsync(pair => RepoPathToDocPath(cfg, pair.Path), pair => pair);
            log.LogDebug($"Found {docPathToRepoFile.Count} repo items of interest.");
                

            // If we're forcing a rebuild of index content, then delete all
            // documents on this site first.
            if (force && docPathToDoc.Any())
            {
                searchClient.DeleteDocuments(docPathToDoc.Values);
                log.LogInformation($"Forced site update: {docPathToDoc.Count} documents deleted.");
                docPathToDoc.Clear();
            }

            var batch = new IndexDocumentsBatch<Doc>();
            foreach (var entry in docPathToDoc)
            {
                SourceRepository.RepoFile item;
                if (!docPathToRepoFile.TryGetValue(entry.Key, out item))
                {
                    // Document source no longer exists in repo, or was moved to
                    // a different path.
                    batch.Actions.Add(IndexDocumentsAction.Delete(entry.Value));
                    log.LogInformation($"Removing index entry for {entry.Value}.");
                }
                else if (entry.Key != item.Hash)
                {
                    // The document was indexed, but its content has changed.
                    var text = await repoClient.GetFileContent(cfg.GitHubRepoDocRoot + item.Path);
                    UploadMarkdownEntry(batch, item.Hash, item.Path, text, entry.Key);
                    log.LogInformation($"Updating index entry for {entry.Value}.");
                }
            }
            foreach (var entry in docPathToRepoFile)
            {
                if (!docPathToDoc.ContainsKey(entry.Key))
                {
                    // New document.
                    var text = await repoClient.GetFileContent(cfg.GitHubRepoDocRoot + entry.Value.Path);
                    UploadMarkdownEntry(batch, entry.Value.Hash, entry.Value.Path, text, entry.Key);
                    log.LogInformation($"Adding index entry for {entry.Key}.");
                }
            }

            if (batch.Actions.Any())
            {
                var response = await searchClient.IndexDocumentsAsync(batch);
            }
            return new OkObjectResult($"Indexed {batch.Actions.Count} documents.");
        }

        private static string RepoPathToDocPath(Configuration cfg, string itemPath)
        {
            return cfg.DocPathPrefix + Regex.Replace(itemPath, @"\.[^/.]*$", "") + cfg.DocPathSuffix;
        }

        private static void UploadMarkdownEntry(
            IndexDocumentsBatch<Doc> batch,
            string itemHash, string itemPath, string text, string docPath)
        {
            string title = ExtractFrontMatter(ref text);
            // Remove all non-word characters.
            text = Regex.Replace(text, @"\W+", " ");

            var doc = new Doc()
            {
                DocPath = docPath,
                RepoHash = itemHash,
                RepoPath = itemPath,
                Title = title,
                Body = text
            };
            batch.Actions.Add(IndexDocumentsAction.Upload(doc));
        }

        private static void UpdatePath(
            IndexDocumentsBatch<Doc> batch, 
            IDictionary<string, string> hashToPath, IDictionary<string, string> pathToHash, 
            string itemHash, string itemPath, string docPath)
        {
            // This version of this file is indexed, but its path changed.
            var doc = new Doc
            {
                RepoHash = itemHash,
                RepoPath = itemPath,
                DocPath = docPath
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
