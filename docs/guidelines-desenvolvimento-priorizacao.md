# Guidelines de desenvolvimento da priorização adaptativa

## 1. Finalidade e escopo

Este documento transforma o plano de priorização adaptativa em regras práticas para iniciar o desenvolvimento antes da leitura do código-fonte. O objetivo inicial não é colocar ML decidindo rotas em produção, mas criar uma fundação segura, reproduzível e mensurável para:

1. representar uma decisão de priorização;
2. separar factibilidade de preferência;
3. reproduzir a política atual;
4. coletar dados confiáveis;
5. comparar baselines;
6. introduzir modelos somente em shadow;
7. promover mudanças de forma gradual e reversível.

As interfaces abaixo são propostas tecnológicas neutras. Nomes e formatos devem ser adaptados à stack após a leitura do repositório, preservando a semântica.

## 2. Fazer agora versus adiar

### Fazer agora

- definir o contrato canônico de decisão e eventos;
- criar um catálogo versionado de hard constraints;
- isolar o score aprendível do motor de constraints;
- encapsular a política manual existente como baseline/fallback;
- definir uma fitness multiobjetivo explícita e decomposta;
- registrar snapshots, candidatos, decisões, overrides e resultados;
- criar replay determinístico e testes de invariantes;
- introduzir feature flags, shadow mode, kill switch e rollback;
- definir SLOs, dashboards e alertas;
- preparar dataset temporal reproduzível, sem leakage;
- estabelecer versionamento conjunto de política, modelo, features, constraints e objective;
- implementar explicações derivadas da própria decisão;
- operar primeiro com heurística/modelos preditivos em shadow.

### Adiar

- substituir automaticamente a política atual por ML;
- exploration em produção;
- contextual bandit;
- reinforcement learning;
- otimização end-to-end opaca;
- treino tendo o peso manual como target;
- feature store ou plataforma ML complexa antes de haver necessidade comprovada;
- auto-retraining com autopromoção;
- otimização de equidade sem definições legais e operacionais aprovadas;
- simulador sofisticado antes de existir replay confiável;
- replanejamento agressivo de rotas já confirmadas;
- uso de texto livre, embeddings ou dados sensíveis sem governança;
- mudança simultânea de taxonomia, constraints, objective e modelo.

Critério geral: adiar qualquer mecanismo que aumente autonomia antes de existirem telemetria, replay, guardrails, avaliação e rollback compatíveis com seu risco.

## 3. Princípios obrigatórios

Os itens abaixo são invariantes de arquitetura:

1. **Hard constraints não são pesos.** Uma regra obrigatória deve eliminar ou invalidar uma ação, nunca depender de uma constante grande na fitness.
2. **ML nunca amplia o conjunto factível.** O modelo só pontua candidatos previamente autorizados.
3. **Validação dupla.** Todo plano deve passar pelo motor de constraints antes e depois da otimização.
4. **Política manual não é ground truth.** É baseline, fallback e contexto histórico; não é rótulo absoluto.
5. **Tempo da decisão é sagrado.** Features só podem usar informação disponível em `decision_time`.
6. **Toda decisão é reproduzível.** Entrada, versões, configuração e saída devem permitir replay.
7. **Score é decomposto.** Não aceitar score final sem componentes, unidades e versões.
8. **Fallback é caminho principal testado.** Não pode ser código residual raramente exercitado.
9. **Ausência de dados reduz autonomia.** Dado inválido, stale, OOD ou timeout deve levar a fallback ou revisão humana.
10. **Caudas importam.** Métricas críticas usam percentis/CVaR e segmentação, não apenas médias.
11. **Mudanças são reversíveis.** Feature flag e kill switch precedem qualquer ativação.
12. **Sem decisão silenciosa.** Override, fallback, rejeição e erro devem ter reason codes.
13. **Configuração é código versionado.** Constraints, coeficientes e schemas passam por revisão.
14. **Uma mudança causal por experimento.** Evitar alterar modelo, objective e regras ao mesmo tempo.
15. **Privacidade por padrão.** IDs pseudonimizados, dados mínimos e acesso controlado.

## 4. Arquitetura de referência e limites de módulos

### 4.1 Fluxo lógico

```text
Fontes operacionais
        |
        v
Snapshot Builder ---> Schema Validator
        |
        v
Constraint Engine ----> Rejected Candidates + reason_codes
        |
        v
Candidate Generator ---> Route Cost Provider (ex.: Google)
        |
        v
Feature Calculator
        |
        +----> Baseline Scorer
        |
        +----> Learned Scorer (shadow inicialmente)
        |
        v
Objective Composer ---> Optimizer/Solver
        |
        v
Plan Validator ---> Policy/Guardrail Gate ---> Recommendation
        |                                      |
        +----------------> Event Logger <------+
                                               |
                                          Outcome Joiner
```

### 4.2 Responsabilidades

#### `snapshot-builder`

- monta uma visão imutável do estado no instante da decisão;
- resolve unidades, timezone e referências;
- não calcula prioridade;
- rejeita ou marca dados stale/incompletos.

#### `schema-validator`

- valida forma, tipos, enums, intervalos e compatibilidade de versões;
- produz erros estruturados;
- não aplica regras operacionais.

#### `constraint-engine`

- determina factibilidade de OS, equipe, par e plano;
- retorna `allowed` e `reason_codes`;
- é determinístico, sem dependência do learned scorer;
- mantém regras com proprietário, vigência, fonte e severidade.

#### `candidate-generator`

- gera apenas alternativas potencialmente executáveis;
- controla explosão combinatória;
- registra o conjunto considerado, inclusive candidatos descartados;
- não ordena por score aprendido.

#### `route-cost-provider`

- encapsula API externa, cache, timeout, retries e fallback;
- devolve distância/duração com origem, momento e qualidade;
- não recebe semântica de criticidade nem decide prioridade.

#### `feature-calculator`

- transforma snapshot e candidatos em features versionadas;
- usa as mesmas definições em treino e serving;
- não consulta informação posterior ao `decision_time`;
- gera também indicadores de missingness e freshness.

#### `baseline-scorer`

- reproduz a política atual;
- oferece ao menos uma heurística determinística independente dos pesos históricos;
- retorna score decomposto e explicação;
- é o fallback padrão.

#### `learned-scorer`

- prediz componentes como risco de SLA, duração, primeira resolução ou benefício marginal;
- não acessa candidatos inviáveis;
- não executa plano nem altera constraints;
- devolve incerteza, cobertura/OOD e versão.

#### `objective-composer`

- combina componentes normalizados e configuração aprovada;
- suporta prioridade lexicográfica;
- não contém lógica de factibilidade;
- torna os trade-offs auditáveis.

#### `optimizer`

- seleciona/atribui/sequencia entre candidatos factíveis;
- respeita orçamento de tempo e retorna status de solução;
- não busca dados adicionais de produção;
- aceita baseline ou learned scores pela mesma interface.

#### `plan-validator`

- reaplica constraints ao plano completo;
- verifica starvation, limites críticos, consistência e dados stale;
- bloqueia publicação em caso de dúvida.

#### `policy-gate`

- aplica feature flags, escopo de rollout, confiança mínima e circuit breaker;
- escolhe entre `manual`, `baseline`, `shadow`, `assist` e `active`;
- não recalcula scores.

#### `event-logger`

- persiste envelope, snapshot ou referência imutável, candidatos, scores, decisão e razões;
- falha de auditoria deve impedir modo autônomo quando o evento for obrigatório;
- não deve bloquear fallback seguro por indisponibilidade não crítica.

#### `outcome-joiner`

- associa execução e feedback tardio à decisão;
- mantém rótulos com maturidade, censura e provenance;
- nunca reescreve o snapshot histórico.

### 4.3 Dependências permitidas

```text
constraints <- snapshot
candidates  <- snapshot + constraints + route costs
features    <- snapshot + candidates
scores      <- features
objective   <- scores + configuração
plan        <- candidates + objective
validation  <- plan + constraints + snapshot
```

Dependências proibidas:

- `constraint-engine -> learned-scorer`;
- `learned-scorer -> outcome futuro`;
- `route-cost-provider -> pesos de urgência`;
- `event-logger -> alteração da decisão`;
- `optimizer -> banco operacional sem snapshot`;
- `baseline-scorer -> output do learned-scorer`.

## 5. Contratos e esquemas sugeridos

### 5.1 Envelope comum de eventos

```json
{
  "event_id": "uuid",
  "event_type": "decision.snapshot.created",
  "schema_version": "1.0.0",
  "occurred_at": "2026-11-16T19:00:00Z",
  "ingested_at": "2026-11-16T19:00:01Z",
  "producer": "route-prioritizer",
  "correlation": {
    "decision_id": "dec_...",
    "route_id": "route_...",
    "os_id": null,
    "team_id": null
  },
  "payload": {}
}
```

Regras:

- timestamps em UTC e formato explícito;
- `event_id` idempotente;
- `schema_version` semântico;
- payload imutável;
- PII fora do envelope;
- eventos fora de ordem são esperados e reconciliados por timestamp e versão.

### 5.2 `DecisionSnapshot`

```json
{
  "decision_id": "dec_...",
  "decision_time": "2026-11-16T19:00:00Z",
  "horizon_minutes": 240,
  "region_id": "region_...",
  "work_orders": [
    {
      "os_id": "os_...",
      "category": "LIFE_RISK",
      "severity": "CRITICAL",
      "created_at": "2026-11-16T18:10:00Z",
      "sla_due_at": "2026-11-16T19:30:00Z",
      "location": {"cell_id": "geo_cell_...", "lat": null, "lon": null},
      "requirements": ["SKILL_X"],
      "service_duration_estimate_minutes": 35,
      "data_quality": {"freshness_seconds": 2, "missing_fields": []}
    }
  ],
  "teams": [
    {
      "team_id": "team_...",
      "availability": "AVAILABLE",
      "shift_end_at": "2026-11-16T23:00:00Z",
      "skills": ["SKILL_X"],
      "capacity": 1,
      "location": {"cell_id": "geo_cell_..."}
    }
  ],
  "active_routes": [],
  "context": {
    "traffic_as_of": "2026-11-16T18:59:30Z",
    "weather_code": "NORMAL"
  },
  "input_hash": "sha256:..."
}
```

Localização exata deve ser armazenada apenas onde necessária, com controles de acesso e retenção. Dataset analítico deve preferir geocélula ou distância derivada.

### 5.3 `ConstraintEvaluation`

```json
{
  "decision_id": "dec_...",
  "candidate_id": "cand_...",
  "allowed": false,
  "violations": [
    {
      "constraint_id": "TEAM_REQUIRED_SKILL",
      "constraint_version": "2.1.0",
      "scope": "TEAM_OS_PAIR",
      "severity": "BLOCK",
      "reason_code": "MISSING_REQUIRED_SKILL",
      "owner": "operations-safety"
    }
  ],
  "evaluated_at": "2026-11-16T19:00:00Z",
  "constraint_set_version": "2026.11.3"
}
```

### 5.4 `Candidate`

```json
{
  "candidate_id": "cand_...",
  "decision_id": "dec_...",
  "os_id": "os_...",
  "team_id": "team_...",
  "insertion_position": 2,
  "feasibility": "FEASIBLE",
  "route_cost": {
    "incremental_minutes": 12,
    "incremental_km": 7.4,
    "provider": "google",
    "estimate_as_of": "2026-11-16T18:59:40Z",
    "quality": "FRESH"
  }
}
```

### 5.5 `ScoreResult`

```json
{
  "candidate_id": "cand_...",
  "scorer_type": "BASELINE",
  "scorer_version": "baseline-1.0.0",
  "feature_set_version": "features-1.0.0",
  "components": {
    "risk_cost": 0.72,
    "sla_cost": 0.61,
    "waiting_cost": 0.20,
    "travel_cost": 0.15,
    "quality_cost": 0.08,
    "overtime_cost": 0.00,
    "instability_cost": 0.10
  },
  "total_score": 1.86,
  "uncertainty": null,
  "ood": false,
  "explanation_codes": ["HIGH_SLA_RISK", "LOW_INSERTION_COST"]
}
```

Convenção: maior custo é pior; o optimizer minimiza. Não misturar convenções “maior é melhor” entre componentes.

### 5.6 `PlanDecision`

```json
{
  "decision_id": "dec_...",
  "mode": "SHADOW",
  "policy_version": "policy-1.2.0",
  "constraint_set_version": "2026.11.3",
  "objective_version": "objective-1.0.0",
  "model_version": null,
  "feature_set_version": "features-1.0.0",
  "selected_candidates": ["cand_..."],
  "optimizer_status": "OPTIMAL_OR_BOUNDED",
  "runtime_ms": 183,
  "fallback": false,
  "guardrail_results": [],
  "decision_hash": "sha256:..."
}
```

### 5.7 `ExecutionOutcome`

```json
{
  "decision_id": "dec_...",
  "os_id": "os_...",
  "team_id": "team_...",
  "action_taken": "ACCEPTED",
  "override_reason_code": null,
  "dispatched_at": "2026-11-16T19:01:00Z",
  "arrived_at": "2026-11-16T19:22:00Z",
  "completed_at": "2026-11-16T20:05:00Z",
  "sla_breached": false,
  "first_time_fix": true,
  "reopened_within_window": false,
  "label_maturity": "MATURE",
  "outcome_schema_version": "1.0.0"
}
```

## 6. Hard constraints versus learned score

### 6.1 Classificação prática

| Pergunta | Se “sim” | Tratamento |
|---|---|---|
| A violação é ilegal, insegura ou fisicamente impossível? | Hard | Bloquear candidato/plano |
| Há limite formal que nunca pode ser trocado por eficiência? | Hard | Constraint explícita |
| É um trade-off aceitável dentro de uma faixa? | Learned/configurado | Componente do objective |
| A regra é uma preferência histórica sem owner/fonte? | Incerto | Baseline temporário + decisão de governança |

Exemplos hard:

- habilidade ou credencial obrigatória;
- equipe indisponível;
- capacidade física;
- jornada/descanso obrigatório;
- área proibida;
- precedência técnica;
- limite formal de atendimento crítico.

Exemplos aprendíveis/configuráveis:

- risco de SLA;
- custo incremental de viagem;
- probabilidade de primeira resolução;
- estabilidade da rota;
- horas extras dentro de limites permitidos;
- preferência por maior proficiência;
- custo de postergação.

### 6.2 API mínima do motor de constraints

```text
evaluate_os(os, snapshot) -> ConstraintEvaluation
evaluate_team(team, snapshot) -> ConstraintEvaluation
evaluate_pair(os, team, snapshot) -> ConstraintEvaluation
validate_plan(plan, snapshot) -> PlanValidation
```

Propriedades obrigatórias:

- mesma entrada + mesmas versões = mesma saída;
- reason code para cada rejeição;
- cobertura unitária por regra;
- testes de fronteira;
- nenhum acesso a model serving;
- execução dentro do orçamento de latência;
- fail closed para regra crítica não avaliável.

### 6.3 Tratamento de regras incertas

Não converter peso muito alto automaticamente em hard constraint. Criar registro de decisão com:

- regra atual;
- exemplos reais;
- owner operacional;
- fundamento normativo;
- consequência da violação;
- classificação proposta;
- data e aprovadores.

Até a decisão, manter comportamento atual no baseline e impedir ativação autônoma no segmento ambíguo.

## 7. Telemetria, eventos e dataset

### 7.1 Eventos obrigatórios

| Evento | Quando | Conteúdo mínimo |
|---|---|---|
| `decision.snapshot.created` | início | snapshot/hash, qualidade, versões |
| `decision.candidates.generated` | após constraints | todos os candidatos e rejeições |
| `decision.route_costs.resolved` | após provedor | estimativas, cache, erro/fallback |
| `decision.scores.computed` | por scorer | componentes, total, incerteza |
| `decision.plan.proposed` | após optimizer | plano, status, latência |
| `decision.plan.validated` | após validação | guardrails e violações |
| `decision.recommendation.published` | saída | modo, política, plano/fallback |
| `decision.action.observed` | ação real | aceitação/override e motivo |
| `execution.status.changed` | execução | timestamps e estado |
| `outcome.label.updated` | feedback | rótulo, maturidade, janela |

### 7.2 Regras de logging

- registrar recomendação e ação executada separadamente;
- registrar candidatos não escolhidos;
- registrar por que um candidato não existiu ou foi bloqueado;
- registrar política e configuração efetivas, não apenas “latest”;
- não registrar texto livre sensível por padrão;
- não produzir features offline diretamente de tabelas finais sem respeitar `decision_time`;
- medir perda, duplicação, atraso e ordem dos eventos;
- manter idempotência e lineage até a fonte.

### 7.3 Construção do dataset

Uma linha de treino deve representar uma unidade bem definida: OS no snapshot, par equipe–OS ou candidato de inserção. Não misturar granularidades no mesmo target.

Camadas:

1. **Bronze:** eventos imutáveis conforme recebidos.
2. **Silver:** eventos validados, deduplicados e temporalmente reconciliados.
3. **Gold:** exemplos point-in-time correct com features, ação, propensão quando houver e outcome maduro.

Manifesto do dataset:

```yaml
dataset_id: priority-training-2026-11-16
schema_version: 1.0.0
feature_set_version: features-1.0.0
label_definition_version: labels-1.0.0
source_window:
  start: 2026-08-01T00:00:00Z
  end: 2026-10-31T23:59:59Z
decision_cutoff_field: decision_time
outcome_maturity_days: 14
split_strategy: temporal
source_snapshot_hash: sha256:...
code_commit: git-sha
```

### 7.4 Qualidade mínima antes de treinar

- cobertura e missingness por feature e segmento;
- unicidade de IDs e integridade de joins;
- taxa de eventos tardios;
- labels maduros versus censurados;
- verificação automática de leakage;
- distribuição temporal e por região/categoria;
- divergência entre recomendação e ação;
- documentação de mudanças de taxonomia;
- exclusão ou marcação de incidentes de dados;
- relatório de representatividade.

Não treinar com o score/peso manual como target. Se usado como feature auxiliar em experimento, identificá-lo, executar ablação sem ele e impedir que seja a única fonte de sinal.

## 8. Baseline, modos e feature flags

### 8.1 Baselines obrigatórios

- `manual_v1`: comportamento atual encapsulado;
- `deterministic_v1`: criticidade formal, SLA restante, idade e custo de inserção;
- `fixed_multiobjective_v1`: objective explícito com coeficientes fixos aprovados;
- `learned_shadow_vN`: modelos preditivos sem efeito na decisão.

Todos devem aceitar o mesmo snapshot/candidate contract e produzir o mesmo `ScoreResult`.

### 8.2 Modos de execução

- `OFF`: comportamento legado intacto;
- `LOG_ONLY`: apenas snapshot e outcomes;
- `SHADOW`: calcula alternativa, nunca publica;
- `ASSIST`: mostra recomendação; humano confirma;
- `ACTIVE_CANARY`: automatiza somente escopo autorizado;
- `ACTIVE`: política promovida;
- `FALLBACK`: baseline determinístico;

### 8.3 Feature flags mínimas

```yaml
prioritization.enabled: false
prioritization.mode: OFF
prioritization.shadow_policy: learned-shadow-v1
prioritization.active_policy: manual-v1
prioritization.allowed_regions: []
prioritization.allowed_categories: []
prioritization.excluded_criticalities: [CRITICAL]
prioritization.max_model_latency_ms: 100
prioritization.min_model_confidence: 0.80
prioritization.kill_switch: false
prioritization.audit_required_for_active: true
```

Regras:

- configuração validada por schema;
- defaults seguros;
- mudança auditada com autor e motivo;
- flags avaliadas server-side;
- escopo cumulativamente restritivo;
- kill switch independente do deploy;
- configuração testada em replay antes da promoção.

## 9. Fitness/objective

### 9.1 Estrutura

Usar camadas lexicográficas:

1. factibilidade;
2. proteção crítica;
3. SLA/espera;
4. eficiência/qualidade;
5. estabilidade/equidade operacional.

Dentro das camadas permitidas:

```text
J(P) =
  λrisk        * C_risk
+ λsla         * C_sla
+ λwait        * C_wait
+ λtravel      * C_travel
+ λquality     * C_quality
+ λovertime    * C_overtime
+ λinstability * C_instability
+ λequity      * C_equity
```

### 9.2 Contrato de um componente

Cada componente deve declarar:

- nome e versão;
- semântica;
- unidade de entrada;
- direção (`minimize`);
- transformação/normalização;
- faixa esperada e clipping;
- comportamento para missing;
- owner;
- testes;
- explicação correspondente.

Exemplo:

```yaml
name: sla_cost
version: 1.0.0
input: predicted_breach_probability
input_unit: probability
direction: minimize
normalization: identity
expected_range: [0.0, 1.0]
missing_behavior: fallback
owner: operations-optimization
```

### 9.3 Normalização

- risco/probabilidade: `[0, 1]`, calibrado;
- atraso: dividir por SLA ou limite operacional, com penalidade convexa após 1;
- espera: dividir por limite da classe, com termo antistarvation;
- viagem: minutos incrementais divididos por horizonte ou referência fixa aprovada;
- horas extras: excesso sobre jornada dividido por tolerância permitida;
- instabilidade: proporção ponderada de compromissos alterados;
- qualidade: probabilidade calibrada de falha/reincidência.

Evitar normalização por estatística móvel sem versionamento. Uma alteração de escala é alteração do objective e exige replay e revisão.

### 9.4 Configuração e tuning

- coeficientes em arquivo versionado;
- faixas aprovadas por Operações/Segurança;
- análise de sensibilidade e fronteira de Pareto;
- nenhum tuning diretamente no canário;
- proibir valor “infinito” ou magic number;
- reportar contribuição absoluta e percentual;
- testar invariância a tamanho do lote e número de equipes.

## 10. Pipeline de treino e serving

### 10.1 Treino

```text
Event Store
  -> validação/deduplicação
  -> point-in-time joins
  -> manifesto do dataset
  -> split temporal
  -> treino
  -> calibração
  -> avaliação por segmento
  -> testes de guardrail
  -> model registry (candidate)
  -> shadow
```

Requisitos:

- ambiente e dependências fixados;
- sementes registradas;
- dataset e código imutáveis;
- treino e calibração separados corretamente;
- benchmark contra todos os baselines;
- métricas e intervalos de confiança;
- model card;
- aprovação explícita antes de shadow/promoção;
- champion–challenger;
- sem autopromoção.

### 10.2 Primeiros modelos

Fazer agora em shadow:

- duração esperada do serviço;
- probabilidade de violação de SLA sob cenários definidos;
- probabilidade de primeira resolução;
- benefício marginal de atender agora versus postergar, quando o target puder ser definido sem leakage.

LTR pode ser testado depois desses componentes e comparado com o objective explícito. Bandit e RL permanecem adiados.

### 10.3 Serving

Interface sugerida:

```json
{
  "request_id": "req_...",
  "decision_id": "dec_...",
  "model_name": "sla-risk",
  "model_version": "1.0.0",
  "feature_set_version": "features-1.0.0",
  "instances": [
    {"candidate_id": "cand_...", "features": {}}
  ]
}
```

Resposta:

```json
{
  "request_id": "req_...",
  "predictions": [
    {
      "candidate_id": "cand_...",
      "value": 0.31,
      "uncertainty": 0.07,
      "ood": false,
      "explanation_factors": []
    }
  ],
  "model_version": "1.0.0",
  "runtime_ms": 18
}
```

Regras de serving:

- timeout estrito;
- batching limitado;
- idempotência;
- model version fixada por request/policy;
- validação de schema e feature version;
- detecção de OOD/missing;
- sem chamada recursiva a sistemas operacionais;
- cache somente quando semântica e freshness permitirem;
- fallback local/determinístico;
- métricas sem labels sensíveis.

### 10.4 Paridade treino-serving

- uma biblioteca ou especificação única de features;
- testes golden offline/online;
- comparação de distribuição e valores;
- timestamps e timezone idênticos;
- mesmas regras de missing e clipping;
- bloqueio de deploy quando versões incompatíveis.

## 11. Estratégia de testes

### 11.1 Pirâmide mínima

#### Unitários

- cada hard constraint;
- fronteiras de SLA, jornada e janela;
- normalização e clipping;
- score decomposition;
- reason codes;
- seleção de fallback;
- serialização/versionamento.

#### Property-based

- plano publicado nunca contém candidato inviável;
- adicionar custo positivo não melhora o mesmo plano, salvo mudança lexicográfica documentada;
- reordenação da entrada não altera saída determinística;
- nenhuma OS ultrapassa limite antistarvation permitido;
- score permanece finito;
- valores ausentes nunca criam prioridade artificial.

#### Contract

- produtor/consumidor de eventos;
- schema compatibility;
- route provider;
- model serving;
- optimizer;
- feature flags.

#### Integração

- snapshot até recomendação;
- timeout do Google;
- timeout/erro do modelo;
- audit logger indisponível;
- dataset join;
- override e outcome tardio.

#### Replay/golden

- casos históricos anonimizados;
- decisão atual reproduzida por `manual_v1`;
- casos críticos aprovados por especialistas;
- hashes e versões fixas.

#### Adversariais e stress

- pico de OSs;
- nenhuma equipe factível;
- uma única equipe para várias OSs críticas;
- categoria desconhecida;
- coordenada inválida;
- timestamps inconsistentes;
- feature extrema;
- provedor externo degradado;
- tentativa de manipular prioridade;
- latência no limite.

### 11.2 Gates de CI/CD

Bloquear merge/deploy quando:

- constraint crítica perde cobertura;
- schema incompatível muda sem major version/migração;
- replay diverge sem aprovação;
- baseline degrada além da tolerância;
- qualquer violação hard aparece;
- latência excede orçamento;
- artefatos não possuem versão/hash;
- model card ou manifesto está ausente;
- rollback/fallback não passa no teste.

## 12. Observabilidade

### 12.1 Métricas técnicas

- volume e latência por etapa;
- taxa de erro, timeout e retry;
- cache hit/miss e freshness;
- candidatos gerados/rejeitados;
- status e gap do solver;
- taxa de fallback;
- versão efetiva por decisão;
- perda/atraso/duplicação de eventos;
- paridade de features;
- OOD e missingness.

### 12.2 Métricas operacionais

- SLA por categoria, severidade, região e turno;
- percentis e CVaR de tempo crítico;
- espera máxima e starvation;
- viagem, produtividade e horas extras;
- primeira resolução e reincidência;
- estabilidade/replanejamentos;
- overrides por motivo;
- recomendação versus ação;
- diferenças por segmentos aprovados.

### 12.3 Alertas

Alertas pagináveis:

- qualquer violação de hard constraint;
- regressão crítica de SLA;
- circuit breaker acionado;
- falha generalizada de fallback;
- corrupção/perda de auditoria em modo ativo;
- aumento abrupto de OOD ou missing crítico.

Alertas não pagináveis:

- drift gradual;
- mudança de mix;
- queda de calibração;
- aumento de override;
- divergência de shadow.

Cada alerta deve ter owner, severidade, runbook, limiar, janela e ação automática permitida.

## 13. Segurança, guardrails e rollback

### 13.1 Guardrails de execução

- action masking;
- revalidação pós-optimizer;
- limites máximos de espera;
- exclusão inicial de classes críticas;
- confiança/cobertura mínima;
- timeout total;
- orçamento máximo de replanejamentos;
- freeze de visitas próximas ao atendimento;
- validação de freshness;
- kill switch.

### 13.2 Matriz de fallback

| Falha | Ação |
|---|---|
| Modelo indisponível/timeout | baseline determinístico |
| Feature incompatível/OOD | baseline determinístico + evento |
| Provedor de rota indisponível | cache dentro da validade ou política operacional |
| Constraint não avaliável | fail closed para ação afetada |
| Optimizer timeout | melhor solução factível validada ou baseline |
| Logger crítico indisponível em modo ativo | desativar autonomia |
| Drift/guardrail violado | circuit breaker + política anterior |

### 13.3 Rollback

O rollback deve:

- ocorrer por configuração, sem novo build;
- fixar a política anterior conhecida;
- não apagar eventos;
- preservar rotas em curso conforme regra de estabilidade;
- emitir evento de mudança;
- ter exercício periódico em ambiente de teste/canário;
- possuir RTO definido antes do modo ativo.

### 13.4 Segurança de dados

- pseudonimizar IDs no ambiente analítico;
- limitar precisão geográfica;
- criptografia em trânsito e repouso;
- RBAC e logs de acesso;
- retenção por finalidade;
- revisão de features com potencial de proxy sensível;
- não colocar segredos ou PII em logs/model artifacts;
- threat model para manipulação de categoria, localização e timestamps.

## 14. Explicabilidade

### 14.1 Explicação mínima por decisão

- constraints aplicadas e candidatos bloqueados;
- fatores que mais contribuíram;
- score decomposto e normalizado;
- comparação com alternativa mais próxima;
- ETA e freshness;
- confiança/OOD;
- versões;
- fallback/override e reason code.

### 14.2 Contrato sugerido

```json
{
  "decision_id": "dec_...",
  "summary_code": "HIGH_SLA_RISK_LOW_INSERTION_COST",
  "selected_candidate_id": "cand_...",
  "comparison_candidate_id": "cand_2",
  "top_factors": [
    {"name": "sla_risk", "value": 0.67, "contribution": 0.41},
    {"name": "incremental_minutes", "value": 9, "contribution": 0.12}
  ],
  "blocked_alternatives": [
    {"candidate_id": "cand_3", "reason_code": "MISSING_REQUIRED_SKILL"}
  ],
  "uncertainty": 0.06,
  "versions": {}
}
```

Usar templates de linguagem derivados de códigos; não gerar justificativa livre que possa inventar causalidade. Explicação para operador é curta; auditoria mantém os detalhes.

## 15. Versionamento e reprodutibilidade

### 15.1 Artefatos versionados

- event schema;
- snapshot schema;
- constraint individual e conjunto;
- feature definition e feature set;
- label definition;
- dataset manifest;
- baseline/policy;
- objective e coeficientes;
- optimizer/configuração;
- modelo;
- explicação/template;
- código e ambiente.

### 15.2 Convenções

- SemVer para contratos públicos;
- versão imutável ou content hash para artefatos;
- major: incompatibilidade semântica;
- minor: campo/regra compatível adicionada;
- patch: correção sem mudança intencional de comportamento;
- configuração efetiva registrada em cada decisão;
- nunca referenciar apenas `latest` em produção;
- modelo identifica dataset, código, dependências, seed e métricas.

Identidade reprodutível da decisão:

```text
decision_fingerprint = hash(
  input_snapshot
  + policy_version
  + constraint_set_version
  + feature_set_version
  + objective_version
  + optimizer_version
  + model_version
  + effective_flags
)
```

Replay é considerado reproduzível quando produz o mesmo conjunto factível, componentes e decisão dentro de tolerância numérica declarada.

## 16. Checklist de PR

### Escopo e design

- [ ] O PR declara se altera schema, constraint, feature, objective, modelo ou runtime?
- [ ] Uma única mudança comportamental principal está sendo introduzida?
- [ ] “Fazer agora” versus “adiar” foi respeitado?
- [ ] Há owner e justificativa operacional?

### Constraints e segurança

- [ ] Hard constraint permanece independente de ML?
- [ ] Novas regras têm ID, versão, reason code, fonte e vigência?
- [ ] Fail-open/fail-closed está explícito?
- [ ] Plano é validado antes e depois do optimizer?
- [ ] Fallback, kill switch e rollback foram testados?

### Dados e contratos

- [ ] Schema está versionado e compatível?
- [ ] Migração e consumidores foram avaliados?
- [ ] A feature existia no `decision_time`?
- [ ] Unidades, timezone, missing e freshness estão explícitos?
- [ ] Não há PII/segredo novo em logs ou datasets?
- [ ] Eventos têm idempotência e correlation IDs?

### Objective/modelo

- [ ] Score é decomposto e finito?
- [ ] Normalização e direção estão documentadas?
- [ ] Não há magic number simulando constraint?
- [ ] Peso manual não foi usado como ground truth?
- [ ] Dataset, split temporal, seeds e dependências estão fixados?
- [ ] Baselines e segmentos foram comparados?
- [ ] Calibração, incerteza e OOD foram avaliados?

### Testes

- [ ] Unitários e fronteiras?
- [ ] Invariantes/property-based?
- [ ] Contract tests?
- [ ] Integração com falhas externas?
- [ ] Replay/golden?
- [ ] Stress/adversarial?
- [ ] Latência e orçamento computacional?

### Operação

- [ ] Métricas, logs e tracing suficientes?
- [ ] Alertas/runbooks atualizados?
- [ ] Feature flag com default seguro?
- [ ] Shadow/canário definido?
- [ ] Explicação e reason codes disponíveis?
- [ ] Changelog e versões efetivas registrados?

### Evidência para aprovação

- [ ] Resultados offline com intervalos e segmentação?
- [ ] Zero violações hard?
- [ ] Diferenças do replay explicadas?
- [ ] Plano de rollout e critérios de parada?
- [ ] Aprovação dos owners necessários?

## 17. Milestones e Definition of Done

### M0 — Contrato operacional e inventário

**Fazer**

- definir entidades, granularidade da decisão e horizonte;
- catalogar categorias e regras conhecidas;
- classificar constraints/preferências com owners;
- definir métricas e taxonomia de reason codes;
- aprovar contratos iniciais.

**Definition of Done**

- glossário e decision contract revisados;
- 100% das regras críticas conhecidas possuem owner e classificação;
- ambiguidades estão registradas e bloqueiam somente o escopo afetado;
- schemas v1 passam validação com exemplos;
- critérios de segurança e sucesso foram aprovados.

### M1 — Política atual encapsulada e replay

**Fazer**

- encapsular `manual_v1`;
- criar snapshot, input hash e decision fingerprint;
- montar fixtures anonimizadas;
- implementar replay determinístico.

**Definition of Done**

- baseline reproduz decisões esperadas nas fixtures;
- divergências conhecidas estão explicadas;
- mesma entrada/versões produz mesma saída;
- teste de replay roda em CI;
- política legada continua disponível por flag.

### M2 — Motor de constraints e candidatos

**Fazer**

- implementar APIs do constraint engine;
- gerar candidatos factíveis e reason codes;
- validar plano pós-otimização;
- criar suíte de fronteira e property-based.

**Definition of Done**

- zero candidato conhecido inviável passa;
- cada constraint crítica tem testes positivos, negativos e de fronteira;
- rejeições são auditáveis;
- modelo/score não é dependência do motor;
- comportamento de falha foi aprovado.

### M3 — Telemetria e dataset v1

**Fazer**

- emitir eventos obrigatórios;
- monitorar qualidade do pipeline;
- juntar outcomes;
- gerar dataset manifesto point-in-time correct.

**Definition of Done**

- snapshots, candidatos, recomendação, ação e outcome são correlacionáveis;
- completude/latência atingem SLO definido;
- deduplicação e eventos tardios são testados;
- auditoria reproduz uma decisão amostrada;
- dataset v1 passa testes de leakage e qualidade;
- política de privacidade/retenção está aplicada.

### M4 — Baseline determinístico multiobjetivo

**Fazer**

- implementar `deterministic_v1` e `fixed_multiobjective_v1`;
- versionar normalizações e coeficientes;
- integrar route cost provider;
- produzir score decomposition.

**Definition of Done**

- baselines usam o mesmo contrato;
- nenhum magic number representa hard constraint;
- análise de sensibilidade foi executada;
- latência atende orçamento;
- backtest segmentado está disponível;
- fallback foi exercitado end-to-end.

### M5 — Operação em shadow

**Fazer**

- habilitar `SHADOW`;
- comparar política atual e baselines;
- criar dashboards, alertas e explicações;
- medir divergência recomendação–ação.

**Definition of Done**

- shadow não influencia execução;
- toda recomendação tem versões e explicação;
- taxa de fallback/OOD/missing é visível;
- alertas e runbooks foram exercitados;
- período representativo foi observado sem violação hard.

### M6 — Modelos preditivos v1 em shadow

**Fazer**

- treinar um componente preditivo por vez;
- calibrar e registrar modelo;
- integrar ao objective em shadow;
- comparar com baselines.

**Definition of Done**

- manifesto, model card e lineage completos;
- split temporal e testes de leakage aprovados;
- melhora offline robusta ou aprendizado documentado;
- calibração e segmentos dentro dos gates;
- paridade treino-serving comprovada;
- timeout/OOD acionam fallback;
- nenhuma decisão ativa usa o modelo.

### M7 — Assistência ao operador

**Fazer**

- expor recomendação e explicação;
- captar aceite/override com motivo;
- limitar escopo por flags;
- validar ergonomia.

**Definition of Done**

- humano mantém controle;
- motivos de override têm cobertura aceitável;
- nenhuma regressão de segurança/SLA;
- feedback do operador foi analisado sem tratá-lo automaticamente como verdade;
- rollback por flag foi demonstrado.

### M8 — Canário ativo

**Fazer**

- definir experimento e stopping rules;
- excluir classes críticas inicialmente;
- automatizar escopo pequeno;
- monitorar em tempo real.

**Definition of Done**

- aprovação formal de Segurança/Operações;
- zero violação hard;
- não inferioridade nos guardrails;
- ganho operacional relevante no escopo;
- circuit breaker e rollback testados;
- decisão de expandir ou reverter documentada.

### M9 — Expansão controlada

**Fazer**

- ampliar uma dimensão por vez: região, turno ou categoria;
- manter champion–challenger;
- monitorar drift e caudas;
- revisar objective conforme evidência.

**Definition of Done**

- benefício persiste em múltiplos segmentos/períodos;
- SLOs e guardrails permanecem atendidos;
- ownership operacional está estabelecido;
- runbooks e rotação de suporte estão ativos;
- auditoria e retreinamento controlado funcionam.

### M10 — Pesquisa avançada, somente se justificada

**Adiar até este gate**

- LTR com sinal de desfecho confiável;
- contextual bandit com propensities e safe exploration;
- rolling horizon avançado;
- RL com simulador validado.

**Definition of Done para iniciar pesquisa**

- baselines e modelos atuais atingiram limite mensurável;
- pergunta sequencial/causal está claramente definida;
- candidate logging e suporte comum são suficientes;
- simulador/OPE foram validados onde aplicável;
- risk budget e constraints externos estão aprovados;
- benefício esperado justifica a complexidade.

## 18. Primeira sequência de trabalho recomendada

Sem depender do código-fonte, produzir primeiro:

1. schemas v1 de `DecisionSnapshot`, `ConstraintEvaluation`, `Candidate`, `ScoreResult`, `PlanDecision` e eventos;
2. catálogo inicial de constraints e reason codes;
3. especificação do baseline manual e determinístico;
4. definição dos componentes do objective, unidades e normalizações;
5. matriz de feature flags/modos/fallback;
6. plano de telemetria e manifesto de dataset;
7. fixtures de cenários normais, críticos e degradados;
8. checklist de observabilidade, segurança e rollout.

Após a leitura do código, fazer um gap analysis entre essa arquitetura e o sistema existente. Adaptar nomes e componentes, mas preservar as fronteiras obrigatórias: constraints independentes, scoring explicável, decisão reproduzível, telemetria point-in-time, fallback e promoção gradual.
