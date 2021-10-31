using System;
using Microsoft.Extensions.Configuration;

namespace dwmuller.HomeNet
{
    class Configuration
    {
        public Configuration()
        {
            var root = new ConfigurationBuilder().AddEnvironmentVariables().Build();
            root.Bind(this);
        }

        public string AppName { get; set; }

        public string SiteName { get; set; }
        public Uri SearchServiceUri { get; set; }
        public string SearchIndexName { get; set; }

        public string SearchServiceQueryApiKey { get; set; }

        public string SearchServiceAdminApiKey { get; set; }

        public string GitHubApiKey { get; set; }

        public string GitHubRepoOwner { get; set; }

        public string GitHubRepoName { get; set; }

        public string GitHubIndexableDocRoot { get; set; }

    }
}
