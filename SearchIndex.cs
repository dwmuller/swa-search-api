using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Models;
using Microsoft.Extensions.Logging;

namespace dwmuller.HomeNet
{

    public enum SearchResultsOrder
    {
        Score,
        Title
    }

    public class SearchIndex
    {
        Configuration _cfg;
        ILogger _log;

        public SearchIndex(Configuration cfg, ILogger log)
        {
            _cfg = cfg;
            _log = log;
        }

        public async Task Create()
        {
            var creds = new Azure.AzureKeyCredential(_cfg.SearchServiceAdminApiKey);
            var indexClient = new SearchIndexClient(_cfg.SearchServiceUri, creds);
            var indexFields = new FieldBuilder().Build(typeof(Doc));
            var index = new Azure.Search.Documents.Indexes.Models.SearchIndex(_cfg.SearchIndexName, indexFields);
            var indexes = indexClient.GetIndexNamesAsync();
            if (await indexes.AnyAsync(name => _cfg.SearchIndexName == name))
            {
                await indexClient.DeleteIndexAsync(_cfg.SearchIndexName);
                _log.LogInformation($"Index {_cfg.SearchIndexName} was deleted.");
            }
            indexClient.CreateIndex(index);
            _log.LogInformation($"Index {_cfg.SearchIndexName} was created.");
        }

        public async Task<List<SearchResult<Doc>>> Search(string query, SearchResultsOrder? searchOrder)
        {
            var creds = new Azure.AzureKeyCredential(_cfg.SearchServiceQueryApiKey);
            var searchClient = new SearchClient(_cfg.SearchServiceUri, _cfg.SearchIndexName, creds);
            var options = new SearchOptions();
            options.Select.Add(nameof(Doc.RepoHash));
            options.Select.Add(nameof(Doc.Title));
            options.Select.Add(nameof(Doc.DocPath));
            switch (searchOrder)
            {
                case SearchResultsOrder.Score:
                    options.OrderBy.Add("search.score() desc");
                    break;
                case SearchResultsOrder.Title:
                    options.OrderBy.Add(nameof(Doc.Title) + " asc");
                    break;
            }
            options.QueryType = Azure.Search.Documents.Models.SearchQueryType.Full;
            options.SearchMode = Azure.Search.Documents.Models.SearchMode.All;
            var response = (await searchClient.SearchAsync<Doc>(query, options));
            var result = await response.Value.GetResultsAsync().ToListAsync();
            return result;
        }

        public async Task<int> UpdateIndex(bool force, ISourceRepository repoClient)
        {
            var creds = new Azure.AzureKeyCredential(_cfg.SearchServiceAdminApiKey);
            var searchClient = new SearchClient(_cfg.SearchServiceUri, _cfg.SearchIndexName, creds);

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
            _log.LogDebug($"Found {docPathToDoc.Count} docs.");

            // Get existing repository item paths and their file hashes.
            // We're only interested in Markdown files right now.
            var docPathToRepoFile = await repoClient.GetFiles(_cfg.GitHubRepoDocRoot)
                .Where(item => item.Path.EndsWith(".md", ignoreCase: true, CultureInfo.InvariantCulture))
                .ToDictionaryAsync(pair => _cfg.RepoPathToDocPath(pair.Path), pair => pair);
            _log.LogDebug($"Found {docPathToRepoFile.Count} repo items of interest.");


            // If we're forcing a rebuild of index content, then delete all
            // entries first.
            if (force && docPathToDoc.Any())
            {
                searchClient.DeleteDocuments(docPathToDoc.Values);
                _log.LogInformation($"Forced site update: {docPathToDoc.Count} documents deleted.");
                docPathToDoc.Clear();
            }

            var batch = new IndexDocumentsBatch<Doc>();
            foreach (var entry in docPathToDoc)
            {
                ISourceRepository.RepoFile item;
                if (!docPathToRepoFile.TryGetValue(entry.Key, out item))
                {
                    // Document source no longer exists in repo, or was moved to
                    // a different path.
                    batch.Actions.Add(IndexDocumentsAction.Delete(entry.Value));
                    _log.LogInformation($"Removing index entry for {entry.Key}.");
                }
                else if (entry.Value.RepoHash != item.Hash)
                {
                    // The document was indexed, but its content has changed.
                    var text = await repoClient.GetFileContent(_cfg.GitHubRepoDocRoot + item.Path);
                    UploadMarkdownEntry(batch, item.Hash, item.Path, text, entry.Key);
                    _log.LogInformation($"Updating index entry for {entry.Key}.");
                }
            }
            foreach (var entry in docPathToRepoFile)
            {
                if (!docPathToDoc.ContainsKey(entry.Key))
                {
                    // New document.
                    var text = await repoClient.GetFileContent(_cfg.GitHubRepoDocRoot + entry.Value.Path);
                    UploadMarkdownEntry(batch, entry.Value.Hash, entry.Value.Path, text, entry.Key);
                    _log.LogInformation($"Adding index entry for {entry.Key}.");
                }
            }

            if (batch.Actions.Any())
            {
                var response = await searchClient.IndexDocumentsAsync(batch);
            }

            int count = batch.Actions.Count;
            return count;
        }

        private static void UploadMarkdownEntry(
            IndexDocumentsBatch<Doc> batch,
            string itemHash, string itemPath, string text, string docPath)
        {
            string title = ExtractFrontMatter(ref text);
            // Remove all non-word characters.
            //text = Regex.Replace(text, @"\W+", " ");

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