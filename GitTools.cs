using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using Octokit;

namespace dwmuller.HomeNet
{
    static class GitTools
    {
        public static GitHubClient CreateGitHubClient(string appName, string apiKey, Configuration.Site siteCfg)
        {
            var client = new GitHubClient(new ProductHeaderValue(appName));
            var tokenAuth = new Credentials(apiKey);
            client.Credentials = tokenAuth;
            return client;
        }

        public delegate Task<string> GetFileContent(string itemPath);
        public delegate Task ProcessFile(string id, string itemPath, GetFileContent getContent);
        public static async Task VisitRepoFiles(
            string appName, string apiKey, Configuration.Site siteCfg, ILogger log, ProcessFile processFile)
        {
            var githubClient = GitTools.CreateGitHubClient(appName, apiKey, siteCfg);
            var repo = await githubClient.Repository.Get(siteCfg.GitHubRepoOwner, siteCfg.GitHubRepoName);
            var contentClient = githubClient.Repository.Content;

            await VisitDir(repo.Id, siteCfg.SiteName, siteCfg.GitHubRepoDocRoot, "", contentClient, log, processFile);
        }
        static async Task VisitDir(
            long repoId, string siteName, string rootPath, string itemPath,
            IRepositoryContentsClient contentClient, ILogger log, ProcessFile processFile)
        {
            var repoDirPath = rootPath + itemPath;
            var items = await contentClient.GetAllContents(repoId, repoDirPath);
            GetFileContent getContent = async (string itemPath) =>
            {
                var fullItems = await contentClient.GetAllContents(repoId, rootPath + itemPath);
                var text = fullItems.First().Content;
                Trace.Assert(!fullItems.Skip(1).Any());
                Trace.Assert(!(text is null));
                return text;
            };

            log.LogDebug($"Indexing content of GitHub directory {repoDirPath}");

            foreach (var item in items)
            {
                var newItemPath = $"{itemPath}/{item.Name}";
                if (item.Type == ContentType.Dir)
                    await VisitDir(repoId, siteName, rootPath, newItemPath, contentClient, log, processFile);
                else if (item.Type == ContentType.File)
                {
                    log.LogDebug($"Visiting GitHub file {item.Path}.");
                    await processFile(item.Sha, newItemPath, getContent);
                }
            }
        }

    }
}