# Especificação para a LibGod — Persistência de Referências GCS para Reotimização Intradiária (Opção D)

> Documento de especificação técnica a ser repassado à equipe responsável pela `Energisa.God.Domain` (LibGod).
> Contexto: implementação da Reotimização Intradiária no `MsGodOtimizador`. Decisão arquitetural (Opção D):
> recuperar o `OptimizeToursRequest`/`OptimizeToursResponse` brutos (protobuf) de uma otimização anterior
> a partir do JSON já persistido no Google Cloud Storage (GCS), em vez de reconstruí-los por agregação
> (`ResultadoOtimizacao`) ou depender de convenção de nomes de arquivo.

---

## 1. Motivação

O `IOtimizadorRotasRepository` atual (LibGod 2.10.7) já expõe `ObterCenarioCompletoParaReotimizacaoAsync`,
mas ele retorna `ResultadoOtimizacao` — um DTO agregado (`RotaEquipe`/`OrdemServicoAtendida`) sem os campos
protobuf originais (`Shipment`, `Vehicle`, `AllowedVehicleIndices`, `LoadLimits`, `BreakRule`, `ShipmentRoute`
etc.). O `ReotimizacaoRequestBuilderService` (já implementado no MsGodOtimizador) precisa clonar e ajustar
esses campos protobuf exatamente como foram enviados/recebidos originalmente (§2, §9, §14 da spec de
reotimização), o que só é possível com o JSON bruto do request/response.

Esse JSON já existe fisicamente no GCS (upload feito antes do disparo assíncrono da otimização), mas hoje
**não há nenhuma referência persistida** que permita localizá-lo de forma confiável a partir do
`otimizacaoOriginalId`.

## 2. Requisitos de robustez (ressalvas já acordadas)

A referência ao objeto no GCS **não pode** ser reconstruída por convenção de nome de arquivo. Motivos:

- Um retry pode sobrescrever o arquivo anterior no mesmo nome.
- Uma nova tentativa pode reutilizar o mesmo nome (colisão).
- Mudanças futuras na convenção de nomenclatura quebrariam a leitura de registros históricos.
- Mudança de bucket (ex: migração, ambiente) não fica registrada em lugar nenhum.
- Ambientes/tenants distintos podem gerar colisões de nome.
- O objeto pode ser recriado (nova "generation") no mesmo nome, invalidando o conteúdo esperado.

Por isso, a referência deve ser **exata e imutável**: bucket + nome do objeto + **geração** (`generation`,
atribuída pelo GCS a cada versão do objeto) + **hash MD5** do conteúdo, capturados no momento exato do
upload/confirmação, e persistidos em banco junto ao registro da otimização.

No lado do MsGodOtimizador, já foi implementado o modelo `ReferenciaArmazenamentoGcs`:

```csharp
public class ReferenciaArmazenamentoGcs
{
    public string BucketName { get; set; } = string.Empty;
    public string ObjectName { get; set; } = string.Empty;
    public long Generation { get; set; }
    public string? Md5Hash { get; set; }
    public DateTime DataUploadUtc { get; set; }
}
```

E os métodos de upload/download que capturam e validam essa referência (`UploadJsonComReferenciaAsync`,
`DownloadJsonPorReferenciaAsync`, `ObterReferenciaObjetoAsync`) já estão implementados em
`GoogleCloudStorageService`. **O que falta é o lado de persistência na LibGod.**

## 3. Mudanças solicitadas na LibGod

### 3.1. Novo tipo `ReferenciaArmazenamentoGcs` (ou equivalente) no domínio `Alocacao.OtimizadorRotas`

```csharp
public class ReferenciaArmazenamentoGcs
{
    public string BucketName { get; set; }
    public string ObjectName { get; set; }
    public long Generation { get; set; }
    public string Md5Hash { get; set; }
    public DateTime DataUploadUtc { get; set; }
}
```

### 3.2. Novos campos em `ResultadoOtimizacao`

```csharp
public ReferenciaArmazenamentoGcs ReferenciaInput { get; set; }
public ReferenciaArmazenamentoGcs ReferenciaOutput { get; set; }
```

- `ReferenciaInput`: capturada no momento da criação do request (upload do JSON de input, antes do disparo).
- `ReferenciaOutput`: capturada somente após a confirmação de que o Google gravou o arquivo de output
  (no webhook `atualizar-response`), portanto deve poder ser **atualizada posteriormente** (não é
  conhecida no momento da criação do registro).

### 3.3. Novo(s) parâmetro(s) em `CriaRequestReotimizadorAsync`

Assinatura atual (confirmada por reflection na LibGod 2.10.7):

```csharp
Task<IOtimizadorRotas> CriaRequestReotimizadorAsync(
    OptimizeToursRequest request,
    string empresa,
    string otimizacaoOriginalId,
    List<string> equipesSelecionadas,
    string idempotencyKey,
    DateTime dataCorteUtc,
    string fusoHorario);
```

Solicita-se adicionar um parâmetro `ReferenciaArmazenamentoGcs referenciaInput`, persistido diretamente em
`ResultadoOtimizacao.ReferenciaInput`:

```csharp
Task<IOtimizadorRotas> CriaRequestReotimizadorAsync(
    OptimizeToursRequest request,
    string empresa,
    string otimizacaoOriginalId,
    List<string> equipesSelecionadas,
    string idempotencyKey,
    DateTime dataCorteUtc,
    string fusoHorario,
    ReferenciaArmazenamentoGcs referenciaInput);
```

Recomenda-se também adicionar o mesmo parâmetro (opcional, para não quebrar compatibilidade) em
`CriaRequestOtimizadorAsync`/`CriaResponseOtimizadorAsync`, para que o fluxo de otimização automática
(não apenas o de reotimização) também passe a ter essa rastreabilidade, viabilizando reotimizações
futuras a partir de qualquer otimização anterior (não só as que já tenham sido geradas com esse suporte).

### 3.4. Novo método para persistir a referência de output

A referência de output só é conhecida **depois** que o Google conclui o processamento assíncrono
(momento do webhook `atualizar-response`, hoje identificado apenas pelo `OperationName`). Solicita-se:

```csharp
Task AtualizarReferenciaOutputAsync(string id, ReferenciaArmazenamentoGcs referenciaOutput);
```

ou, alternativamente, por `OperationName` (mais consistente com `AtualizarOperationNameAsync` já existente):

```csharp
Task AtualizarReferenciaOutputPorOperationNameAsync(string operationName, ReferenciaArmazenamentoGcs referenciaOutput);
```

### 3.5. `ObterCenarioCompletoParaReotimizacaoAsync` deve expor as referências

O retorno atual (`ResultadoOtimizacao`) já passará a conter `ReferenciaInput`/`ReferenciaOutput` com a
mudança do item 3.2, então nenhuma alteração adicional de assinatura é necessária aqui — apenas garantir
que esses campos sejam preenchidos na projeção/query usada por esse método.

### 3.6. Índice único recomendado (opcional, reforço de integridade)

Sugere-se um índice único composto em `(ReferenciaInput.BucketName, ReferenciaInput.ObjectName, ReferenciaInput.Generation)`
na coleção de `ResultadoOtimizacao`, para reforçar em nível de banco a garantia de que cada referência de
input é única — detectando eventuais bugs de reuso acidental de referência entre otimizações distintas.

## 4. Compatibilidade com registros históricos

Registros de `ResultadoOtimizacao` criados **antes** desta mudança não terão `ReferenciaInput`/`ReferenciaOutput`
preenchidos. O `ReotimizacaoDataService` (a ser implementado no MsGodOtimizador) deve tratar esse cenário como
**não elegível para reotimização por reconstrução exata do protobuf**, retornando um erro de negócio claro
(ex: HTTP 422 — "Otimização original não possui referência de armazenamento GCS; não é possível reotimizar")
em vez de tentar aplicar qualquer fallback de convenção de nomes ou reconstrução aproximada.

## 5. Resumo do checklist para a LibGod

| # | Item | Tipo de mudança |
|---|------|------------------|
| 1 | Novo tipo `ReferenciaArmazenamentoGcs` (`BucketName`, `ObjectName`, `Generation`, `Md5Hash`, `DataUploadUtc`) | Novo modelo |
| 2 | Campos `ReferenciaInput`/`ReferenciaOutput` em `ResultadoOtimizacao` | Novo campo |
| 3 | Parâmetro `ReferenciaArmazenamentoGcs referenciaInput` em `CriaRequestReotimizadorAsync` | Alteração de assinatura |
| 4 | (Recomendado) mesmo parâmetro em `CriaRequestOtimizadorAsync`/`CriaResponseOtimizadorAsync` | Alteração de assinatura (opcional/retrocompatível) |
| 5 | Novo método `AtualizarReferenciaOutputAsync` (ou por `OperationName`) | Novo método no repositório |
| 6 | Preencher `ReferenciaInput`/`ReferenciaOutput` no retorno de `ObterCenarioCompletoParaReotimizacaoAsync` | Ajuste de projeção/query |
| 7 | Índice único opcional em `(BucketName, ObjectName, Generation)` | Infraestrutura de banco |

---

*Documento gerado a partir da implementação já realizada no `MsGodOtimizador` (branch `feature/Reotimizador`):
`Models/Reotimizacao/ReferenciaArmazenamentoGcs.cs`, `Services/GoogleCloudStorageService.cs` e
`Services/OtimizacaoPipelineService.cs`.*
