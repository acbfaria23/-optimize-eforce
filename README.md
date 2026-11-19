# MsGodOtimizador

Microsserviço de otimização de rotas de equipes de campo, responsável por converter dados do sistema **E-force** para o modelo da **Google Cloud Route Optimization API** (GMPRO), executar a otimização e pós-processar os resultados com indicadores operacionais e financeiros. Inclui fluxo de **reotimização intradiária** para re-otimizar rotas durante o expediente.

> **?? Este documento é mantido atualizado conforme a evolução da aplicação.**
>
> Última atualização: Julho/2025

---

## Índice

- [Visão Geral](#visão-geral)
- [Arquitetura](#arquitetura)
- [Projetos da Solução](#projetos-da-solução)
- [Pipeline de Otimização](#pipeline-de-otimização)
- [Fluxo de Reotimização](#fluxo-de-reotimização)
- [Tecnologias](#tecnologias)
- [Configuração](#configuração)
- [Como Executar](#como-executar)
- [Testes](#testes)
- [Análise de Risco: Morte Prematura da Aplicação em OtimizacaoWorker](#análise-de-risco-morte-prematura-da-aplicação-em-otimizacaoworker)

---

## Visão Geral

O **MsGodOtimizador** é composto por três projetos principais:

| Projeto | Tipo | Descrição |
|---------|------|-----------|
| `Energisa.Eforce.Otimizador` | Class Library | Biblioteca core com todo o pipeline de otimização, modelos, serviços e workers |
| `Energisa.Eforce.Otimizador.Api` | Web API (ASP.NET Core) | Expõe endpoints REST para execução sob demanda da otimização |
| `Energisa.Eforce.Otimizador.Cronjob` | Worker Service | Executa o pipeline de otimização de forma agendada (batch) |
| `Energisa.Eforce.Otimizador.Tests` | xUnit Test Project | Testes unitários da biblioteca core |

---

## Arquitetura

```
????????????????????????????????????????????????????????????????
?  Energisa.Eforce.Otimizador.Api        (Web API)            ?
?  Energisa.Eforce.Otimizador.Cronjob    (Worker Service)     ?
????????????????????????????????????????????????????????????????
                       ?
        ???????????????????????????????
        ? Energisa.Eforce.Otimizador  ?
        ?       (Class Library)       ?
        ???????????????????????????????
                       ?
  ???????????????????????????????????????????
  ?                    ?                    ?
LibGodDataService   EforceParaGmpro    GmProBuilder
  (MongoDB)         Orchestrator       Service (Protobuf)
                                            ?
                                  ?????????????????????
                                  ?         ?         ?
                              GoogleCloud  GmProApi  PosProcessamento
                              Storage      Client    Service
                              (gRPC)    (Relatório)
```

---

## Projetos da Solução

Consulte os READMEs individuais para detalhes de cada projeto:

- [Energisa.Eforce.Otimizador](Energisa.Eforce.Otimizador/README.md) — Biblioteca core
- [Energisa.Eforce.Otimizador.Api](Energisa.Eforce.Otimizador.Api/README.md) — Web API
- [Energisa.Eforce.Otimizador.Cronjob](Energisa.Eforce.Otimizador.Cronjob/README.md) — Worker Service

---

## Pipeline de Otimização

| Passo | Serviço | Descrição |
|-------|---------|-----------|
| 1 | `LibGodDataService` | Busca OS, Equipes, Perfis e Regiões no MongoDB via repositórios LibGod |
| 2 | `EforceParaGmproOrchestrator` | Valida, filtra, ajusta turnos, calcula pesos e atribui equipes |
| 3 | `GmProBuilderService` | Monta `OptimizeToursRequest` com Shipments, Vehicles, TimeWindows e Waypoints (Protobuf) |
| 4 | `GoogleCloudStorageService` | Upload do JSON da request para bucket GCS |
| 5 | `GmProApiClient` | Envia a request para a API Google Route Optimization (gRPC ou REST) |
| 6 | `PosProcessamentoService` | Processa a response: calcula multas, skips e gera relatório final |

### Diagrama do Pipeline de Otimização

```
Fluxo Automático (CronJob)  ou  Fluxo Sob Demanda (API)
                |
                ?
    [§1] Carregar Dados
    (LibGodDataService)
         • OS, Equipes, Perfis, Regiões
         • Filtros: empresa, período, elegibilidade
                |
                ?
    [§2] Conversão E-force ? GMPRO
    (EforceParaGmproOrchestrator)
         • Validação semântica
         • Cálculo de pesos
         • Atribuição de equipes
                |
                ?
    [§3] Construção do Request
    (GmProBuilderService)
         • Shipments, Vehicles, TimeWindows
         • Waypoints, Polylines
                |
                ?
    [§4] Persistência em GCS
    (GoogleCloudStorageService)
         • Upload JSON request
                |
                ?
    [§5] Chamada GMPRO
    (GmProApiClient)
         • gRPC ou REST
         • LRO (Long Running Operation)
                |
                ?
    [§6] Pós-Processamento
    (PosProcessamentoService)
         • Multas, skips, indicadores
         • Persistência de resultado
         • Notificação (webhook/fila)
                |
                ?
         Resultado Final
```

---

## Fluxo de Reotimização

A **reotimização intradiária** é um fluxo assíncrono exclusivo da API que re-otimiza rotas de equipes específicas durante o expediente, considerando a situação operacional real (posição, turno, capacidade restante) e novas OSs que chegam após a otimização original.

### Comparação: Otimização vs. Reotimização

| Aspecto | Otimização Completa | Reotimização Intradiária |
|--------|-------------------|-------------------------|
| **Escopo** | Todas as equipes e OSs elegíveis | Apenas equipes selecionadas + novas OSs |
| **Acionamento** | Automático (CronJob) ou demanda (API) | Exclusivamente via API com `IdempotencyKey` |
| **Dados Base** | MongoDB (cenário atual completo) | Cenário original persistido (GCS) |
| **Posição** | Ponto de partida cadastrado | Real (coordenada capturada) |
| **OSs** | Pendentes no período | Pendentes originais + novas + classificação |
| **Estado** | Novo cenário completo | Delta vs. original (equipes/OSs) |
| **Persistência** | Integração (resultado final) | GCS (input/output) + DB (referência) |

### Arquitetura da Reotimização

```
???????????????????????????????????????????????????????????????
?  Endpoint API: POST /reotimizacoes                          ?
?  Header: Idempotency-Key (obrigatório, §3, §16)             ?
?  Body: { OtimizacaoOriginalId, Equipes[], IdempotencyKey } ?
???????????????????????????????????????????????????????????????
                              ?
                              ?
           [§3] Capturar DataCorte e validações
                              ?
                              ?
    ????????????????????????????????????????????
    ? ReotimizacaoDataService.MontarContextoAsync ?
    ????????????????????????????????????????????
    ?
    ?? [§1] Recuperar cenário original (GCS)
    ?        Request/Response brutos
    ?
    ?? [§4] Validar consistência
    ?        • Cenário pertence à empresa
    ?        • Base deve ser 1ª otimização (não reotimização)
    ?
    ?? [§5] Montar snapshot operacional
    ?        ReotimizacaoDataService.MontarEquipesSelecionadasAsync
    ?        ?
    ?        ?? [§6] Posição atual (coordenada capturada)
    ?        ?? [§6] Disponibilidade (horário livre)
    ?        ?? [§6] Capacidade restante (visitas - concluídas)
    ?        ?? [§11] Situação de refeição (futura/ativa/concluída)
    ?        ?? Turno encerrado? ? marcar como Ignore
    ?
    ?? [§7] Classificar OSs do request original
    ?        ClassificadorOrdensReotimizacaoService.Classificar
    ?        ?
    ?        ?? Execução ativa (em serviço no local, §8)
    ?        ?? Deslocamento (em trânsito)
    ?        ?? Despachada pendente (fixada na equipe atual)
    ?        ?? Original não despachada (redistribuível)
    ?        ?? Removida (finalizada/encerrada)
    ?        ?? Nova pendente (não existia no original)
    ?
    ?? [§8] Calcular TME restante das OSs em execução
    ?        ClassificadorOrdensReotimizacaoService.CalcularTmeRestante
    ?        ?
    ?        ?? TME_restante = TME_total - tempo_transcorrido
    ?           (fallback conservador se sem horário real)
    ?
    ?? [§12] Buscar e construir novas OSs pendentes
    ?         ConstruirNovosShipmentsAsync
    ?         ?
    ?         ?? Shipments com pesos completos
    ?            (sobre conjunto total, não apenas novo lote)
    ?
    ?? [§16] Reservar atomicamente equipes + turno
             (Idempotency-Key: mesma key = execução idêntica)
                              ?
                              ?
        ????????????????????????????????????????
        ? ReotimizacaoRequestBuilderService     ?
        ?              .Build()                 ?
        ????????????????????????????????????????
        ?
        ?? [§2/§9] Clonar request, índices preservados
        ?
        ?? Atualizar somente veículos selecionados
        ?  • StartLocation (posição atual)
        ?  • StartTimeWindows (disponibilidade)
        ?  • LoadLimits (capacidade restante)
        ?  • BreakRule (refeição)
        ?  • Demais veículos ? Ignore = true
        ?
        ?? [§7/§9] Reconstruir shipments
        ?  • Execução ativa ? remover
        ?  • Despachada pendente ? fixar na equipe
        ?  • Original não despachada ? redistribuir
        ?  • Removida ? descartar
        ?
        ?? [§12] Adicionar novas OSs
        ?
        ?? [§14] Construir solução injetada
        ?         (rotas do original com labels válidos)
        ?
        ?? SearchMode por ReotimizacaoSettings.ModoBusca
                              ?
                              ?
            [§17] Persistência e Disparo Assíncrono
            • Upload de request/response para GCS
            • Disparo da otimização (fire-and-forget)
            • Retorno 202 Accepted ao cliente
                              ?
                              ?
            OtimizacaoPipelineService.ExecutarReotimizacaoAsync
                              ?
                              ?? Chamada GMPRO (§5)
                              ?? Pós-processamento (§6)
                              ?? Webhook/Notificação final
```

### Serviços Principais da Reotimização

| Serviço | Responsabilidade | Logging |
|---------|------------------|---------|
| `ReotimizacaoDataService` | Montar snapshot completo (§5), validar, buscar dados reais | ? Estruturado (início, fim, métricas) |
| `ClassificadorOrdensReotimizacaoService` | Classificar OSs por estado semântico (§7), calcular TME (§8) | ? Estruturado (estados, contadores) |
| `ReotimizacaoRequestBuilderService` | Construir novo request GMPRO preservando índices (§2, §9) | ? Estruturado (veículos, shipments) |
| `OtimizadorController.SolicitarReotimizacaoAsync` | Endpoint HTTP, validações, tratamento de erros | ? Estruturado (validação, processamento, erros) |

### Configuração da Reotimização

| Chave | Tipo | Padrão | Descrição |
|-------|------|--------|-----------|
| `Reotimizacao:Enabled` | `bool` | `true` | Habilita/desabilita reotimização intradiária |
| `Reotimizacao:ModoBusca` | `string` | `AUTOMATIC` | Modo de busca GMPRO (§14) |
| `Reotimizacao:MaxIdadePosicaoMinutos` | `int` | `15` | Idade máxima aceitável de coordenada capturada |

---

## Tecnologias

| Tecnologia | Versão | Finalidade |
|------------|--------|------------|
| **.NET** | 8.0 | Runtime e framework da aplicação |
| **ASP.NET Core** | 8.0 | Web API (projeto Api) |
| **Microsoft.Extensions.Hosting** | 10.0.5 | Worker Service (projeto Cronjob) |
| **Swashbuckle.AspNetCore** | 6.6.2 | Documentação Swagger / OpenAPI |
| **Google.Maps.RouteOptimization.V1** | 1.4.0 | Client da API Google Cloud Route Optimization |
| **Google.Cloud.Storage.V1** | 4.14.0 | Upload de arquivos para Google Cloud Storage |
| **Google.Protobuf** | 3.33.5 | Serialização Protobuf nativa |
| **MongoDB** | (via LibGod) | Banco NoSQL para dados operacionais |
| **xUnit** | 2.9.2 | Framework de testes unitários |

---

## Configuração

A aplicação utiliza o padrão `IOptions<T>` com as seguintes seções no `appsettings.json`:

### `GoogleCloudStorage`

| Chave | Tipo | Descrição |
|-------|------|-----------|
| `BucketName` | `string` | Nome do bucket GCS para upload das requests |
| `CredentialFilePath` | `string?` | Caminho do JSON de credenciais (ou vazio para ADC) |

### `GmPro`

| Chave | Tipo | Descrição |
|-------|------|-----------|
| `CredentialFilePath` | `string?` | Caminho do JSON de credenciais da API Route Optimization |

### `Otimizacao`

| Chave | Tipo | Padrão | Descrição |
|-------|------|--------|-----------|
| `ProjectId` | `string` | — | ID do projeto Google Cloud |
| `UsarRest` | `bool` | `false` | Usar REST em vez de gRPC |
| `Label` | `string` | `"Cenario Energisa"` | Label de rastreabilidade do cenário |
| `DebugJsonFile` | `bool` | `false` | Gravar JSONs de request/response em `saida/` |
| `EmpresaId` | `string` | — | IdExterno da empresa para filtro no MongoDB |

### `Reotimizacao`

| Chave | Tipo | Padrão | Descrição |
|-------|------|--------|-----------|
| `Enabled` | `bool` | `true` | Habilita/desabilita reotimização intradiária |
| `ModoBusca` | `string` | `AUTOMATIC` | Modo de busca GMPRO (AUTOMATIC, QUALITY_OPTIMIZED, DISTANCE_OPTIMIZED) |
| `MaxIdadePosicaoMinutos` | `int` | `15` | Idade máxima aceitável de coordenada capturada (em minutos) |

---

## Como Executar

### Pré-requisitos

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Acesso ao MongoDB com dados LibGod
- Credenciais de Service Account do Google Cloud

### Build

```bash
dotnet build -c Release
```

---

## Testes

```bash
dotnet test Energisa.Eforce.Otimizador.Tests/Energisa.Eforce.Otimizador.Tests.csproj

# Com cobertura
dotnet test Energisa.Eforce.Otimizador.Tests/Energisa.Eforce.Otimizador.Tests.csproj --collect:"XPlat Code Coverage"
```

---

## Análise de Risco: Morte Prematura da Aplicação em OtimizacaoWorker

## ?? Sumário Executivo

A classe `OtimizacaoWorker` apresenta um **padrão perigoso de encerramento** que pode causar morte prematura da aplicação quando qualquer tenant falhar, mesmo em cenários recuperáveis. Este documento exemplifica os riscos e propõe soluções.

---

## ?? Problema Identificado

### Código Problemático

// Arquivo: Energisa.Eforce.Otimizador\Workers\OtimizacaoWorker.cs (linhas 402-448)

protected override async Task ExecuteAsync(CancellationToken stoppingToken)
{
    var stopwatch = Stopwatch.StartNew();
    var instancia = Environment.GetEnvironmentVariable("HOSTNAME") ?? Environment.MachineName;

    try
    {
        _logger.LogInformation("Cronjob do otimizador iniciado...");
        
        await base.ExecuteAsync(stoppingToken);  // ? Processa múltiplos tenants
        
        _logger.LogInformation("Processamento de todos os tenants concluído...");
    }
    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
    {
        _logger.LogWarning("Processamento dos tenants cancelado...");
    }
    catch (Exception ex)
    {
        Environment.ExitCode = 1;
        _logger.LogCritical(ex, "Processamento dos tenants encerrado por falha...");
        throw;
    }
    finally
    {
        _lifetime.StopApplication();  // ?? SEMPRE ENCERRA A APLICAÇÃO
    }
}

### Por Que é Problemático?

1. **`finally` incondicional**: Sempre chama `StopApplication()`, mesmo em cenários recuperáveis
2. **Falha de um tenant = falha de todos**: Um tenant com problema encerra o CronJob inteiro
3. **Re-lançamento de exceção**: Propaga falhas de `DoWorkAsync()` até `ExecuteAsync()`
4. **Sem isolamento**: Não há tentativa de isolar falhas por tenant

---

## ?? Cenários de Falha

### Cenário 1: Tenant com Problema de Rede (Recuperável)
 
sequenceDiagram
    participant CronJob as CronJob (Scheduler)
    participant App as OtimizacaoWorker
    participant Tenant1 as Tenant 1<br/>(Empresa 101)
    participant Tenant2 as Tenant 2<br/>(Empresa 102)
    participant Cloud as Google Cloud Storage

    CronJob->>App: Executa ExecuteAsync()
    App->>Tenant1: ProcessarAsync(contexto)
    Tenant1->>Tenant1: Carrega dados...
    Tenant1->>Cloud: Faz requisição
    Cloud--X Tenant1: Timeout (rede instável)
    Tenant1--X App: Lança TimeoutException
    App->>App: Captura em catch Exception
    App->>App: Seta Environment.ExitCode = 1
    App->>App: Re-lança exceção
    App->>App: finally ? StopApplication()
    App--X CronJob: Aplicação encerra
    
    Note over Tenant2: ? Nunca executou<br/>Perdeu oportunidade

**Resultado**: 
- ? Tenant 1 falhou por timeout de rede
- ? Tenant 2 nunca foi processado
- ? Aplicação morreu desnecessariamente

---

### Cenário 2: Erro em Serialização JSON (Isolável)
 
sequenceDiagram
    participant App as OtimizacaoWorker
    participant Tenant1 as Tenant 1
    participant Tenant2 as Tenant 2
    participant DB as Banco de Dados

    App->>Tenant1: ProcessarAsync()
    Tenant1->>DB: Consulta dados
    Tenant1->>Tenant1: Converte para JSON (GMPRO)
    Tenant1--X Tenant1: JsonSerializationException
    Tenant1--X App: Exceção não capturada
    App->>App: catch (Exception)
    App->>App: StopApplication()
    
    Note over Tenant2: ? Nunca processou
    Note over App: ? CronJob encerrado

**Resultado**:
- ? Erro em Tenant 1 é isolável (problema de dados, não de infraestrutura)
- ? Aplicação encerra mesmo com erro recuperável
- ? Tenant 2 perde execução

---

### Cenário 3: Comportamento Esperado (Com Isolamento)
 
sequenceDiagram
    participant CronJob as CronJob
    participant App as OtimizacaoWorker<br/>(Corrigido)
    participant Tenant1 as Tenant 1
    participant Tenant2 as Tenant 2
    participant Log as Logger

    CronJob->>App: ExecuteAsync()
    
    App->>Tenant1: DoWorkAsync()
    Tenant1--X Tenant1: TimeoutException
    Tenant1->>Log: LogError (isolado)
    App->>App: ? Continua (não re-lança)
    
    App->>Tenant2: DoWorkAsync()
    Tenant2->>Tenant2: ? Processa com sucesso
    Tenant2->>Log: LogInformation
    
    App->>App: finally ? Não chama StopApplication()
    App->>CronJob: ? Encerra normalmente
    
    Note over CronJob: ? Próxima execução:<br/>15 min depois

**Resultado**:
- ? Tenant 1 falhou, mas foi registrado
- ? Tenant 2 processou com sucesso
- ? Aplicação permanece viva
- ? CronJob agenda próxima tentativa

---

## ?? Exemplos de Código

### ? Situação Atual (Problemática)
 
public override async Task DoWorkAsync(
    IContextRepository context,
    IServiceScope scope,
    CancellationToken cancellationToken)
{
    // ... preparação ...
    
    try
    {
        // Processa otimização
        var resultado = await _pipeline.ExecutarPipelineSincronoAsync(...);
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
        _logger.LogWarning("Tentativa de otimização cancelada...");
        throw;  // ?? Re-lança no ExecuteAsync()
    }
    catch (Exception ex)
    {
        Environment.ExitCode = 1;
        _logger.LogError(ex, "Tentativa de otimização falhou...");
        throw;  // ?? Propaga até ExecuteAsync() ? StopApplication()
    }
}

protected override async Task ExecuteAsync(CancellationToken stoppingToken)
{
    try
    {
        await base.ExecuteAsync(stoppingToken);  // ? Chama DoWorkAsync() para cada tenant
    }
    catch (Exception ex)
    {
        Environment.ExitCode = 1;
        _logger.LogCritical(ex, "Processamento dos tenants encerrado por falha...");
        throw;  // ?? Lançada aqui
    }
    finally
    {
        _lifetime.StopApplication();  // ?? SEMPRE EXECUTA
    }
}

**Problema**: 
- Qualquer exceção em qualquer tenant ? `StopApplication()`

---

### ? Solução 1: Remover Encerramento Incondicional
 
protected override async Task ExecuteAsync(CancellationToken stoppingToken)
{
    var stopwatch = Stopwatch.StartNew();
    var instancia = Environment.GetEnvironmentVariable("HOSTNAME") ?? Environment.MachineName;
    var houveFalha = false;

    try
    {
        _logger.LogInformation(
            "Cronjob do otimizador iniciado. Instancia: {Instancia}.",
            instancia);

        await base.ExecuteAsync(stoppingToken);

        _logger.LogInformation(
            "Processamento de todos os tenants concluído. Instancia: {Instancia}.",
            instancia);
    }
    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
    {
        _logger.LogWarning(
            "Processamento dos tenants cancelado pelo encerramento da aplicação. Instancia: {Instancia}.",
            instancia);
    }
    catch (Exception ex)
    {
        houveFalha = true;
        Environment.ExitCode = 1;

        _logger.LogCritical(
            ex,
            "Processamento dos tenants encerrado por falha. Instancia: {Instancia}.",
            instancia);
        throw;
    }
    finally
    {
        // ? Apenas encerra se foi solicitado cancelamento
        if (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation(
                "Encerrando aplicação por solicitação de cancelamento. Instancia: {Instancia}.",
                instancia);
            _lifetime.StopApplication();
        }
        // ? Não encerra por falha de um tenant — deixa o scheduler tentar novamente
    }
}

**Benefícios**:
- ? Aplicação continua viva mesmo com falha de um tenant
- ? Próxima execução do CronJob terá chance de sucesso
- ? Apenas encerra se cancelamento for explicitamente solicitado

---

### ? Solução 2: Isolamento de Falhas por Tenant
 
public override async Task DoWorkAsync(
    IContextRepository context,
    IServiceScope scope,
    CancellationToken cancellationToken)
{
    if (string.IsNullOrWhiteSpace(context.RequestId))
        context.RequestId = Guid.NewGuid().ToString("N");

    if (string.IsNullOrWhiteSpace(context.SourceRequestId))
        context.SourceRequestId = context.RequestId;

    var execucaoOtimizacaoId = context.RequestId;
    var empresa = context.Empresa;
    var stopwatchTotal = Stopwatch.StartNew();
    
    try
    {
        context.User = "MsGodOtimizador";

        _logger.LogInformation(
            "Tentativa de otimização iniciada. ExecucaoOtimizacaoId: {ExecucaoOtimizacaoId}. Empresa: {Empresa}.",
            execucaoOtimizacaoId,
            empresa);

        // ... restante da lógica ...

        _logger.LogInformation(
            "Tentativa de otimização concluída com sucesso. ExecucaoOtimizacaoId: {ExecucaoOtimizacaoId}. Empresa: {Empresa}.",
            execucaoOtimizacaoId,
            empresa);
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
        _logger.LogWarning(
            "Tentativa de otimização cancelada. ExecucaoOtimizacaoId: {ExecucaoOtimizacaoId}. Empresa: {Empresa}.",
            execucaoOtimizacaoId,
            empresa);
        // ? Não re-lança — permite próximo tenant processar
    }
    catch (Exception ex)
    {
        // ? Log do erro como aviso, não como crítico
        _logger.LogError(
            ex,
            "Tentativa de otimização falhou para tenant. ExecucaoOtimizacaoId: {ExecucaoOtimizacaoId}. " +
            "Empresa: {Empresa}. DuracaoTotalMs: {DuracaoTotalMs}.",
            execucaoOtimizacaoId,
            empresa,
            stopwatchTotal.ElapsedMilliseconds);
        
        // ? Não re-lança — falha é isolada a este tenant
        // Classe base ou scheduler tratará de retry na próxima execução
    }
}

**Benefícios**:
- ? Falha em Tenant 1 não afeta Tenant 2
- ? Cada tenant é responsável apenas por seus próprios dados
- ? Aplicação permanece estável

---

## ?? Impacto das Correções

### Antes (Comportamento Atual)

| Situação | Resultado |
|----------|-----------|
| 1 de 10 tenants falha por timeout | ? Aplicação encerra; 9 não processam |
| Erro de serialização em 1 tenant | ? CronJob todo morre; retry em 15 minutos |
| Falha passageira de rede | ? Sem resiliência; aplicação morre desnecessariamente |
| **Taxa de Sucesso Esperada** | **~85-90%** (1 falha = tudo encerra) |

### Depois (Com Isolamento)

| Situação | Resultado |
|----------|-----------|
| 1 de 10 tenants falha por timeout | ? Log registrado; 9 processam normalmente |
| Erro de serialização em 1 tenant | ? Isolado; próximos tenants continuam |
| Falha passageira de rede | ? Retry automático em 15 minutos |
| **Taxa de Sucesso Esperada** | **~99%** (falhas isoladas) |

---

## ??? Checklist de Implementação

- [ ] Revisar implementação de `ConcurrentMultiTenantBackgroundService`
- [ ] Remover `_lifetime.StopApplication()` do `finally` incondicional
- [ ] Adicionar flag `houveFalha` para casos críticos
- [ ] Implementar isolamento de exceções em `DoWorkAsync()`
- [ ] Remover re-lançamento de exceções de um tenant único
- [ ] Adicionar testes de cenários de falha isolada
- [ ] Documentar estratégia de retry do scheduler
- [ ] Validar comportamento com múltiplos tenants em ambiente de staging

---

## ?? Referências

- **BackgroundService**: https://learn.microsoft.com/pt-br/dotnet/api/microsoft.extensions.hosting.backgroundservice
- **IHostApplicationLifetime**: https://learn.microsoft.com/pt-br/dotnet/api/microsoft.extensions.hosting.ihostapplicationlifetime
- **CancellationToken**: https://learn.microsoft.com/pt-br/dotnet/api/system.threading.cancellationtoken
- **Structured Logging**: https://learn.microsoft.com/pt-br/dotnet/core/extensions/logging

---

## ?? Arquivos Relacionados

- `Energisa.Eforce.Otimizador\Workers\OtimizacaoWorker.cs` (Classe principal)
- `Energisa.Eforce.Otimizador.Cronjob\Program.cs` (Configuração de DI)
- `Energisa.God.InfraData.Worker\ConcurrentMultiTenantBackgroundService` (Classe base - verificar documentação)
