# POC de priorização: lote de 19/11/2026

Cenário: `Roterizacao Energisa Cronjob, Metodo Assincrono: 20261119-073059`.

Fontes, mantidas fora do repositório porque têm coordenadas, IDs de OS e logins de aprovadores:

- `InputESS_1911.json`: `OptimizeToursRequest` enviado à Google.
- `OutputESS_1911.json`: `OptimizeToursResponse`.
- `Executado.json`: rotas por equipe, com o flag `Aprovada`, e as OS não roteirizadas.
- `PesosManuais.txt`: valores de `Eforce:ValoresPesoOtimizacao` neste dia (em `docs/Dados de treinamento`).

Números agregados: [`resultado-2026-11-19.json`](resultado-2026-11-19.json). Para gerar de novo, ou rodar com outro dia:

```bash
node docs/poc/analise-poc.mjs "docs/Dados de treinamento" docs/poc/resultado-AAAA-MM-DD.json
```

Painel visual com o resultado do dia, a simulação e os parâmetros: abra `docs/poc/painel/index.html` no navegador. Para usar o botão de otimização automática, abra pelo servidor local (`node docs/poc/painel/servir.mjs`), porque a busca lê os dados brutos que ficam só na máquina. Para incluir um dia novo, acrescente uma entrada em [`indice.json`](indice.json) e rode `node docs/poc/painel/gerar-dados.mjs`, que atualiza `painel/dados.js`.

"Aprovada" quer dizer que a equipe teve a rota aprovada. Os arquivos não trazem desfecho em campo por OS.

## Resultado

As 49 OS vencidas ficaram todas fora da rota. Onze equipes voltaram sem nenhuma visita, então faltou atendimento e não equipe.

| Grupo | OS | Vencidas |
|---|---:|---:|
| Em equipe aprovada | 285 | 0 |
| Em rota de equipe não aprovada | 173 | 0 |
| Fora da rota | 175 | 49 |

A Google usou 77 dos 88 veículos. Custo da resposta:

| Termo | Custo |
|---|---:|
| Pular OS (`penalty_cost`) | 187.826 |
| Km acima do soft max da rota | 30.062 |
| Custo por km | 10.781 |
| Custo fixo | 231 |

## O que os pesos fizeram neste lote

As 633 OS eram comerciais, reguladas, prioridade normal, sem risco à vida, sem cliente essencial, sem reincidência e com zero pessoas atingidas.

`CalcularPesos` aplica min-max em cada fator. Quando o lote inteiro tem o mesmo valor, o fator vira 0. Por isso `PesoRiscoVida` 100.000, `PesoClienteEssencial` 100.000 e `PesoRegulada` não entraram no `PenaltyCost` deste dia.

Correlação de Pearson com o `PenaltyCost` das 633 OS:

| Atributo | Correlação |
|---|---:|
| Compensação | 0,84 |
| Compensação por hora | 0,70 |
| Horas até vencer | -0,73 |
| Vencida (0/1) | 0,24 |
| Horas aberta | 0,06 |

O maior `PenaltyCost` foi 15.635: base 10 mais 5^6, com `PotenciaPesos` 5 e `CompensacaoImportancia` 6. O prazo, com `OsPrazoImportancia` 1, contribui no máximo 5.

## Por que a OS vencida foi pulada

Para compensação vencida, `AplicarRegraVencida` usa o início do turno como vencimento ajustado. `GmProBuilderService` então monta duas visitas:

1. Janela 08:15–08:15, menor que a duração de 19 minutos da visita.
2. Janela do turno inteiro, com `costPerHourAfterSoftEndTime` igual à compensação por hora (cerca de 510) contando desde 08:15.

Com cerca de 500 por hora, seis horas da segunda janela já custam mais que o maior penalty entre as OS fora da rota (2.839). Para a Google, pagar o penalty sai mais barato que atender.

## Rotas aprovadas e não aprovadas

Considerando só rotas com pelo menos uma visita:

| | Rotas | Visitas (mediana) | Km (mediana) |
|---|---:|---:|---:|
| Aprovadas | 47 | 6 | 12,7 |
| Não aprovadas | 30 | 6 | 11,2 |

Número de visitas e distância não separam as rotas aprovadas das não aprovadas. O motivo da não aprovação não está nos arquivos.

## Parâmetros sugeridos para replay shadow

Um dia não basta para recalibrar o objetivo. Estes valores servem para rodar de novo este mesmo input e comparar. O request publicado hoje não muda.

| Parâmetro | Hoje | Sugestão | Motivo |
|---|---:|---:|---|
| `PesoRiscoVida`, `PesoClienteEssencial` | 100.000 | manter | Não havia OS desses tipos. Com o min-max, o valor absoluto não chega ao penalty. |
| `PotenciaPesos` | 5 | manter | A primeira versão sugeria 2. A simulação mostrou que, com 2, o penalty fica abaixo do custo de km e os atendimentos caem. Ver [`parametros-replay-2026-11-19.md`](parametros-replay-2026-11-19.md). |
| `OsPrazoImportancia` | 1 | 6 | Com potência 5, o prazo passa a valer 5^6, o mesmo peso da compensação. |
| `MetrosLimiteRotaPorEquipeSoft` | 1.000 m | 20.000 m | Quase toda rota passa de 1 km; três quartos das aprovadas passaram de 6,8 km. O termo custou 30.062 sem distinguir rota curta de longa. A mediana das aprovadas foi 12,7 km. |
| `CustoKmEquipe` | 10 | 10 | Km não separou aprovadas de não aprovadas. Sem evidência para mexer. |
| `CustoFixoEquipe` | 3 | 3 | Onze equipes ficaram sem visita. O fixo pesou 231 no total. |
| Janela da OS vencida | soft end 08:15, multa = compensação/hora | turno inteiro, soft end 12:15, multa 10/hora | A multa horária volta a ser `MultaAtrasoReguladaHora`. Na simulação, só o peso do prazo colocou 12 vencidas em rota; com a janela corrigida, foram 39. |

## O que este dia não permite concluir

- Não há desfecho em campo por OS, só a aprovação da rota da equipe.
- Não há OS de risco à vida ou de cliente essencial para testar o peso de 100.000.
- O motivo da não aprovação das 30 rotas com visita não aparece nos dados.

## Como comparar no futuro

Rodar o script com os arquivos de um novo dia gera outro `resultado-AAAA-MM-DD.json` no mesmo formato. Comparar principalmente `vencidasPorGrupo`, `totais.porGrupo`, `totais.rotasSemVisita`, `custosGoogle` e `correlacaoComPenalty`.
