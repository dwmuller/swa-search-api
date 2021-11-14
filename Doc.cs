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
        /// We use a single index for several sites. This value is used to
        /// distinguish tenants. Most searches will filter on this. 
        /// </remarks>
        [SimpleField(IsFilterable = true, IsFacetable = true)]
        public string Site { get; set; }

        /// <summary>
        /// The document's path, an absolute URL path.
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