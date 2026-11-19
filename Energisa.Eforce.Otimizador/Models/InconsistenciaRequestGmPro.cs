namespace Energisa.Eforce.Otimizador.Models;

/// <summary>
/// Severidade de uma inconsistência detectada no request enviado à GMPRO.
/// </summary>
public enum SeveridadeInconsistencia
{
    /// <summary>Alerta: a Google pode aceitar o request, mas o resultado pode ser inesperado.</summary>
    Alerta,

    /// <summary>Crítico: a Google rejeita o request (INVALID_REQUEST).</summary>
    Critico
}

/// <summary>
/// Inconsistência encontrada na validação pré-envio do <c>OptimizeToursRequest</c>.
/// </summary>
/// <param name="Severidade">Severidade da inconsistência.</param>
/// <param name="Caminho">Caminho no JSON, por exemplo <c>model.vehicles[72].startWaypoint</c>.</param>
/// <param name="Rotulo">Label do veículo/shipment, quando existir.</param>
/// <param name="Mensagem">Descrição do problema.</param>
public record InconsistenciaRequestGmPro(
    SeveridadeInconsistencia Severidade,
    string Caminho,
    string? Rotulo,
    string Mensagem);
