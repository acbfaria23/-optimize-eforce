# Plano técnico para priorização adaptativa de OSs

## 1. Objetivo e princípios

Evoluir o middleware que hoje atribui pesos manuais por categoria — por exemplo, risco à vida = 100.000 e corte de luz = 20 — para um sistema que aprenda, avalie e adapte a priorização de ordens de serviço (OSs) ao longo do tempo, antes do envio à API do Google.

O sistema deve:

- preservar segurança, obrigações regulatórias, capacidade operacional e regras contratuais;
- otimizar simultaneamente urgência, cumprimento de SLA, tempo de atendimento, deslocamento e estabilidade operacional;
- produzir decisões auditáveis e explicáveis;
- aprender com desfechos reais, não apenas imitar as prioridades atuais;
- permitir evolução controlada por experimentos, com rollback imediato.

Os pesos atuais são uma política histórica e uma fonte útil de contexto, mas **não devem ser tratados como verdade absoluta nem como rótulos finais de treinamento**. Eles podem conter heurísticas necessárias, vieses, escalas arbitrárias e decisões que nunca foram comparadas com alternativas. Durante a transição, servem como baseline, fallback e, no máximo, sinal auxiliar com procedência explícita.

## 2. Formulação do problema

### 2.1 Entidades e decisão

- **Visitas:** OSs a executar.
- **Visitantes:** equipes, com habilidades, localização, turno, capacidade, ferramentas e restrições próprias.
- **Estado no instante de decisão:** conjunto de OSs elegíveis, equipes disponíveis, rotas em curso, tempos estimados de viagem e serviço, clima, região, SLAs e contexto operacional.
- **Ação:** escolher quais OSs entram no horizonte de planejamento, atribuir OSs a equipes e definir sua ordem.
- **Desfecho:** tempos reais, cumprimento de SLA, risco mitigado, sucesso na primeira visita, replanejamentos, distância, horas extras, cancelamentos e incidentes.

O problema completo combina:

1. seleção/priorização de OSs;
2. atribuição equipe–OS;
3. sequenciamento e roteamento;
4. reotimização quando o estado muda.

É uma variante dinâmica de vehicle routing/scheduling com janelas de tempo, habilidades e objetivos múltiplos. A priorização não deve ser modelada isoladamente do custo de inserção na rota: uma OS urgente, porém inviável para determinada equipe, não pode receber uma recomendação executável para ela.

### 2.2 Arquitetura conceitual

Fluxo recomendado:

1. validar e enriquecer OSs e equipes;
2. aplicar restrições duras e gerar somente pares/planos factíveis;
3. estimar atributos de rota pela API do Google, quando necessários, sem delegar a ela a semântica de urgência;
4. pontuar preferências aprendíveis e custos;
5. resolver o problema de atribuição/sequenciamento sob restrições;
6. aplicar guardrails e validar o plano;
7. registrar decisão, alternativas consideradas e desfechos.

Deve-se confirmar no código se a API do Google apenas calcula matriz/rota, se também ordena waypoints, quais limites possui e em que ponto exato o peso manual altera o conjunto ou a ordem enviados.

## 3. Restrições duras versus preferências aprendíveis

### 3.1 Restrições duras

São condições cuja violação torna uma decisão inválida, independentemente de ganho médio:

- equipe sem habilitação, ferramenta, composição ou credencial exigida;
- OS fora da área atendida ou equipe indisponível;
- incompatibilidade entre tipo de serviço e equipe;
- descanso, jornada, regras trabalhistas e limites físicos;
- bloqueios de segurança e procedimentos obrigatórios;
- janelas legais/contratuais que não admitem violação;
- precedências técnicas entre serviços;
- OSs críticas que, por política formal, exigem atendimento imediato ou limite máximo garantido;
- capacidades e limites aceitos pela API e pelo otimizador.

Essas regras devem ser codificadas como filtros, constraints do solver ou validações pós-solução, e não como números muito grandes na fitness function. “Peso 100.000” não prova que uma regra seja dura: uma soma de outros termos, mudança de escala ou bug numérico pode superá-lo.

### 3.2 Preferências aprendíveis

São trade-offs nos quais existe margem operacional:

- risco estimado de atraso de SLA;
- benefício esperado de antecipar uma OS;
- distância e tempo incremental;
- probabilidade de resolução na primeira visita;
- estabilidade da rota versus vantagem de replanejar;
- equilíbrio de carga e horas extras;
- agrupamento geográfico;
- custo de deixar uma OS para o próximo horizonte;
- preferência por equipe com melhor proficiência, além do mínimo obrigatório.

### 3.3 Por que separar

A separação:

- impede que o aprendizado “compre” uma violação de segurança;
- torna o modelo e a auditoria mais claros;
- evita escalas frágeis e constantes arbitrariamente grandes;
- reduz o espaço de ações e melhora o aprendizado;
- permite atualizar preferências sem revalidar regras críticas;
- facilita fallback: restrições continuam válidas mesmo sem modelo.

Cada regra deve ter proprietário, fonte normativa, severidade, vigência, versão e testes. Casos ambíguos devem ser resolvidos com Segurança/Operações antes de serem classificados.

## 4. Dados e eventos de produção

### 4.1 Eventos mínimos

Registrar eventos versionados e correlacionados por `decision_id`, `os_id`, `team_id`, `route_id`, `model_version` e horário:

- **OS criada/alterada/cancelada:** categoria original, descrições estruturadas, localização aproximada, severidade, clientes/ativos afetados, SLA, janela, dependências e origem;
- **estado de equipe:** posição, turno, disponibilidade, competências, composição, ferramentas, carga corrente e motivo de indisponibilidade;
- **snapshot de decisão:** todas as features disponíveis naquele momento, política/modelo, constraints ativas e qualidade/frescor dos dados;
- **candidate set:** pares equipe–OS e sequências consideradas, alternativas removidas e motivo de inviabilidade;
- **recomendação:** scores decompostos, rota proposta, posição, ETA, incerteza e explicação;
- **decisão executada:** aceitação, override humano, motivo padronizado e texto opcional; distinguir recomendação de ação realmente tomada;
- **execução:** despacho, deslocamento, chegada, início, pausas, conclusão, retorno, reatribuição e replanejamento;
- **resultado:** SLA, duração real, resolução na primeira visita, reincidência, cliente/ativo normalizado, incidentes de segurança e qualidade;
- **estimativas externas:** duração/distância retornadas pela API, versão/configuração, falhas, timeout e fallback;
- **contexto:** trânsito, clima, evento de massa, região, dia/turno e condições excepcionais;
- **feedback tardio:** reclamação, reabertura, auditoria ou consequência observada após a conclusão.

### 4.2 Requisitos de qualidade

- timestamps consistentes e indicação de relógio/fonte;
- distinção entre dado conhecido na decisão e dado revelado depois, evitando leakage;
- categorias e motivos controlados, com versão de taxonomia;
- unidades explícitas;
- deduplicação e tratamento de eventos fora de ordem;
- completude e latência monitoradas;
- proteção de dados pessoais, retenção definida e acesso mínimo;
- trilha imutável suficiente para reproduzir uma decisão.

### 4.3 Dados necessários para aprendizado causal

Logs da política atual mostram apenas o resultado da ação escolhida; o desfecho das alternativas é contrafactual e não é observado. Para avaliação confiável:

- registrar o conjunto de candidatos e a propensão/probabilidade de escolha quando houver exploração;
- introduzir exploração apenas entre ações seguras e quase equivalentes;
- registrar overrides e razões sem presumir que todo override está correto;
- considerar experimentos randomizados estratificados ou políticas de shadow para reduzir viés de seleção;
- medir atraso de feedback e censura, especialmente para reincidência e risco.

## 5. Baselines

Manter baselines reproduzíveis:

1. política atual de pesos manuais;
2. regras ordenadas por criticidade formal, SLA restante e idade, sem ML;
3. heurística de custo de inserção: criticidade/SLA com penalidade de viagem;
4. solver multiobjetivo com coeficientes fixos definidos por Operações;
5. oracle retrospectivo limitado, usando desfechos conhecidos apenas como teto analítico, nunca como comparação online justa.

Antes de ML, calibrar e medir o baseline determinístico. Uma heurística bem estruturada pode capturar grande parte do ganho e cria uma referência interpretável. Comparar também por região, categoria, turno e nível de criticidade, não somente pela média global.

## 6. Abordagens candidatas

### 6.1 Learning-to-rank (LTR)

Uso inicial recomendado para ordenar OSs ou inserções factíveis em um snapshot. Features podem incluir SLA restante, idade, severidade estruturada, impacto, custo incremental, compatibilidade, probabilidade de sucesso e contexto.

- **Vantagens:** simples de colocar em shadow, boa explicabilidade relativa, adequado a feedback de preferência e desfechos observados.
- **Limitações:** rótulos históricos sofrem viés da política; ranking local não captura perfeitamente efeitos futuros e interação entre rotas.
- **Treinamento preferível:** objetivos derivados de desfechos e comparações contrafactuais seguras; não usar o peso manual como target. Pesos atuais podem entrar apenas como feature marcada ou sinal de professor fraco em uma análise de ablação.

Pode-se combinar previsão de componentes (risco de violar SLA, duração, primeira resolução) com ranking final e solver.

### 6.2 Modelos preditivos + otimização

Treinar modelos calibrados para prever componentes de resultado e usar suas estimativas em um solver com objective explícito.

- **Vantagens:** separa previsão de decisão; facilita auditoria, calibração e mudança de política sem retreinar tudo.
- **Limitações:** erros correlacionados podem se acumular; o solver explora regiões onde o modelo tem pouca cobertura.

Esta é a proposta preferencial para a primeira versão aprendível, possivelmente complementada por LTR.

### 6.3 Contextual bandit

Considerar após telemetria madura, para escolher entre ações seguras em cada contexto e aprender com recompensa observada.

- adequado quando a decisão é relativamente local e o efeito futuro é curto;
- requer logging de propensão, suporte comum entre políticas e exploração controlada;
- usar action masking para restrições e exploração conservadora;
- aplicar off-policy evaluation antes de exposição.

Não é indicado no início se não houver candidatos registrados, recompensa confiável e capacidade de randomização segura.

### 6.4 Reinforcement learning

RL pode ser relevante quando replanejamentos sequenciais e consequências de longo prazo dominarem o problema, por exemplo reservar capacidade para demanda futura.

Não é primeira escolha: há estado/ação combinatórios, recompensa atrasada, risco de exploração e dificuldade de validar um simulador. Só avançar se:

- baselines supervisionados/otimizadores atingirem um limite mensurável;
- houver simulador validado contra produção;
- constraints forem impostas fora da política;
- avaliação off-policy e rollout conservador forem viáveis.

Alternativas menos arriscadas incluem model predictive control, previsão de demanda futura e otimização rolling-horizon.

## 7. Proposta de objective/fitness multiobjetivo

### 7.1 Ordem de otimização

Usar otimização lexicográfica ou hierárquica, não uma única soma irrestrita:

1. **Factibilidade e segurança:** nenhuma violação dura.
2. **Proteção crítica:** minimizar risco esperado e cauda de atraso das classes protegidas.
3. **Serviço:** minimizar violações e risco de SLA das demais OSs.
4. **Eficiência e qualidade:** reduzir deslocamento, horas extras e reincidência.
5. **Estabilidade e equidade operacional:** limitar replanejamento e concentrações indevidas.

Em cada nível, só melhorar níveis inferiores dentro de uma tolerância formal do superior. Isso evita que grande economia de quilômetros compense uma piora crítica.

### 7.2 Objective concreto

Para um plano factível \(P\), em um horizonte \(H\):

\[
J(P) =
\lambda_1 C_{\text{risco}} +
\lambda_2 C_{\text{SLA}} +
\lambda_3 C_{\text{espera}} +
\lambda_4 C_{\text{viagem}} +
\lambda_5 C_{\text{qualidade}} +
\lambda_6 C_{\text{hora-extra}} +
\lambda_7 C_{\text{instabilidade}} +
\lambda_8 C_{\text{equidade}}
\]

Minimizar \(J\), sujeito às restrições duras. Componentes sugeridos:

- \(C_{\text{risco}} = \sum_i p_i(\text{dano}\mid atraso_i)\times impacto_i\), com tratamento de cauda (por exemplo CVaR) para eventos raros e severos;
- \(C_{\text{SLA}} = \sum_i w_i \,[p_i(\text{violação}) + \alpha \cdot atrasoNormalizado_i]\), com penalidade convexa após o limite;
- \(C_{\text{espera}} = \sum_i w_i \cdot esperaNormalizada_i\), evitando starvation por termo crescente com a idade;
- \(C_{\text{viagem}}\): minutos ou quilômetros incrementais normalizados;
- \(C_{\text{qualidade}} = \sum_i p_i(\text{reincidência ou falha na primeira visita})\);
- \(C_{\text{hora-extra}}\): excesso esperado sobre jornada, com função convexa;
- \(C_{\text{instabilidade}}\): OSs deslocadas/reordenadas após confirmação, ponderadas pela proximidade do atendimento;
- \(C_{\text{equidade}}\): dispersão de espera/SLA entre regiões ou grupos operacionalmente legítimos, sem utilizar atributo protegido de forma indevida.

Os \(w_i\) devem refletir impacto, SLA e política formal em unidades documentadas; os \(\lambda\) representam trade-offs de negócio aprovados. Os modelos aprendem probabilidades e benefícios marginais; governança define limites e trade-offs aceitáveis. Para classes críticas, preferir constraints como “percentil 99 do tempo de resposta ≤ limite” em vez de depender apenas de \(\lambda\).

### 7.3 Aprendizado do benefício marginal

Uma alternativa robusta é estimar:

\[
\Delta U(i,t) = E[\text{custo se atender depois}] - E[\text{custo se atender agora}]
\]

e combinar \(\Delta U\) com o custo de inserção e as constraints. Isso fornece significado operacional ao score e reduz a dependência de pesos opacos.

## 8. Normalização e escalas

- converter componentes para unidades comparáveis, preferencialmente custo esperado por OS ou por horizonte;
- normalizar por limites operacionais estáveis, como SLA contratual ou orçamento de jornada, e não apenas por média/desvio da amostra;
- aplicar transformações robustas e clipping documentado para outliers;
- usar penalidades convexas para atrasos e horas extras, preservando sensibilidade perto dos limites;
- calibrar probabilidades por segmento e monitorar reliability curves;
- evitar constantes gigantes; constraints críticas ficam fora da soma;
- registrar a versão de cada transformação e fazer score decomposition;
- validar invariância a tamanho do lote, quantidade de equipes, região e duração do horizonte;
- executar análise de sensibilidade dos \(\lambda\), fronteira de Pareto e stress tests antes de definir valores.

A governança deve aprovar faixas permitidas dos coeficientes. Alterações de escala não podem mudar silenciosamente a prioridade sem revisão.

## 9. Avaliação offline

### 9.1 Separação dos dados

- split temporal, treinando no passado e avaliando em períodos futuros;
- holdouts por eventos excepcionais, regiões ou categorias quando relevante;
- impedir que dados posteriores à decisão entrem nas features;
- avaliar atraso de rótulo e exemplos censurados;
- repetir métricas por segmentos e por severidade.

### 9.2 Modos de avaliação

1. **Replay exato:** reproduzir snapshots e verificar factibilidade, latência e determinismo.
2. **Shadow counterfactual:** gerar decisões sem executá-las e comparar características, com a ressalva de que o desfecho alternativo não é observado.
3. **Simulação:** usar durações/demanda estocásticas; validar o simulador comparando distribuições, transições e resposta a intervenções com produção.
4. **Off-policy evaluation:** IPS/SNIPS e doubly robust somente quando propensities e suporte forem adequados; reportar intervalos de confiança e effective sample size.
5. **Backtesting operacional:** comparar com baselines sob as mesmas informações disponíveis e os mesmos limites computacionais.

### 9.3 Métricas

- violações duras: zero;
- tempo de resposta e taxa de SLA por percentis, categoria e severidade;
- CVaR/percentis de atraso em OSs críticas;
- tempo médio e máximo de espera/starvation;
- distância, tempo em trânsito, produtividade e horas extras;
- primeira resolução, reincidência e cancelamentos;
- replanejamentos e estabilidade;
- overrides, aceitação e motivos;
- cobertura, calibração, incerteza e dados fora de distribuição;
- latência, timeout e taxa de fallback;
- diferenças entre regiões/segmentos e respectivos intervalos de confiança.

Testes adversariais devem cobrir picos de demanda, trânsito extremo, falta de equipes, dados ausentes, categorias novas, coordenadas inválidas e indisponibilidade da API.

## 10. Avaliação online

Sequência recomendada:

1. shadow sem impacto;
2. comparação assistida para operadores;
3. canário em região/turno de baixo risco;
4. experimento controlado estratificado por região, demanda e severidade;
5. expansão progressiva.

Definir antes do teste:

- hipótese e métrica primária;
- métricas de segurança não inferiores e margens máximas;
- duração baseada em volume e atraso dos desfechos, não em uma janela arbitrária;
- unidade de randomização que evite contaminação entre rotas;
- regras de parada automática;
- análise de novelty e efeitos de aprendizado dos operadores.

Não explorar classes críticas. Em ações seguras, usar exploração pequena, registrada e limitada por regret/risk budget. A decisão de promoção deve considerar caudas e segmentos, não só ganho médio.

## 11. Segurança, guardrails e fallback

- motor de constraints independente do modelo;
- allowlist de ações e action masking;
- limites máximos de espera por classe e gatilhos de escalonamento;
- proteção contra starvation;
- validação do plano antes da execução;
- fallback determinístico para modelo indisponível, baixa confiança, dados ruins ou timeout;
- circuit breaker baseado em métricas de segurança, SLA, latência e drift;
- rollback de configuração/modelo em um passo;
- versões imutáveis de modelo, features, constraints e objective;
- aprovação humana para exceções e mudanças críticas;
- monitoramento em tempo real e auditoria pós-incidente;
- testes de abuso, dados manipulados e degradação da API externa;
- princípio de menor privilégio e minimização de PII.

Modelos não devem inferir menor prioridade a partir de proxies socialmente sensíveis sem justificativa legal e teste de impacto. Restrições e métricas de equidade devem ser definidas com Jurídico, Segurança e Operações.

## 12. Explicabilidade

Para cada recomendação, apresentar explicação operacional, não apenas score:

- regras duras aplicadas e alternativas eliminadas;
- principais fatores: “SLA restante”, “risco de atraso”, “impacto”, “custo de inserção”, “habilidade da equipe”;
- decomposição normalizada do objective;
- comparação com a próxima alternativa;
- ETA e incerteza;
- versão do modelo e frescor dos dados;
- motivo do fallback, quando houver.

Exemplo: “OS priorizada porque o risco calibrado de violar o SLA nas próximas 45 min subiu de 18% para 67%; a equipe E3 é habilitada e adiciona 9 min à rota. A alternativa B adicionaria 4 min, mas tem 6% de risco de SLA.”

Disponibilizar visão simplificada ao operador e trilha detalhada para auditoria. Explicações não devem expor dados pessoais nem sugerir causalidade quando o modelo só indica associação.

## 13. Drift, monitoramento e retreinamento

Monitorar:

- drift de features e categorias;
- mudança na disponibilidade de equipes, demanda, geografia e tempos de serviço;
- calibração e erro dos modelos por segmento;
- mudança na relação feature–resultado;
- degradação do objective e das métricas de guardrail;
- taxa de OOD, dados ausentes, fallback e override;
- diferença entre política recomendada e executada.

Retreinamento deve ser acionado por evidência e também avaliado em cadência regular, sem promover automaticamente um modelo apenas por ser recente. Pipeline:

1. validar esquema e qualidade;
2. criar dataset temporal reproduzível;
3. treinar candidato;
4. comparar com o champion e baselines;
5. executar testes de segurança, calibração, segmentos e stress;
6. aprovar;
7. shadow/canário;
8. promover ou reverter.

Usar model registry, lineage e champion–challenger. Mudanças de taxonomia, SLA ou processo operacional exigem análise de conceito, não apenas retreino.

## 14. Rollout em fases

### Fase 0 — Descoberta e contrato operacional

- mapear código, fluxo da API, regras, owners e SLAs;
- classificar regras em duras e aprendíveis;
- definir eventos, taxonomia, privacidade e métricas;
- medir a política atual e identificar gaps de dados.

**Saída:** especificação de decisão, catálogo de constraints e baseline reproduzível.

### Fase 1 — Observabilidade e baseline

- instrumentar snapshots, candidatos, decisões, overrides e resultados;
- implementar replay e dashboards;
- estruturar heurística/solver determinístico com objective explícito;
- manter comportamento atual como fallback.

**Gate:** dados completos e replay consistente por período representativo.

### Fase 2 — Modelos preditivos em shadow

- prever duração, risco de SLA, primeira resolução e benefício marginal;
- integrar estimativas ao solver sem executar recomendações;
- calibrar, segmentar e comparar com baselines.

**Gate:** zero violação dura, melhora offline robusta e latência dentro do orçamento.

### Fase 3 — Assistência ao operador

- mostrar ranking, explicação e incerteza;
- registrar aceitação/override e motivo;
- validar ergonomia e efeitos operacionais.

**Gate:** guardrails aprovados e ausência de regressões críticas.

### Fase 4 — Canário controlado

- execução automática em escopo restrito e reversível;
- excluir classes de maior risco inicialmente;
- experimento estratificado com parada automática.

**Gate:** não inferioridade em segurança/SLA e benefício estatística e operacionalmente relevante.

### Fase 5 — Expansão e aprendizado

- ampliar regiões, turnos e categorias gradualmente;
- introduzir bandit conservador somente onde houver suporte e propensities;
- considerar previsão de demanda/rolling horizon.

### Fase 6 — Sequencial avançado

- avaliar RL apenas com simulador validado e vantagem clara sobre abordagens mais simples;
- manter constraints e safety layer externos à política.

## 15. Perguntas para leitura posterior do código

### Fluxo e integração

- Onde os pesos são definidos, versionados e aplicados?
- Eles filtram OSs, alteram a ordem, viram parâmetro da API do Google ou entram em um solver próprio?
- A API retorna matriz, sequência otimizada ou ambos?
- Qual é o horizonte de planejamento e com que frequência ocorre reotimização?
- O sistema reordena rotas já aceitas? Como trata compromissos em curso?
- Quais são timeout, retries, quotas e fallback da integração externa?

### Regras e semântica

- Quais categorias existem e quem é proprietário de cada regra?
- “Risco à vida” é constraint formal ou preferência extrema? Qual é o limite de resposta?
- Há precedências, janelas, habilidades, zonas, capacidade, jornada e regras contratuais?
- Como empates, OSs sem categoria e categorias múltiplas são tratados?
- Existem regras divergentes por região ou cliente?

### Dados

- Quais timestamps representam criação, despacho, aceite, chegada, início e conclusão?
- São registrados snapshots e candidatos, ou apenas o plano vencedor?
- A posição das equipes e estimativas de viagem são historizadas?
- Existem motivos estruturados para override, reatribuição e cancelamento?
- Como reincidência e primeira resolução são identificadas?
- Há IDs estáveis para ligar OS, equipe, rota, decisão e desfecho?

### Arquitetura e operação

- Há feature store, event bus, solver, model serving e model registry?
- Qual é o orçamento de latência e volume de decisões?
- O fluxo é síncrono, assíncrono ou híbrido?
- Como configurações e regras são promovidas e revertidas?
- Quais métricas, alertas, testes e trilhas de auditoria existem?
- Quem pode fazer override e como o motivo é capturado?

### Exemplos de produção a solicitar

- snapshots anonimizados de picos e operação normal;
- rota antes/depois de replanejamento;
- casos em que o peso 100.000 dominou corretamente e incorretamente;
- empates entre OSs de mesma categoria com SLAs distintos;
- OS crítica distante versus várias OSs próximas;
- indisponibilidade da API, dados ausentes e coordenadas inválidas;
- override humano bem-sucedido e malsucedido;
- incidentes, violações de SLA e starvation;
- OS reaberta ou não resolvida na primeira visita;
- mudanças recentes de categoria, SLA ou estrutura de equipes.

## 16. Riscos e mitigação

| Risco | Mitigação |
|---|---|
| Imitar vieses dos pesos históricos | Treinar com desfechos, comparar políticas e tratar peso atual apenas como baseline/sinal auxiliar |
| Confundir correlação com benefício de priorizar | Exploração segura, logging de propensão, OPE e experimentos controlados |
| Violação crítica por trade-off numérico | Constraints duras e otimização hierárquica |
| Otimizar média e piorar cauda | Percentis, CVaR, limites por classe e métricas segmentadas |
| Reward hacking | Métricas múltiplas, auditoria de efeitos não intencionais e guardrails externos |
| Dados incompletos ou leakage | Event contract, validação temporal, lineage e testes de qualidade |
| Baixo suporte para ações alternativas | Restringir OPE, explorar gradualmente e reportar incerteza |
| Drift operacional | Monitoramento, champion–challenger e rollback |
| Instabilidade por reotimização | Penalidade e limites de mudança, congelamento próximo ao atendimento |
| Perda de confiança dos operadores | Explicação, modo assistido e feedback estruturado |
| Dependência da API externa | Cache apropriado, timeout, fallback e testes de degradação |
| Viés geográfico/social | Revisão de features, métricas por segmento e governança jurídica |
| Complexidade excessiva precoce | Começar por baseline explícito e modelos preditivos antes de bandit/RL |

## 17. Critérios de sucesso

Os valores-alvo devem ser definidos após medir o baseline, mas a promoção exige:

### Segurança e conformidade

- zero violação conhecida de restrição dura;
- nenhum aumento material em incidentes ou tempo de resposta de classes protegidas;
- 100% das decisões reproduzíveis e auditáveis dentro da política de retenção;
- fallback e rollback validados em testes e canário.

### Resultado operacional

- redução estatística e operacionalmente relevante de violações de SLA;
- melhora ou não inferioridade nos percentis altos de tempo de resposta crítico;
- redução de deslocamento/horas extras sem degradar qualidade;
- ausência de starvation e regressão relevante em qualquer segmento importante;
- melhora de resolução na primeira visita ou não inferioridade definida.

### Produto e modelo

- latência e disponibilidade dentro do SLO;
- calibração aceitável por segmento;
- explicações disponíveis para a grande maioria das recomendações;
- taxa de override acompanhada por motivo, sem crescimento anormal;
- ganho persistente em múltiplos períodos, regiões e regimes de demanda.

### Decisão de avanço

Uma fase só avança se os guardrails forem atendidos e o ganho superar a complexidade operacional. Se ML não superar de forma robusta o baseline determinístico, manter o baseline e melhorar dados/formulação; não promover o modelo por obrigação.

## 18. Decisão técnica inicial recomendada

1. formalizar constraints duras fora da fitness function;
2. instrumentar snapshots, candidatos, ações, propensities quando aplicável e desfechos;
3. substituir a escala arbitrária por um objective hierárquico e componentes normalizados;
4. estabelecer solver/heurística determinística como baseline;
5. treinar modelos calibrados de duração, SLA, qualidade e benefício marginal, integrados ao solver;
6. avaliar LTR como camada de ordenação em shadow;
7. adiar contextual bandit até existir exploração segura e telemetria contrafactual;
8. adiar RL até haver evidência de valor sequencial e simulador validado.

Essa sequência permite aprender gradualmente sem transformar regras críticas em preferências negociáveis e sem perpetuar os pesos manuais como rótulos de verdade.
