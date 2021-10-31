using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;

namespace dwmuller.HomeNet
{
    static class IndexTools
    {
        public static SearchClient CreateSearchClient(Configuration cfg, bool withWritePermissions = false)
        {
            var creds = new Azure.AzureKeyCredential(
                withWritePermissions
                ? cfg.SearchServiceAdminApiKey
                : cfg.SearchServiceQueryApiKey);
            return new SearchClient(cfg.SearchServiceUri, cfg.SearchIndexName, creds);
        }

        public static SearchIndexClient CreateIndexClient(Configuration cfg)
        {
            var creds = new Azure.AzureKeyCredential(cfg.SearchServiceAdminApiKey);
            return new SearchIndexClient(cfg.SearchServiceUri, creds);
        }
    }
}