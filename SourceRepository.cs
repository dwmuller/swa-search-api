using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Octokit;

namespace dwmuller.HomeNet
{

    class GitHubRepository : ISourceRepository
    {
        private GitHubClient _client;
        long _repoId;

        public static async Task<ISourceRepository> Create(Configuration cfg)
        {
            var client = new GitHubClient(new ProductHeaderValue(cfg.GitHubAppName));
            var tokenAuth = new Credentials(cfg.GitHubApiKey);
            client.Credentials = tokenAuth;
            var repo = await client.Repository.Get(cfg.GitHubRepoOwner, cfg.GitHubRepoName);
            return new GitHubRepository(client, repo.Id);
        }

        internal GitHubRepository(GitHubClient client, long repoId)
        {
            _client = client;
            _repoId = repoId;
        }

        public async IAsyncEnumerable<ISourceRepository.RepoFile> GetFiles(string repoDocRoot)
        {
            await foreach (var item in GetDirFiles(repoDocRoot, ""))
            {
                yield return item;
            }
        }

        private async IAsyncEnumerable<ISourceRepository.RepoFile> GetDirFiles(string rootPath, string itemPath)
        {
            var repoDirPath = rootPath + itemPath;
            foreach (var item in await _client.Repository.Content.GetAllContents(_repoId, repoDirPath))
            {
                var newItemPath = $"{itemPath}{item.Name}";
                if (item.Type == ContentType.Dir)
                {
                    await foreach (var subItem in GetDirFiles(rootPath, newItemPath + "/"))
                    {
                        yield return subItem;
                    }
                }
                else if (item.Type == ContentType.File)
                {
                    yield return new ISourceRepository.RepoFile { Hash = item.Sha, Path = newItemPath };
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