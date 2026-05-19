using Windows.Storage;

namespace Files_Tools
{
    public sealed class FileNavigationRequest
    {
        public required StorageFile File { get; init; }
    }
}
