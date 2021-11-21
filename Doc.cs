using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Indexes.Models;

namespace dwmuller.HomeNet
{
    /// <summary>
    /// Model for documents stored in the search index.
    /// </summary>
    public partial class Doc
    {

        /// <summary>
        /// The document's path, which may be relative.
        /// </summary>
        [SimpleField]
        public string DocPath { get; set; }

        /// <summary>
        /// The document's identifier, which is its Git hash.
        /// </summary>
        /// <remarks>
        /// We use this as a key for index entries. The doc path would be a more
        /// natural fit, but limitations on the character set make this more
        /// convenient.
        /// </remarks>
        [SearchableField(IsKey = true)]
        public string RepoHash { get; set; }

        /// <summary>
        /// Repository path of the source file, relative to <see
        /// cref="Configuration.GitHubRepoDocRoot"/>, including the file
        /// extension.
        /// </summary>
        [SimpleField]
        public string RepoPath { get; set; }

        /// <summary>
        ///  The document's title, if any, taken from front matter.
        /// </summary>
        [SearchableField(IsSortable = true)]
        public string Title { get; set; }

        /// <summary>
        /// The body of the document, with punctuation removed. Cannot be
        /// retrieved.
        /// </summary>
        /// <remarks>
        /// We'd prefer this field to be hidden, but that apparently makes it
        /// unsearchable.
        /// </remarks>
        [SearchableField]
        public string Body { get; set; }

    }
}