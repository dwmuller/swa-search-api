using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

using Newtonsoft.Json;

namespace dwmuller.HomeNet
{
    public class Configuration
    {
        private IList<Site> sites;
        public Configuration(HttpRequest req)
        {
            var root = new ConfigurationBuilder().AddEnvironmentVariables().Build();
            root.Bind(this);
        }

        public string AppName { get; set; } = string.Empty;

        public Uri SearchServiceUri { get; set; }
        public string SearchIndexName { get; set; } = string.Empty;

        public string SearchServiceQueryApiKey { get; set; } = string.Empty;

        public string SearchServiceAdminApiKey { get; set; } = string.Empty;

        public string GitHubApiKey { get; set; } = string.Empty;

        public async Task<IList<Site>> GetSiteConfigs()
        {
            if (sites == null) await LoadSites();
            return sites;
        }

        public async Task<Site> GetSiteConfig(string siteName)
        {
            if (sites == null) await LoadSites();
            return sites.FirstOrDefault(x => x.SiteName == siteName);
        }

        private Task LoadSites()
        {
            // Tried various ways to load this data from a JSON file on the
            // static site, but there's seemingly no way for a function in
            // Static Web App to reliably get the base URL.
            //
            // The proper way to do this is to store the data in blob storage
            // and read it from there, but this will do for now.
            //
            // None of this configuration data is sensitive. API keys used with
            // it are stored in the normal configuration.
            sites = new Site[] {
                new Site
                {
                    SiteName = "homenet",
                    GitHubRepoName =  "homenet",
                    GitHubRepoOwner = "dwmuller",
                    GitHubRepoDocRoot =  "src/household",
                    DocRoot = "/household",
                    Readers =  new []{"household", "admin"},
                    Administrators = new [] {"admin"}
                }
            };
            return Task.CompletedTask;
        }

        public class Site
        {
            public string SiteName { get; set; } = string.Empty;
            public string GitHubRepoName { get; set; } = string.Empty;
            public string GitHubRepoOwner { get; set; } = string.Empty;
            public string GitHubRepoDocRoot { get; set; } = string.Empty;
            public string DocRoot { get; set; } = string.Empty;

            public IList<string> Readers { get; set; } = new string[0];
            public IList<string> Administrators { get; set; } = new string[0];

        }

    }
    public static class ConfigurationExensions
    {
        public static bool CanRead(this ClaimsPrincipal user, Configuration.Site siteCfg)
        {
            return siteCfg.Readers.Any(r => user.IsInRole(r));
        }
        public static bool CanManage(this ClaimsPrincipal user, Configuration.Site siteCfg)
        {
            return siteCfg.Administrators.Any(r => user.IsInRole(r));
        }
    }
}
