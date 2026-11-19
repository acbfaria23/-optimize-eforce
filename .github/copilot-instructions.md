# Copilot Instructions — MsGodOtimizador

Antes de responder qualquer prompt neste repositório, siga obrigatoriamente os guidelines definidos em [`GUIDELINES.md`](../GUIDELINES.md). Visão geral da arquitetura, diagramas do pipeline e do fluxo de reotimização estão no [`README.md`](../README.md).

## Build e Testes

```powershell
dotnet restore Energisa.Eforce.Otimizador.sln
dotnet build Energisa.Eforce.Otimizador.sln
dotnet test Energisa.Eforce.Otimizador.Tests
```

- O restore depende de um feed NuGet privado (pacotes `Energisa.God.*` e `Energisa.Message.Kafka` do framework LibGod). Sem acesso/autenticação a esse feed, o restore falha.
- Não há `NuGet.config` no repositório — a fonte do feed privado vem de configuração global da máquina/CI.

## Resumo das regras mais importantes

### Arquitetura
- A solução tem 4 projetos: `Energisa.Eforce.Otimizador` (core), `.Api`, `.Cronjob` e `.Tests`.
- **Toda lógica de negócio fica em `Energisa.Eforce.Otimizador`**. Nunca em `.Api` ou `.Cronjob`.
- Novos passos do pipeline entram em `OtimizacaoPipelineService` — nunca em Controllers ou Workers.
- Fluxo completo (carga de dados → conversão E-force→GMPRO → construção da request → chamada GMPRO → pós-processamento) está diagramado no README ([Pipeline de Otimização](../README.md#pipeline-de-otimização), [Fluxo de Reotimização](../README.md#fluxo-de-reotimização)).

### Injeção de Dependência
- `AddSingleton` → serviços stateless. `AddScoped` → serviços com contexto por requisição.
- Nunca injete `Scoped` em `Singleton` diretamente — use `IServiceScopeFactory`.
- Configurações sempre via `IOptions<T>` com `SectionName` definido como `const string`.

### Código
- **Idioma:** nomes de classes, métodos, propriedades e variáveis em **Português (PT-BR)**.
- XML doc (`<summary>`) obrigatório em todo membro público.
- `async/await` com `CancellationToken` propagado até o último nível de I/O.
- Coleções inicializadas com `[]` (C# 12). Preferir `is null` / `is not null`.
- Nunca usar `.Result` ou `.Wait()`.

#### Logging Estruturado (Obrigatório)

**Premissa:** Toda aplicação deve deixar registrado seu fluxo de execução em ferramentas de logs centralizadas (Application Insights, Stack Driver, etc.) para rastreabilidade, debugging e monitoramento operacional.

- **Padrão obrigatório:** `_logger.LogInformation("Texto {Param}.", valor)` — Nunca interpolação com `$""`
- **Cada método público** deve logar seu início e fim (com duração quando relevante)
- **Pontos críticos do pipeline** (carregamento de dados, conversão, otimização, pós-processamento): logar situação + métricas chave
- **Erros e warnings:** sempre registre contexto suficiente para auditoria:
  - `_logger.LogWarning("Situação {Motivo}. Detalhe: {Detalhe}.", motivo, detalhe)` — para condições esperadas mas indesejáveis
  - `_logger.LogError(ex, "Erro ao processar {Recurso}. Empresa: {Empresa}.", recurso, empresa)` — sempre inclua a exceção
- **Variáveis de contexto** (Empresa, IdEquipe, NumeroOs, etc.) devem estar em TODOS os logs do fluxo
- **Não logar dados sensíveis:** credenciais, chaves de API, dados PII completos

**Exemplos:**
```csharp
// Correto: estruturado, sem interpolação
_logger.LogInformation(
    "Iniciando otimização. Empresa: {Empresa}. Os: {Os}. Equipes: {Equipes}. DuracaoMs: {DuracaoMs}.",
    empresa, osCount, equipesCount, stopwatch.ElapsedMilliseconds);

// Errado: interpolação
_logger.LogInformation($"Iniciando otimização. Empresa: {empresa}...");

// Correto: warning com contexto
_logger.LogWarning(
    "Nenhum cenário encontrado para otimização. Empresa: {Empresa}. Motivo: {Motivo}.",
    empresa, motivo);

// Correto: erro com exceção
_logger.LogError(ex, "Falha na carga de dados. Empresa: {Empresa}. Origem: {Origem}.", empresa, origem);
```

### Testes
- Nomenclatura: `{Metodo}_{Cenario}_{ResultadoEsperado}`.
- Sempre usar `TestDataFactory` para criar objetos de teste.
- Todo novo serviço precisa cobrir: caminho feliz, entrada vazia/nula e um edge case de domínio.

### Modelos
- DTOs imutáveis → `record`. Modelos que sofrem mutação → `class`.
- Modelos de integração (E-force, GMPRO) ficam em `Models/Integracao/` — um arquivo por entidade.
- Helpers são classes estáticas e puras — sem efeitos colaterais e sem dependências injetadas.

### Segurança
- Nunca commitar credenciais, connection strings ou chaves de API.
- Segredos de desenvolvimento via `dotnet user-secrets`.

### PR / Qualidade
- Sem `#if DEBUG`, `Console.WriteLine` ou código comentado no diff final.
- `ConversaoConstantes`: todo novo parâmetro deve ser adicionado ao método `Validar()`.
- `TestDataFactory` deve ser atualizada quando novos modelos forem adicionados.
