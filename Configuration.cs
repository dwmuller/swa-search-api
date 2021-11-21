using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

using Newtonsoft.Json;

namespace dwmuller.HomeNet
{
    public class Configuration
    {
        public Configuration(HttpRequest req)
        {
            var root = new ConfigurationBuilder().AddEnvironmentVariables().Build();
            root.Bind(this);
        }

        public Uri SearchServiceUri { get; set; }

        public string SearchIndexName { get; set; } = string.Empty;

        public string SearchServiceQueryApiKey { get; set; } = string.Empty;

        public string SearchServiceAdminApiKey { get; set; } = string.Empty;

        public string GitHubApiKey { get; set; } = string.Empty;

        public string GitHubAppName { get; set; } = string.Empty;

        public string GitHubRepoName { get; set; } = string.Empty;

        public string GitHubRepoOwner { get; set; } = string.Empty;

        public string GitHubRepoDocRoot { get; set; } = string.Empty;

        /// <summary>
        /// Prefix added to the an item's relative path in the source repository
        /// to form a page URL.
        /// </summary>
        /// <remarks>
        /// The path to which this is prepended is relative to <see
        /// cref="GitHubRepoDocRoot"/>.
        /// </remarks>
        public string DocPathPrefix { get; set; } = string.Empty;

        /// <summary>
        /// Suffix added to the an item's relative path in the source repository
        /// to form a page URL.
        /// </summary>
        /// <remarks>
        /// The path to which this is appended is relative to <see
        /// cref="GitHubRepoDocRoot"/>.
        /// </remarks>
        public string DocPathSuffix { get; set; } = string.Empty;


        public string RepoPathToDocPath(string itemPath)
        {
            return DocPathPrefix + Regex.Replace(itemPath, @"\.[^/.]*$", "") + DocPathSuffix;
        }

    }
}
