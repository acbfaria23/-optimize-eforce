# Parâmetros sugeridos para replay: lote de 19/11/2026

Status: sugestão revisada pela simulação local de 19/11 e ainda não aplicada. Os valores em produção (`Eforce:ValoresPesoOtimizacao`) continuam como estão.

- Análise do dia: [`2026-11-19-poc-priorizacao.md`](2026-11-19-poc-priorizacao.md)
- Agregados do dia real: [`resultado-2026-11-19.json`](resultado-2026-11-19.json)
- Simulação dos cenários: [`simulacao-2026-11-19.json`](simulacao-2026-11-19.json), gerada por [`simulacao-replay.mjs`](simulacao-replay.mjs)

## Regras de frota

Desde a inclusão das regras R1 e R2 na guideline (seção 6.4), toda proposta precisa usar no máximo as equipes disponíveis e não ultrapassar o km total da referência simulada, que é de 977 km neste lote. Os ajustes desta página, aplicados juntos ou separados, aumentam o km e **violam a R2**. Eles continuam aqui como análise de efeito de cada parâmetro. A proposta válida sai da otimização automática abaixo.

## Otimização automática

`otimizar-parametros.mjs` busca combinações de `PotenciaPesos`, `OsPrazoImportancia`, `CompensacaoImportancia`, `MetrosLimiteRotaPorEquipeSoft`, `CustoKmEquipe` e da janela da vencida. Só aceita combinações que cumprem R1 e R2. Entre elas, escolhe a de maior pontuação, que é o número de OS atendidas mais o de vencidas em rota (a vencida conta em dobro); no empate, vence a de menos km. Cada execução parte do melhor resultado já gravado em `simulacao-2026-11-19.json` e tenta superá-lo.

- **Pelo painel:** rode `node docs/poc/painel/servir.mjs`, abra `http://localhost:5173/painel/` e clique em **Gerar nova simulação otimizada**.
- **Pela linha de comando:** `node docs/poc/otimizar-parametros.mjs "docs/Dados de treinamento" docs/poc/simulacao-2026-11-19.json 120`.

O resultado de cada rodada fica em `otimizacao` e `cenarios.otimizado` dentro de `simulacao-2026-11-19.json`, com histórico. O painel mostra a melhor combinação, as outras viáveis e a checagem de R1 e R2.

Cada execução também entra em `historico-simulacoes.json`. Para cada parâmetro que mudou em relação ao dia, o registro guarda um campo `justificativas` com:

- `mecanismo`: o que a troca faz no objetivo do solver. É um texto fixo por parâmetro e direção.
- `efeito`: a mesma combinação simulada de novo com só aquele parâmetro no valor do dia. Guarda a diferença de OS, vencidas e km e se R1 e R2 continuam passando.

Quando a troca existe para manter a combinação dentro de R1 ou R2, o registro diz isso explicitamente, mesmo que ela custe OS.

## Mudanças de configuração

Chaves de `Eforce:ValoresPesoOtimizacao`. As chaves que não aparecem aqui ficam com o valor de `docs/Dados de treinamento/PesosManuais.txt`.

| Chave | Valor em 19/11 | Replay | Motivo |
|---|---:|---:|---|
| `OsPrazoImportancia` | 1 | 6 | Com `PotenciaPesos` 5, o prazo passa a valer 5^6, o mesmo peso da compensação. |
| `MetrosLimiteRotaPorEquipeSoft` | 1000 | 20000 | Quase toda rota passou de 1 km. A mediana das rotas aprovadas foi 12,7 km. |

```json
{
  "OsPrazoImportancia": "6",
  "MetrosLimiteRotaPorEquipeSoft": "20000"
}
```

A primeira versão desta sugestão também baixava `PotenciaPesos` de 5 para 2. A simulação mostrou que isso piora o resultado: com potência 2, o penalty máximo fica perto de 80 e a mediana perto de 20, menos que o custo de 2 km de deslocamento. O solver passa a pular OS para economizar quilômetro, e os atendimentos caem de 403 para 243 no simulador. `PotenciaPesos` fica em 5.

## Mudança de regra, que precisa de código

A janela da OS de compensação vencida não é uma chave de configuração. Ela sai de `AplicarRegraVencida`, em `EforceParaGmproOrchestrator`, e de `GmProBuilderService`.

| Item | Em 19/11 | Replay |
|---|---|---|
| Visitas alternativas | duas | uma |
| Janela | 08:15–08:15, ou o turno inteiro com custo fixo igual à compensação | turno inteiro |
| Soft end | início do turno (08:15) | início do turno + 4 h (12:15) |
| `costPerHourAfterSoftEndTime` | compensação por hora (cerca de 500) | `MultaAtrasoReguladaHora` (10) |

Em 19/11, a primeira visita era inviável, porque nenhuma equipe chega às 08:15 em ponto. A segunda tinha custo fixo igual à compensação, por exemplo 2.259,93 contra um penalty de 2.172, e ainda cobrava 500 por hora desde 08:15. Pular saía sempre mais barato.

## Mantidos de propósito

| Chave | Valor | Motivo |
|---|---:|---|
| `PotenciaPesos` | 5 | Com valor menor, o penalty fica abaixo do custo de km. Ver a simulação. |
| `PesoRiscoVida`, `PesoClienteEssencial` | 100000 | Não havia OS desses tipos no lote. |
| `CustoKmEquipe` | 10 | O km não separou rotas aprovadas de não aprovadas. |
| `CustoFixoEquipe` | 3 | Onze equipes ficaram sem visita. O custo fixo somou 231. |

## Simulação local de 19/11

A simulação não chamou a Google. [`simulacao-replay.mjs`](simulacao-replay.mjs) lê o `InputESS_1911.json`, recalcula o `PenaltyCost` com a mesma fórmula de `CalcularPesos` e resolve as rotas com uma heurística de inserção gulosa, usando o mesmo modelo de custo do request: penalty, custo por km, km acima do soft max, custo fixo, janelas, pausa, capacidade e limite de 35 km.

Calibração:

- O `PenaltyCost` reconstruído bate com o request em 633 de 633 OS.
- O deslocamento usa 1,38 vez a distância em linha reta, a 30,2 km/h, valores medidos nas 394 transições reais da resposta.
- Com os parâmetros de 19/11, o simulador atende 403 OS, contra 458 da Google; 377 delas coincidem com as da Google. A heurística é mais fraca que o solver real, então os números valem para comparar cenários entre si, não como previsão absoluta.

| Cenário | Atendidas | Vencidas atendidas | Veículos | Km |
|---|---:|---:|---:|---:|
| Google real em 19/11 | 458 | 0 | 77 | 1.078 |
| Simulado com parâmetros de 19/11 | 403 | 0 | 69 | 977 |
| Só soft max 20 km | 444 | 0 | 72 | 1.112 |
| Só `OsPrazoImportancia` 6 | 483 | 12 | 80 | 1.296 |
| Só janela da vencida corrigida | 424 | 37 | 70 | 1.091 |
| Prazo 6 e soft max 20 km, sem corrigir a janela | 487 | 13 | 78 | 1.357 |
| Primeira sugestão (potência 2, prazo 6, 20 km, janela) | 256 | 25 | 49 | 218 |
| **Revisado (potência 5, prazo 6, 20 km, janela)** | **494** | **39** | **79** | **1.356** |

O custo total não aparece na tabela porque a escala do penalty muda entre cenários, e os totais não são comparáveis.

No cenário revisado, o simulador atende 91 OS a mais que o próprio simulador com os parâmetros de 19/11 e coloca 39 das 49 vencidas em rota. Em troca, roda 379 km a mais e usa 10 veículos a mais. A mediana por rota sobe de 12,7 para 16,5 km, e a de visitas fica em 6.

Para gerar de novo:

```bash
node docs/poc/simulacao-replay.mjs "docs/Dados de treinamento" docs/poc/simulacao-AAAA-MM-DD.json
```

## Como medir o replay

Num replay real em shadow, compare com os números de 19/11 nestes campos de [`resultado-2026-11-19.json`](resultado-2026-11-19.json):

| Métrica | 19/11 | Objetivo do replay |
|---|---:|---|
| `vencidasPorGrupo.fora_da_rota` | 49 | perto de 0; o simulador chegou a 10 |
| OS fora da rota | 175 | diminuir |
| `totais.rotasSemVisita` | 11 | diminuir |
| `custosGoogle` acima do soft max | 30.062 | cair e passar a distinguir rota curta de longa |
| Km total | 1.078 | acompanhar; no simulador, a alta foi de 39% |

Para cada variação testada, crie uma seção nova neste arquivo, ou um `parametros-replay-AAAA-MM-DD.md`, com os valores usados e o resultado obtido.
