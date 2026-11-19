using Energisa.Eforce.Otimizador.Models.Reotimizacao;
using Energisa.God.Domain.Model.Alocacao.OtimizadorRotas;
using Google.Cloud.Storage.V1;
using Microsoft.Extensions.Logging;

namespace Energisa.Eforce.Otimizador.Services;

public class GoogleCloudStorageService
{
    private readonly StorageClient _storageClient;
    private readonly ILogger<GoogleCloudStorageService> _logger;

    public GoogleCloudStorageService(StorageClient storageClient, ILogger<GoogleCloudStorageService> logger)
    {
        _storageClient = storageClient;
        _logger = logger;
    }

    public async Task UploadJsonAsync(string bucketName, string objectName, string jsonContent, CancellationToken cancellationToken = default)
    {
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(jsonContent));

        _logger.LogInformation("Enviando '{ObjectName}' para o bucket '{BucketName}'...", objectName, bucketName);

        var uploadedObject = await _storageClient.UploadObjectAsync(
            bucketName,
            objectName,
            "application/json",
            stream,
            cancellationToken: cancellationToken);

        _logger.LogInformation("'{ObjectName}' enviado para o bucket '{BucketName}' com sucesso. Geração: {Generation}",
            objectName, bucketName, uploadedObject.Generation);
    }

    /// <summary>
    /// Faz o upload do JSON e retorna a <see cref="ReferenciaArmazenamentoGcs"/> exata e imutável
    /// (bucket, nome do objeto, geração e hash MD5) para uso posterior na recuperação garantida
    /// do mesmo conteúdo gravado (§17), sem depender de convenção de nomes.
    /// </summary>
    public async Task<ReferenciaArmazenamentoGcs> UploadJsonComReferenciaAsync(
        string bucketName, string objectName, string jsonContent, CancellationToken cancellationToken = default)
    {
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(jsonContent));

        _logger.LogInformation("Enviando '{ObjectName}' para o bucket '{BucketName}'...", objectName, bucketName);

        var uploadedObject = await _storageClient.UploadObjectAsync(
            bucketName,
            objectName,
            "application/json",
            stream,
            cancellationToken: cancellationToken);

        _logger.LogInformation(
            "'{ObjectName}' enviado para o bucket '{BucketName}' com sucesso. Geração: {Generation}, Md5: {Md5}",
            objectName, bucketName, uploadedObject.Generation, uploadedObject.Md5Hash);

        return new ReferenciaArmazenamentoGcs
        {
            BucketName = bucketName,
            ObjectName = objectName,
            Generation = uploadedObject.Generation ?? 0,
            Md5Hash = uploadedObject.Md5Hash,
            DataUploadUtc = DateTime.UtcNow
        };
    }

    public async Task<string> DownloadJsonAsync(string bucketName, string objectName, CancellationToken cancellationToken = default)
    {
        using var stream = new MemoryStream();

        _logger.LogInformation("Baixando '{ObjectName}' do bucket '{BucketName}'...", objectName, bucketName);

        await _storageClient.DownloadObjectAsync(
            bucketName,
            objectName,
            stream,
            cancellationToken: cancellationToken);

        stream.Position = 0;
        using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);
        return await reader.ReadToEndAsync(cancellationToken);
    }

    /// <summary>
    /// Baixa o conteúdo referenciado por uma <see cref="ReferenciaArmazenamentoGcs"/>, exigindo a
    /// geração exata gravada originalmente (imutabilidade garantida mesmo que o objeto tenha sido
    /// sobrescrito por um upload posterior no mesmo nome) e validando o hash MD5 retornado (§17).
    /// Lança <see cref="InvalidOperationException"/> caso o hash não confira.
    /// </summary>
    public async Task<string> DownloadJsonPorReferenciaAsync(
        ReferenciaArmazenamentoGcs referencia, CancellationToken cancellationToken = default)
    {
        using var stream = new MemoryStream();

        _logger.LogInformation(
            "Baixando '{ObjectName}' (geração {Generation}) do bucket '{BucketName}'...",
            referencia.ObjectName, referencia.Generation, referencia.BucketName);

        var downloadOptions = new DownloadObjectOptions { Generation = referencia.Generation };

        var downloadedObject = await _storageClient.DownloadObjectAsync(
            referencia.BucketName,
            referencia.ObjectName,
            stream,
            downloadOptions,
            cancellationToken: cancellationToken);

        if (!string.IsNullOrWhiteSpace(referencia.Md5Hash) &&
            !string.Equals(downloadedObject.Md5Hash, referencia.Md5Hash, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Hash MD5 divergente ao recuperar 'gs://{referencia.BucketName}/{referencia.ObjectName}#{referencia.Generation}'. " +
                $"Esperado '{referencia.Md5Hash}', obtido '{downloadedObject.Md5Hash}'. " +
                "O conteúdo pode ter sido corrompido ou a referência está inconsistente.");
        }

        stream.Position = 0;
        using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);
        return await reader.ReadToEndAsync(cancellationToken);
    }

    /// <summary>
    /// Obtém a <see cref="ReferenciaArmazenamentoGcs"/> (geração e hash MD5 atuais) de um objeto
    /// já existente no bucket, escrito por terceiros (ex: o próprio Google Route Optimization,
    /// ao gravar o arquivo de output de uma otimização assíncrona). Deve ser usado imediatamente
    /// após a confirmação externa de que o objeto foi criado (§17), para fixar uma referência
    /// exata e imutável, em vez de reconstruir o path por convenção.
    /// </summary>
    public async Task<ReferenciaArmazenamentoGcs> ObterReferenciaObjetoAsync(
        string bucketName, string objectName, CancellationToken cancellationToken = default)
    {
        var obj = await _storageClient.GetObjectAsync(bucketName, objectName, cancellationToken: cancellationToken);

        return new ReferenciaArmazenamentoGcs
        {
            BucketName = bucketName,
            ObjectName = objectName,
            Generation = obj.Generation ?? 0,
            Md5Hash = obj.Md5Hash,
            DataUploadUtc = DateTime.UtcNow
        };
    }
}
