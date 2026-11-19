# Energisa.Eforce.Otimizador.Api

Web API RESTful que expõe endpoints para execução sob demanda do pipeline de otimização de rotas.

---

## Endpoints

### `POST /api/otimizador`

Recebe um `OptimizeToursResponse` (JSON do Google Route Optimization) e persiste o resultado no MongoDB.

**Request Body:** `application/json` — objeto `OptimizeToursResponse` serializado.

| Status | Descrição |
|--------|-----------|
| `201 Created` | Resposta persistida com sucesso |
| `500 Internal Server Error` | Erro interno ao processar a resposta |

---

### `POST /api/otimizador/otimizar`

Executa uma otimização síncrona filtrando apenas as ordens de serviço informadas. Retorna o `ResultadoOtimizacao` persistido no MongoDB.

**Request Body:**

```json
{
  "numerosOs": ["123456", "789012"]
}
```

| Status | Descrição |
|--------|-----------|
| `200 OK` | Otimização executada com sucesso — retorna `ResultadoOtimizacao` |
| `400 Bad Request` | Lista de OS vazia ou nenhuma OS encontrada após filtro |
| `500 Internal Server Error` | Erro interno ao executar o pipeline |

---

### `POST /otimizar-rotas/solicitar-reotimizacao`

Solicita a reotimização intradiária de uma otimização já executada, restringindo o novo
cálculo às equipes selecionadas. O serviço monta automaticamente o snapshot operacional
completo (posição/turno atual de cada equipe via `ObjetoMapa`, situação atual de cada OS na
coleção `ordem_servico`, novas OSs pendentes) e dispara a otimização de forma assíncrona
(fire-and-forget) via URI, assim como o fluxo automático.

Requer que a funcionalidade esteja habilitada via configuração (`Reotimizacao:Enabled`) e que a
otimização original possua as referências GCS de input/output persistidas (otimizações anteriores
a essa funcionalidade não são elegíveis).

**Request Body:**

```json
{
  "otimizacaoOriginalId": "6614abf1e2f4a90012345678",
  "equipes": ["CGRGA34", "CGRGA35"]
}
```

**Headers opcionais:** `User`, `Empresa`.

| Status | Descrição |
|--------|-----------|
| `202 Accepted` | Reotimização montada, persistida e disparada com sucesso (assíncrono) — retorna `label`, `otimizacaoOriginalId`, `ordensNaoAtendidas`, `ordensEmExecucao`, `ordensEmDeslocamento` e `avisos` |
| `400 Bad Request` | `otimizacaoOriginalId` não informado, lista de equipes vazia, funcionalidade desabilitada, otimização original não encontrada/sem referências GCS, ou equipe não encontrada no cenário original/cadastro atual |
| `500 Internal Server Error` | Erro interno ao montar o contexto ou disparar a reotimização |

---

## Swagger / OpenAPI

A documentação interativa está disponível em ambientes não-produtivos:

```
https://localhost:<porta>/swagger
```

---

## Executar

```bash
cd Energisa.Eforce.Otimizador.Api
dotnet run --environment Development
```
