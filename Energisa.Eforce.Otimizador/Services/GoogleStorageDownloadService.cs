using Google.Cloud.Storage.V1;

namespace Energisa.Eforce.Otimizador.Services
{
    public class GoogleStorageDownloadService
    {
        private readonly StorageClient _storageClient;

        public GoogleStorageDownloadService(StorageClient storageClient)
        {
            _storageClient = storageClient;
        }

        public async Task<string> DownloadAsStringAsync(string gsUri)
        {
            if (string.IsNullOrWhiteSpace(gsUri) || !gsUri.StartsWith("gs://"))
                throw new ArgumentException("URI inválida. Esperado formato gs://bucket/path", nameof(gsUri));

            var uri = new Uri(gsUri.Replace("gs://", "https://storage.googleapis.com/"));
            var bucket = uri.Segments[1].TrimEnd('/');
            var objectName = string.Join("", uri.Segments.Skip(2));

            using var memoryStream = new MemoryStream();
            await _storageClient.DownloadObjectAsync(bucket, objectName, memoryStream);
            memoryStream.Position = 0;

            using var reader = new StreamReader(memoryStream);
            return await reader.ReadToEndAsync();
        }
    }
}
