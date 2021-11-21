using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Octokit;

namespace dwmuller.HomeNet
{
    class SourceRepository
    {
        private GitHubClient _client;
        long _repoId;

        public static async Task<SourceRepository> GetGitHubRepository(
            string appName, string apiKey, string repoOwner, string repoName)
        {
            var client = new GitHubClient(new ProductHeaderValue(appName));
            var tokenAuth = new Credentials(apiKey);
            client.Credentials = tokenAuth;
            var repo = await client.Repository.Get(repoOwner, repoName);
            return new SourceRepository(client, repo.Id);
        }
        
        private SourceRepository (GitHubClient client, long repoId)
        {
            _client = client;
            _repoId = repoId;
        }

        public struct RepoFile {
            public string Hash;
            public string Path; // Relative to searched root.
        }

        public async IAsyncEnumerable<RepoFile> GetFiles(string repoDocRoot)
        {
            await foreach (var item in GetDirFiles(repoDocRoot, ""))
            {
                yield return item;
            }
        }

        private async IAsyncEnumerable<RepoFile> GetDirFiles(
            string rootPath, string itemPath)
        {
            var repoDirPath = rootPath + itemPath;
            foreach (var item in await _client.Repository.Content.GetAllContents(_repoId, repoDirPath))
            {
                var newItemPath = $"{itemPath}/{item.Name}";
                if (item.Type == ContentType.Dir)
                {
                    await foreach (var subItem in GetDirFiles(rootPath, newItemPath))
                    {
                        yield return subItem;
                    }
                }
                else if (item.Type == ContentType.File)
                {
                    yield return new RepoFile { Hash = item.Sha, Path = newItemPath };
                }
            }
        }

        public async Task<string> GetFileContent(string itemPath) 
        {
            var fullItems = await _client.Repository.Content.GetAllContents(_repoId, itemPath);
            var text = fullItems.First().Content;
            return text;
        }
    }
}