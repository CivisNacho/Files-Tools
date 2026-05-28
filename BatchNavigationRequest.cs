using System.Collections.Generic;
using Windows.Storage;

namespace Files_Tools
{
    public sealed class BatchNavigationRequest
    {
        public required IReadOnlyList<StorageFile> Files { get; init; }
    }
}
