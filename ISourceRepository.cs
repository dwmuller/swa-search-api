using System.Collections.Generic;
using System.Threading.Tasks;

namespace dwmuller.HomeNet
{
    public interface ISourceRepository
    {
        public struct RepoFile
        {
            public string Hash;
            public string Path; // Relative to searched root.
        }

        Task<string> GetFileContent(string itemPath);
        IAsyncEnumerable<RepoFile> GetFiles(string repoDocRoot);
    }
}