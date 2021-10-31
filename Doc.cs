using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Indexes.Models;

namespace dwmuller.HomeNet
{
    public partial class Doc
    {

        /// <summary>
        /// The document's identifier, which is its Git hash.
        /// </summary>
        [SearchableField(IsKey = true)]
        public string Id { get; set; }

        /// <summary>
        /// The site this document lives in.
        /// </summary>
        /// <remarks>
        /// We use this index to search several sites. Most searches will filter
        /// on this. 
        /// </remarks>
        [SimpleField(IsFilterable = true, IsFacetable = true)]
        public string Site { get; set; }

        /// <summary>
        /// The document's path, a relative path from the document root in the
        /// repository (not necessarily the repository root), with the file
        /// extension removed. This is also expected to be the trailing part of
        /// a URL path to the online document. It should start with a slash.
        /// </summary>
        [SimpleField]
        public string Path { get; set; }

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