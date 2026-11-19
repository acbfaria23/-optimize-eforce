# MsGodOtimizador

Microsserviço de otimização de rotas de equipes de campo, responsável por converter dados do sistema **E-force** para o modelo da **Google Cloud Route Optimization API** (GMPRO), executar a otimização e pós-processar os resultados com indicadores operacionais e financeiros.

> **?? Este documento é mantido atualizado conforme a evolução da aplicação.**
>
> Última atualização: Julho/2025

---

## Índice

- [Visão Geral](#visão-geral)
- [Arquitetura](#arquitetura)
- [Projetos da Solução](#projetos-da-solução)
- [Pipeline de Otimização](#pipeline-de-otimização)
- [Tecnologias](#tecnologias)
- [Configuração](#configuração)
- [Como Executar](#como-executar)
- [Testes](#testes)

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
                              Service      (gRPC)    (Relatório)
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
