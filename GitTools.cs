using Octokit;

namespace dwmuller.HomeNet
{
    static class GitTools
    {
        public static GitHubClient CreateGitHubClient(Configuration cfg)
        {
            var client = new GitHubClient(new ProductHeaderValue(cfg.AppName));
            var tokenAuth = new Credentials(cfg.GitHubApiKey);
            client.Credentials = tokenAuth;
            return client;
        }
    }
}