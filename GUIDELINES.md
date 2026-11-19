# Guidelines de Desenvolvimento — MsGodOtimizador

> Versão 1.0 — gerado com base na revisão da branch `feature/ConstantesOtimizador`.  
> Estes guidelines devem ser seguidos por **todos** os desenvolvedores que contribuírem com esta aplicação.

---

## 1. Visão Geral da Arquitetura

A solução é composta por quatro projetos com responsabilidades bem definidas:

| Projeto | Responsabilidade |
|---|---|
| `Energisa.Eforce.Otimizador` | Biblioteca core: modelos, serviços, configuração, helpers e workers |
| `Energisa.Eforce.Otimizador.Api` | API REST (ASP.NET Core) — ponto de entrada via HTTP |
| `Energisa.Eforce.Otimizador.Cronjob` | Executável de CronJob — dispara o pipeline de forma agendada |
| `Energisa.Eforce.Otimizador.Tests` | Testes unitários (xUnit + Moq) |

**Regra:** Lógica de negócio **nunca** deve residir nos projetos `Api` ou `Cronjob`. Toda regra fica em `Energisa.Eforce.Otimizador`.

---

## 2. Estrutura de Pastas e Namespaces

```
Energisa.Eforce.Otimizador/
??? Configuration/   ? Settings (IOptions<T>), constantes de domínio
??? Helpers/         ? Funções utilitárias estáticas e puras
??? Mappers/         ? Mapeamento entre modelos (SourceMapper, etc.)
??? Models/
?   ??? Enums/       ? Enumerações de domínio
?   ??? Integracao/  ? DTOs / modelos de integração com sistemas externos
??? Services/
?   ??? Interface/   ? Interfaces públicas de serviços
??? Workers/         ? BackgroundService / Workers de execução agendada
```

**Regras:**
- O namespace deve **sempre** refletir o caminho da pasta.
- Não criar sub-pastas genéricas como `Utils`, `Common` ou `Misc`. Use `Helpers` para funções puras e `Services` para lógica com dependências.
- Modelos que representam contratos com sistemas externos (E-force, GMPRO, LibGod) ficam em `Models/Integracao`.

---

## 3. Injeção de Dependência

### 3.1 Registro no `Program.cs`
- **Regra de lifetime:**
  - `AddSingleton` ? serviços stateless com dependências que também são singleton (ex.: `EforceParaGmproOrchestrator`, `GmProBuilderService`).
  - `AddScoped` ? serviços que dependem de contexto por-requisição (ex.: `OtimizacaoPipelineService`, `NoSqlDataService`).
  - `AddTransient` ? apenas para objetos muito leves e sem estado compartilhado.
- Nunca injete um `Scoped` em um `Singleton` diretamente. Use `IServiceScopeFactory` quando necessário (veja `OtimizacaoPipelineService`).
- Configurações externas **sempre** via `IOptions<T>` / `IOptionsSnapshot<T>` — nunca leia `IConfiguration` diretamente dentro de um serviço.

### 3.2 Primary Constructors (.NET 8)
- Use **primary constructors** para classes de serviço simples.
- Quando a classe precisar de lógica no construtor (validação, inicialização), use construtor explícito.
- Evite misturar os dois estilos na mesma classe.

```csharp
// ? Correto — primary constructor simples
public class MeuServico(ILogger<MeuServico> logger, IOptions<MinhaConfig> options)
{
    private readonly ILogger<MeuServico> _logger = logger;
    private readonly MinhaConfig _config = options.Value;
}
```

---

## 4. Configurações (`Configuration/`)

### 4.1 Settings via `IOptions<T>`
- Cada bloco de configuração do `appsettings.json` deve ter uma classe de settings correspondente com `SectionName` definido como `const string`.
- Propriedades de settings devem ter **XML doc** descrevendo o propósito e impacto operacional.

```csharp
public class MinhaSettings
{
    public const string SectionName = "MinhaSecao";

    /// <summary>Descrição clara do que esta propriedade controla.</summary>
    public bool MinhaFlag { get; set; }
}
```

### 4.2 Constantes de Domínio (`ConversaoConstantes`)
- Constantes que dependem de parâmetros de banco (pesos, multiplicadores, janelas de tempo) **não possuem valor padrão** — devem ser carregadas explicitamente via `CarregarDeParametro` antes do uso.
- Sempre chamar `Validar()` antes de iniciar o pipeline para garantir que todas as chaves foram carregadas.
- Nunca usar `ConversaoConstantes` diretamente em testes unitários — forneça os valores explicitamente no `TestDataFactory`.

---

## 5. Serviços e Pipeline

### 5.1 `OtimizacaoPipelineService` — ponto central
- É o **único** serviço que orquestra o fluxo completo: carga de dados ? conversão ? construção da request ? execução ? pós-processamento.
- Controllers e Workers **nunca** devem reimplementar o pipeline; devem chamar os métodos públicos deste serviço.
- Novos passos do pipeline devem ser adicionados como métodos no `OtimizacaoPipelineService` ou em serviços dedicados injetados nele.

### 5.2 `EforceParaGmproOrchestrator` — conversão de dados
- Responsável **exclusivamente** pela transformação do modelo E-force para o modelo intermediário GMPRO.
- Não deve persistir dados, não deve fazer chamadas HTTP e não deve depender de `IConfiguration`.
- Cada passo da conversão deve ser um método privado nomeado com verbo de ação (ex.: `FiltrarEquipes`, `ProcessarEquipes`, `AplicarSufixoIdentificacao`).

### 5.3 `PosProcessamentoService` — análise do resultado
- Responsável por interpretar a resposta da API Google e calcular métricas/indicadores.
- Não deve alterar o estado do banco de dados — apenas produz um `RelatorioOtimizacao`.

### 5.4 Padrão de retorno de erro
- Prefira retornar `null` ou objetos vazios para indicar "sem dados" (ex.: `DadosEforce?`).
- Use exceções apenas para condições verdadeiramente excepcionais (configuração ausente, falha de infraestrutura).
- Nunca engula exceções silenciosamente — sempre logar antes de relançar ou retornar.

---

## 6. Controllers (`Api/Controllers/`)

- Controllers devem ser **finos**: receber a requisição, delegar ao serviço e retornar o resultado.
- Toda lógica de negócio deve estar nos serviços do projeto `Energisa.Eforce.Otimizador`.
- Sempre declarar `[ProducesResponseType]` para todos os códigos HTTP possíveis.
- Usar `ILogger` injetado (não `ILogger<T>` inline) e logar início/fim de cada endpoint com nível `Information`.
- Retornar `IActionResult` ou `ActionResult<T>` — nunca tipos concretos diretamente.

```csharp
// ? Correto
[HttpGet("meu-endpoint")]
[ProducesResponseType(typeof(MeuRetorno), StatusCodes.Status200OK)]
[ProducesResponseType(StatusCodes.Status404NotFound)]
public async Task<IActionResult> MeuEndpoint(CancellationToken ct)
{
    var resultado = await _pipeline.ExecutarAsync(ct);
    return resultado is null ? NotFound() : Ok(resultado);
}
```

---

## 7. Workers (`Workers/`)

- Workers herdam de `ConcurrentMultiTenantBackgroundService` (LibGod) — não use `BackgroundService` diretamente.
- A lógica de execução fica em `DoWorkAsync` e **deve** delegar ao `OtimizacaoPipelineService`.
- Flags de debug em tempo de compilação (`#if DEBUG`) são toleradas apenas durante desenvolvimento — devem ser **removidas** antes de merge na branch principal.
- Sempre verificar se o parâmetro de agendamento está configurado antes de executar (padrão atual com `ChaveHorarioOtimizacao`).

---

## 8. Modelos e DTOs

- Modelos de integração (E-force, GMPRO) ficam em `Models/Integracao/` — um arquivo por entidade.
- Usar **record** para DTOs imutáveis de resposta/request da API.
- Usar **class** para modelos que participam de pipelines e sofrem mutação.
- Inicializar coleções com `[]` (C# 12) em vez de `new List<T>()`.
- Não adicionar lógica de negócio em modelos — eles devem ser POCOs/Records puros.

```csharp
// ? Correto — modelo de integração
public class EquipeGmpro
{
    public string Codigo { get; set; } = string.Empty;
    public List<string> Regioes { get; set; } = [];
}
```

---

## 9. Helpers (`Helpers/`)

- Helpers são classes **estáticas** com métodos **estáticos** e **puros** (sem efeitos colaterais, sem dependências injetadas).
- Cada método deve ter XML doc com: descrição, parâmetros relevantes e referência à seção do documento de especificação (ex.: `§14.2`).
- Helpers **não** devem acessar `ConversaoConstantes` — recebam os valores como parâmetros.

---

## 10. Testes (`Energisa.Eforce.Otimizador.Tests/`)

### 10.1 Organização
- A estrutura de pastas do projeto de testes **espelha** a estrutura do projeto principal:
  - `Services/` ? testes de serviços
  - `Helpers/` ? testes de helpers
  - `Models/` ? testes de modelos e DTOs
  - `Configuration/` ? testes de settings e constantes

### 10.2 Nomenclatura
- Nome da classe: `{ClasseTestada}Tests.cs`
- Nome do método: `{Metodo}_{Cenario}_{ResultadoEsperado}`

```csharp
// ? Correto
public void ConverterComDetalhes_DadosVazios_RetornaResultadoVazio() { }
public void Haversine_CoordenadaIguais_RetornaZero() { }
```

### 10.3 Fábrica de dados (`TestDataFactory`)
- **Sempre** usar `TestDataFactory` para criar objetos de teste — nunca instanciar modelos inline nos testes.
- Quando um novo modelo for adicionado, adicionar o método de criação correspondente na `TestDataFactory`.
- `TestDataFactory.DataExecucao` deve ser usado como data de referência nos testes.

### 10.4 Mocks
- Usar **Moq** para mockar dependências.
- Mockar apenas o mínimo necessário — prefira instâncias reais quando possível (ex.: `EforceParaGmproOrchestrator` é testável sem mocks).
- Nunca mockar `ConversaoConstantes` — configure os valores diretamente no `[Fact]` ou em `TestDataFactory`.

### 10.5 Cobertura
- Todo novo serviço **deve** ter pelo menos os seguintes cenários cobertos:
  - Caminho feliz com dados completos.
  - Entrada vazia / nula.
  - Pelo menos um cenário de edge case relevante ao domínio.

---

## 11. Logging

- Usar `ILogger<T>` injetado — nunca `Console.WriteLine` ou `Debug.WriteLine`.
- Nível `Information` para eventos do pipeline (início, conclusão de cada passo, contagens).
- Nível `Warning` para condições anômalas que não interrompem o fluxo (parâmetro ausente, tenant sem dados).
- Nível `Error` para falhas que interrompem o processamento.
- **Sempre** usar structured logging (parâmetros nomeados entre `{}`):

```csharp
// ? Correto
_logger.LogInformation("Conversão concluída. OS processadas: {Os}, Equipes: {Equipes}.", osCount, equipeCount);

// ? Errado
_logger.LogInformation($"Conversão concluída. OS: {osCount}");
```

---

## 12. Convenções de Código

### 12.1 Idioma
- **Nomes de classes, métodos, propriedades e variáveis:** Português (domínio do negócio em PT-BR).
- **Comentários XML doc:** Português.
- **Commits e Pull Requests:** Português.

### 12.2 XML Doc
- Toda classe e método **público** deve ter `<summary>`.
- Parâmetros com semântica não óbvia devem ter `<param>`.
- Referenciar a seção do documento de especificação quando aplicável (ex.: `§13.2`).

### 12.3 Estilo
- Usar `var` quando o tipo é óbvio pelo lado direito da expressão.
- Preferir expressões LINQ sobre loops `foreach` para transformações de coleções.
- Usar `null-coalescing` (`??`, `??=`) e `null-conditional` (`?.`) em vez de blocos `if (x != null)`.
- Usar `string.Empty` em vez de `""` para strings vazias em propriedades de modelos.
- Preferir `is not null` / `is null` em vez de `!= null` / `== null`.

### 12.4 Async/Await
- Todo método que realiza I/O (HTTP, banco, GCS) deve ser `async Task<T>`.
- Sempre propagar `CancellationToken` até o último nível possível.
- Nunca usar `.Result` ou `.Wait()` — sempre `await`.

---

## 13. Segurança e Configuração de Ambiente

- **Nunca** commitar credenciais, connection strings ou chaves de API no repositório.
- Segredos de desenvolvimento devem usar `dotnet user-secrets`.
- Em produção, as configurações sensíveis são injetadas via variáveis de ambiente ou Secret Manager.
- A senha do MongoDB é construída dinamicamente em `Program.cs` com `Uri.EscapeDataString` — mantenha esse padrão para novos data sources.

---

## 14. Pull Requests e Revisão de Código

### Checklist mínimo antes de abrir PR:
- [ ] Build sem erros e sem warnings novos.
- [ ] Todos os testes existentes passando (`dotnet test`).
- [ ] Novos serviços/helpers cobertos por testes unitários.
- [ ] XML doc adicionado em todas as classes e métodos públicos novos.
- [ ] Nenhum `#if DEBUG`, `Console.WriteLine` ou código comentado no diff.
- [ ] Flags de feature/debug removidas ou protegidas por configuração.
- [ ] `ConversaoConstantes`: novos parâmetros adicionados ao método `Validar()`.
- [ ] `TestDataFactory` atualizada se novos modelos foram adicionados.

---

## 15. Extensibilidade — Adicionando Novos Passos ao Pipeline

Se for necessário adicionar uma nova etapa ao pipeline de otimização:

1. Criar o serviço na pasta `Services/` com interface em `Services/Interface/`.
2. Registrar no `Program.cs` com o lifetime correto.
3. Injetar no `OtimizacaoPipelineService` — **não** no Controller nem no Worker.
4. Adicionar o passo ao método `ConstruirRequestAsync` ou criar um novo método público no `OtimizacaoPipelineService`.
5. Adicionar testes para o novo serviço seguindo a estrutura de `EforceParaGmproOrchestratorTests`.
