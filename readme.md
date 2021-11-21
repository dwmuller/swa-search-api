# Azure Static Web Apps search API

This is a simple API intended to be used as a Git submodule to provide a
complete Azure Static Web Apps (SWA) API implementation to support the
management and use of a search index, defined in an Azure Cognitive Search
service, and used to index Markdown files that are presented as static HTML on
the same SWA site. I use this code in several private Web sites, which is why
I've separated it out this way.

This may be helpful to others as example code, but it's not packaged for ease of
use. A Git submodule was the simplest thing that would work for me, and adequate
for my scale of work.

## Software development

For local work, you need a local.settings.json file in the api folder, with the
following settings. These are documented in the 
[Configuration class](/api/Configuration.cs).

```json
{
  "IsEncrypted": false,
  "Values": {
    "FUNCTIONS_WORKER_RUNTIME": "dotnet",
    "AppName": "my-wonderful-app",
    "DocPathTemplate": "/myfiles/{0}/{1}/",
    "GitHubApiKey": "<from a GitHub user account>",
    "GitHubRepoName": "your repo's name",
    "GitHubRepoOwner": "you",
    "GitHubRepoDocRoot": "/src/<or wherever you keep your indexable files>",
    "SearchIndexName": "an index name, possibly different for testing and production",
    "SearchServiceAdminApiKey": "<secondary admin key for search service>",
    "SearchServiceQueryApiKey": "<created in search service via Azure portal>",
    "SearchServiceUri": "https://<your resource name>.search.windows.net",
  }
}

For deployment, all of those values have to be defined in the SWA resource's configuration.

```

