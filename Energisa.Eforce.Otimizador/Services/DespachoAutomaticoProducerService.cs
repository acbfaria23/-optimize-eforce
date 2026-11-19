using Energisa.Context.Interface;
using Energisa.God.Data.Constants;
using Energisa.God.Domain.Model._OrdemServico_;
using Energisa.God.Domain.Model.Alocacao;
using Energisa.God.Domain.Model.Alocacao.OtimizadorRotas;
using Energisa.Message.Interface;

namespace Energisa.Eforce.Otimizador.Services;

public class DespachoAutomaticoProducerService
{
    private readonly IProducerChannel<string, Alocacao> _producer;

    public DespachoAutomaticoProducerService(IBrokerProvider brokerProvider)
    {
        _producer = brokerProvider.CreateProducer<string, Alocacao>(
            brokerProvider.CreateQueue(ContextDefinition.Alocacao.Queue.AlocacaoAutomatica.Name));
    }

    public async Task ProduceDespachoAsync(RotaEquipe rotaEquipe, IContextRepository context)
    {
        var ordensServico = rotaEquipe.OrdensServico
            .Where(os => !os.IsAlmoco)
            .Select(os => new OrdemServicoReference { Id = os.Id })
            .ToList();

        if (ordensServico.Count == 0)
            return;

        var alocacao = new Alocacao
        {
            OrdensServico = ordensServico,
            Equipes = [new EquipeAlocada { IdExterno = rotaEquipe.Equipe }]
        };

        await _producer.ProduceAsync(rotaEquipe.Equipe, alocacao, context);
    }
}
