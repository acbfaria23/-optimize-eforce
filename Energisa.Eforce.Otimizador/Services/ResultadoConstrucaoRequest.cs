using Energisa.God.Domain.Model.Alocacao.OtimizadorRotas;
using Google.Maps.RouteOptimization.V1;

namespace Energisa.Eforce.Otimizador.Services
{
    public class ResultadoConstrucaoRequest
    {
        public string Label { get; set; }
        public OptimizeToursRequest Request { get; set; }
        public string RequestJson { get; set; }
        public ResultadoConversao ResultadoConversao { get; set; }

        /// <summary>Nome do objeto gravado no GCS (para uso no disparo assíncrono via URI).</summary>
        public string ObjectName { get; set; } = string.Empty;

        /// <summary>Referência exata e imutável do input no GCS (§17), capturada no upload.</summary>
        public ReferenciaArmazenamentoGcs ReferenciaInput { get; set; } = null!;

        /// <summary>Mensagem de erro caso a construção tenha sido interrompida (dados insuficientes, etc.).</summary>
        public string? ErroInterrupcao { get; set; }
    }
}