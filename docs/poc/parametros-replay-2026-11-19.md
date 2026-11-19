# Parâmetros sugeridos para replay: lote de 19/11/2026

Status: sugestão, ainda não aplicada. Servem para rodar de novo `InputESS_1911.json` em shadow e comparar com [`resultado-2026-11-19.json`](resultado-2026-11-19.json). Os valores em produção (`Eforce:ValoresPesoOtimizacao`) continuam como estão.

Análise completa: [`2026-11-19-poc-priorizacao.md`](2026-11-19-poc-priorizacao.md).

## Mudanças de configuração

Chaves de `Eforce:ValoresPesoOtimizacao`. As chaves que não aparecem aqui ficam com o valor de `docs/Dados de treinamento/PesosManuais.txt`.

| Chave | Valor em 19/11 | Replay | Motivo |
|---|---:|---:|---|
| `PotenciaPesos` | 5 | 2 | Com 5, 5^6 vale 15.625 e o prazo (5^1) some. |
| `OsPrazoImportancia` | 1 | 6 | O prazo passa a competir com a compensação. |
| `MetrosLimiteRotaPorEquipeSoft` | 1000 | 20000 | Toda rota passou de 1 km. A mediana das rotas aprovadas foi 12,7 km. |

```json
{
  "PotenciaPesos": "2",
  "OsPrazoImportancia": "6",
  "MetrosLimiteRotaPorEquipeSoft": "20000"
}
```

## Mudança de regra, que precisa de código

A janela da OS de compensação vencida não é uma chave de configuração. Ela sai de `AplicarRegraVencida`, em `EforceParaGmproOrchestrator`, e de `GmProBuilderService`.

| Item | Em 19/11 | Replay |
|---|---|---|
| Soft end da janela | início do turno (08:15) | 12:15 |
| Janela | 08:15–08:15 mais o turno inteiro | turno inteiro |
| `costPerHourAfterSoftEndTime` | compensação por hora (cerca de 500) | `MultaAtrasoReguladaHora` (10) |

Sem essa mudança, nenhum ajuste de peso coloca as 49 vencidas em rota: seis horas de multa já custam mais que o maior penalty entre as OS fora da rota (2.839).

## Mantidos de propósito

| Chave | Valor | Motivo |
|---|---:|---|
| `PesoRiscoVida`, `PesoClienteEssencial` | 100000 | Não havia OS desses tipos no lote. |
| `CustoKmEquipe` | 10 | O km não separou rotas aprovadas de não aprovadas. |
| `CustoFixoEquipe` | 3 | Onze equipes ficaram sem visita. O custo fixo somou 231. |

## Como medir o replay

Compare com os números de 19/11 nestes campos do resultado:

| Métrica | 19/11 | Objetivo do replay |
|---|---:|---|
| `vencidasPorGrupo.fora_da_rota` | 49 | perto de 0 |
| OS fora da rota | 175 | diminuir |
| `totais.rotasSemVisita` | 11 | diminuir |
| `custosGoogle` acima do soft max | 30.062 | cair e passar a distinguir rota curta de longa |
| `correlacaoComPenalty.horasAteVencer` | -0,73 | ficar mais negativa |

Para cada variação testada, crie uma seção nova neste arquivo, ou um `parametros-replay-AAAA-MM-DD.md`, com os valores usados e o resultado obtido.
