namespace Energisa.Eforce.Otimizador.Services;

/// <summary>
/// Resultado do disparo assíncrono de uma otimização na API Google Route Optimization.
/// </summary>
/// <param name="OperationName">
/// Identificador da operação de longa duração criado pela Google.
/// </param>
/// <param name="InputUri">
/// URI do arquivo de entrada no Google Cloud Storage.
/// </param>
/// <param name="OutputUri">
/// URI esperada para o arquivo de saída no Google Cloud Storage.
/// </param>
/// <param name="RastreamentoNotificado">
/// Indica se a API responsável por acompanhar a operação confirmou
/// o início do rastreamento.
/// </param>
public sealed record ResultadoDisparoOtimizacaoAssincrona(
    string OperationName,
    string InputUri,
    string OutputUri,
    bool RastreamentoNotificado);