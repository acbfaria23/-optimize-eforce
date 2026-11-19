# Implementação dos Parâmetros de Custo e Penalidade

## ?? Resumo Executivo

Este documento detalha as mudanças implementadas para adicionar os três novos parâmetros de negócio ao sistema de otimização de rotas:

1. ? **Custo por Quilômetro** - Já estava implementado
2. ? **Custo de Saída da Base** - Já estava implementado  
3. ? **Distância para Penalidade** - Implementado nesta versão

---

## 1. Custo por Quilômetro (CustoKmEquipe)

### Status: ? JÁ IMPLEMENTADO

**Definição de Negócio:**
- Parâmetro denominado "Custo por Quilômetro"
- Valor informado em R$ (Real)
- Representa o custo operacional de deslocamento das equipes por quilômetro percorrido
- Aceita valores decimais com duas casas de precisão
- Utilizado pelo algoritmo para calcular o custo estimado dos deslocamentos das rotas

**Implementação Técnica:**

| Arquivo | Local | Descrição |
|---------|-------|-----------|
| `ConversaoConstantes.cs` | Linha 148 | Propriedade estática `public static double CustoKmEquipe { get; set; }` |
| `ConversaoConstantes.cs` | Linha 221 | Incluído na lista de chaves obrigatórias `_chavesObrigatorias` |
| `ConversaoConstantes.cs` | Linha 276 | Serialização em `ToValoresDictionary()` |
| `ConversaoConstantes.cs` | Linha 364 | Carregamento em `CarregarDeParametro()` via `SetDouble()` |
| `SourceMapper.cs` | Linha 101 | Mapeado de `ConversaoConstantes.CustoKmEquipe` para `EquipeGmpro.CustoKm` |
| `GmProBuilderService.cs` | Linha 313 | Aplicado ao Vehicle via `vehicle.CostPerKilometer` |

**Fluxo de Dados:**
```
MongoDB (Eforce:ValoresPesoOtimizacao)
  ?
CarregarDeParametro()
  ?
ConversaoConstantes.CustoKmEquipe
  ?
SourceMapper (Equipe ? EquipeGmpro)
  ?
GmProBuilderService (Vehicle.CostPerKilometer)
  ?
Google Cloud Route Optimization API
```

---

## 2. Custo de Saída da Base (CustoFixoEquipe)

### Status: ? JÁ IMPLEMENTADO

**Definição de Negócio:**
- Parâmetro denominado "Custo de Saída da Base"
- Valor informado em R$ (Real)
- Representa o custo fixo considerado sempre que uma equipe iniciar sua rota
- O valor é somado ao custo total da rota de cada equipe otimizada
- Aceita valores decimais com duas casas de precisão

**Implementação Técnica:**

| Arquivo | Local | Descrição |
|---------|-------|-----------|
| `ConversaoConstantes.cs` | Linha 151 | Propriedade estática `public static double CustoFixoEquipe { get; set; }` |
| `ConversaoConstantes.cs` | Linha 222 | Incluído na lista de chaves obrigatórias `_chavesObrigatorias` |
| `ConversaoConstantes.cs` | Linha 277 | Serialização em `ToValoresDictionary()` |
| `ConversaoConstantes.cs` | Linha 365 | Carregamento em `CarregarDeParametro()` via `SetDouble()` |
| `SourceMapper.cs` | Linha 102 | Mapeado de `ConversaoConstantes.CustoFixoEquipe` para `EquipeGmpro.CustoFixo` |
| `GmProBuilderService.cs` | Linha 314 | Aplicado ao Vehicle via `vehicle.FixedCost` |

**Fluxo de Dados:**
```
MongoDB (Eforce:ValoresPesoOtimizacao)
  ?
CarregarDeParametro()
  ?
ConversaoConstantes.CustoFixoEquipe
  ?
SourceMapper (Equipe ? EquipeGmpro)
  ?
GmProBuilderService (Vehicle.FixedCost)
  ?
Google Cloud Route Optimization API
```

---

## 3. Distância para Penalidade (DistanciaParaPenalidade)

### Status: ? IMPLEMENTADO NESTA VERSÃO

**Definição de Negócio:**
- Parâmetro denominado "Distância para Penalidade"
- Valor informado em quilômetros (km)
- Define a distância máxima considerada ideal para deslocamentos entre atendimentos
- Quando o deslocamento ultrapassa a distância parametrizada, uma penalidade é aplicada
- Objetivo: desestimular construção de rotas com deslocamentos excessivos, favorecendo rotas compactas e eficientes
- A penalidade é apenas critério de otimização, não impedindo a criação da rota quando não houver alternativa operacional
- Aceita valores inteiros (em quilômetros)

**Implementação Técnica:**

### 3.1 Adição da Propriedade Estática

**Arquivo:** `Energisa.Eforce.Otimizador\Configuration\ConversaoConstantes.cs`

```csharp
/// <summary>
/// Distância máxima ideal (em quilômetros) para deslocamentos entre atendimentos.
/// Quando ultrapassada, uma penalidade é aplicada para desestimular rotas com 
/// deslocamentos excessivos.
/// </summary>
public static int DistanciaParaPenalidade { get; set; }
```

**Localização:** Linhas 153-156

---

### 3.2 Inclusão na Lista de Chaves Obrigatórias

**Arquivo:** `Energisa.Eforce.Otimizador\Configuration\ConversaoConstantes.cs`

```csharp
private static readonly IReadOnlyList<string> _chavesObrigatorias =
[
    // ... outras chaves ...
    nameof(CustoFixoEquipe),
    nameof(DistanciaParaPenalidade)  // ? NOVO
];
```

**Localização:** Linha 223

**Impacto:** O parâmetro é obrigatório durante a validação. Se não estiver presente no MongoDB, uma `InvalidOperationException` será lançada com mensagem clara listando as chaves ausentes.

---

### 3.3 Serialização para Persistência

**Arquivo:** `Energisa.Eforce.Otimizador\Configuration\ConversaoConstantes.cs`

```csharp
public static Dictionary<string, string> ToValoresDictionary() => new()
{
    // ... outros parâmetros ...
    [nameof(CustoFixoEquipe)] = CustoFixoEquipe.ToString(CultureInfo.InvariantCulture),
    [nameof(DistanciaParaPenalidade)] = DistanciaParaPenalidade.ToString(CultureInfo.InvariantCulture)  // ? NOVO
};
```

**Localização:** Linha 278

**Uso:** Método auxiliar para serializar todos os parâmetros para persistência ou auditoria.

---

### 3.4 Carregamento Dinâmico do MongoDB

**Arquivo:** `Energisa.Eforce.Otimizador\Configuration\ConversaoConstantes.cs`

```csharp
public static void CarregarDeParametro(Dictionary<string, string?> valores)
{
    _chavesCarregadas.Clear();

    // ... funções auxiliares SetDouble(), SetInt(), etc. ...

    // Carregamento dos parâmetros
    SetDouble(nameof(CustoKmEquipe), v => CustoKmEquipe = v);
    SetDouble(nameof(CustoFixoEquipe), v => CustoFixoEquipe = v);
    SetInt(nameof(DistanciaParaPenalidade), v => DistanciaParaPenalidade = v);  // ? NOVO
}
```

**Localização:** Linha 366

**Detalhe:** Utiliza `SetInt()` porque o valor é em quilômetros (inteiros), diferentemente dos custos que usam `SetDouble()`.

---

### 3.5 Implementação da Lógica de Penalidade

**Arquivo:** `Energisa.Eforce.Otimizador\Services\GmProBuilderService.cs`

#### 3.5.1 Novo Método Auxiliar

```csharp
/// <summary>
/// Constrói o <see cref="TransitionAttributes"/> para aplicar penalidade em deslocamentos
/// que ultrapassam a distância máxima parametrizada (DistanciaParaPenalidade).
/// A penalidade desestimula a construção de rotas com deslocamentos excessivos,
/// favorecendo rotas mais compactas e eficientes.
/// </summary>
private static TransitionAttributes ConstruirTransitionAttributesPenalidade()
{
    var distanciaMaximaMetros = ConversaoConstantes.DistanciaParaPenalidade * 1000;

    return new TransitionAttributes
    {
        CostPerKilometer = 0,
        DistanceLimit = new DistanceLimit
        {
            SoftMaxMeters = distanciaMaximaMetros,
            CostPerKilometerBelowSoftMax = 0,
            CostPerKilometerAboveSoftMax = 100
        }
    };
}
```

**Localização:** Linhas 419-438

**Funcionamento:**
- **SoftMaxMeters:** Define o limite ideal em metros (convertido de km)
- **CostPerKilometerBelowSoftMax:** Sem penalidade até o limite (custo = 0)
- **CostPerKilometerAboveSoftMax:** Penalidade de 100 por km acima do limite

#### 3.5.2 Integração no Método Build()

```csharp
var model = new ShipmentModel
{
    GlobalStartTime = ToTimestamp(adjustedStartTime),
    GlobalEndTime = ToTimestamp(adjustedEndTime),
    TransitionAttributes =
    { 
        new TransitionAttributes { /* atributos originais */ },
        ConstruirTransitionAttributesPenalidade()  // ? NOVO
    }
};
```

**Localização:** Linhas 50-71

**Funcionamento:** Adiciona um segundo `TransitionAttributes` à lista que aplica a penalidade de distância globalmente para todos os deslocamentos.

---

## 4. Testes Unitários

### 4.1 Setup dos Testes

**Arquivo:** `Energisa.Eforce.Otimizador.Tests\Helpers\ConversaoConstantesTestSetup.cs`

```csharp
[ModuleInitializer]
public static void Inicializar()
{
    ConversaoConstantes.CarregarDeParametro(new Dictionary<string, string?>
    {
        // ... outros parâmetros ...
        [nameof(ConversaoConstantes.CustoKmEquipe)] = "1.5",
        [nameof(ConversaoConstantes.CustoFixoEquipe)] = "0",
        [nameof(ConversaoConstantes.DistanciaParaPenalidade)] = "10"  // ? NOVO
    });

    ConversaoConstantes.Validar();
}
```

**Localização:** Última linha do dicionário

**Valor de Teste:** 10 km (distância máxima ideal para os testes)

### 4.2 Testes de Validação

**Arquivo:** `Energisa.Eforce.Otimizador.Tests\Configuration\ConversaoConstantesTests.cs`

```csharp
[Fact]
public void DistanciaParaPenalidade_DeveSerDez()
{
    Assert.Equal(10, ConversaoConstantes.DistanciaParaPenalidade);
}

[Fact]
public void CustoKmEquipe_DeveSerPositivo()
{
    Assert.True(ConversaoConstantes.CustoKmEquipe >= 0);
}

[Fact]
public void CustoFixoEquipe_DeveSerNaoNegativo()
{
    Assert.True(ConversaoConstantes.CustoFixoEquipe >= 0);
}
```

**Localização:** Linhas 171-184

**Cobertura:**
- Validação do valor padrão do novo parâmetro
- Validação de positividade dos custos (km e fixo)

---

## 5. Configuração do MongoDB

Para ativar estes parâmetros no ambiente de execução, a seguinte estrutura deve estar presente na coleção `ParametroConfig` do MongoDB:

```json
{
  "_id": ObjectId("..."),
  "NomeParametro": "Eforce:ValoresPesoOtimizacao",
  "Valores": {
    "CustoKmEquipe": "1.50",           // Custo por km em R$
    "CustoFixoEquipe": "50.00",        // Custo fixo por equipe em R$
    "DistanciaParaPenalidade": "15",   // Distância máxima ideal em km
    // ... outros parâmetros ...
  },
  "DataUltimaAtualizacao": ISODate("2024-01-15T10:30:00Z")
}
```

---

## 6. Fluxo Completo de Dados

```
???????????????????????????????????????????????????????????????????
? 1. CARREGAMENTO DE PARÂMETROS                                  ?
???????????????????????????????????????????????????????????????????
? MongoDB (Eforce:ValoresPesoOtimizacao)                          ?
?   ?? CustoKmEquipe: "1.50"                                      ?
?   ?? CustoFixoEquipe: "50.00"                                   ?
?   ?? DistanciaParaPenalidade: "15"                              ?
?                                                                  ?
? OtimizacaoPipelineService.ExecutarAsync()                       ?
?   ?? Chama ConversaoConstantes.CarregarDeParametro()            ?
?       ?? Chama ConversaoConstantes.Validar() ?                  ?
???????????????????????????????????????????????????????????????????
                              ?
???????????????????????????????????????????????????????????????????
? 2. CONSTRUÇÃO DO REQUEST OTIMIZAÇÃO                            ?
???????????????????????????????????????????????????????????????????
? GmProBuilderService.Build()                                     ?
?   ?? ConstruirVeiculos()                                        ?
?   ?   ?? vehicle.CostPerKilometer = CustoKmEquipe               ?
?   ?   ?? vehicle.FixedCost = CustoFixoEquipe                    ?
?   ?                                                              ?
?   ?? ShipmentModel.TransitionAttributes[]                       ?
?       ?? ConstruirTransitionAttributesPenalidade()              ?
?           ?? SoftMaxMeters = DistanciaParaPenalidade * 1000     ?
???????????????????????????????????????????????????????????????????
                              ?
???????????????????????????????????????????????????????????????????
? 3. OTIMIZAÇÃO                                                   ?
???????????????????????????????????????????????????????????????????
? Google Cloud Route Optimization API                             ?
?   ?? Calcula custo de deslocamento:                             ?
?   ?   custo_km = distancia_km × CustoKmEquipe                  ?
?   ?                                                              ?
?   ?? Aplica penalidade:                                         ?
?   ?   IF distancia > DistanciaParaPenalidade:                  ?
?   ?     penalidade = (distancia - limite) × 100 R$/km           ?
?   ?                                                              ?
?   ?? Calcula custo total da rota:                               ?
?       custo_total = CustoFixoEquipe + custo_km + penalidade     ?
?                                                                  ?
?   Result: Rotas otimizadas com deslocamentos eficientes         ?
???????????????????????????????????????????????????????????????????
```

---

## 7. Comportamento Esperado

### Exemplo 1: Distância Dentro do Limite

```
Parâmetros:
- DistanciaParaPenalidade = 15 km
- CustoKmEquipe = 2.00 R$/km
- CustoFixoEquipe = 50.00 R$

Rota A:
- Distância total: 12 km
- Custo = 50.00 + (12 × 2.00) + 0 = 74.00 R$
- Status: ? Sem penalidade
```

### Exemplo 2: Distância Acima do Limite

```
Parâmetros:
- DistanciaParaPenalidade = 15 km
- CustoKmEquipe = 2.00 R$/km
- CustoFixoEquipe = 50.00 R$

Rota B:
- Distância total: 22 km
- Penalidade: (22 - 15) × 100 = 700.00 R$
- Custo = 50.00 + (22 × 2.00) + 700.00 = 794.00 R$
- Status: ? Com penalidade (desestimulada, mas viável se necessário)
```

### Exemplo 3: Comparação de Rotas

O algoritmo preferirá **Rota A** (74.00 R$) em detrimento de **Rota B** (794.00 R$) quando ambas forem viáveis, **incentivando rotas mais compactas e eficientes**.

---

## 8. Resumo de Mudanças

| Arquivo | Tipo | Linhas | Descrição |
|---------|------|--------|-----------|
| `ConversaoConstantes.cs` | Propriedade | 153-156 | Nova propriedade `DistanciaParaPenalidade` |
| `ConversaoConstantes.cs` | Chaves | 223 | Adição à lista `_chavesObrigatorias` |
| `ConversaoConstantes.cs` | Serialização | 278 | Adição em `ToValoresDictionary()` |
| `ConversaoConstantes.cs` | Carregamento | 366 | Adição em `CarregarDeParametro()` |
| `GmProBuilderService.cs` | Integração | 50-71 | Integração no método `Build()` |
| `GmProBuilderService.cs` | Método | 419-438 | Novo método `ConstruirTransitionAttributesPenalidade()` |
| `ConversaoConstantesTestSetup.cs` | Setup | Última linha | Parâmetro de teste DistanciaParaPenalidade = "10" |
| `ConversaoConstantesTests.cs` | Testes | 171-184 | 3 novos testes unitários |

---

## 9. Validação e Testes

? **Build:** Compilação bem-sucedida sem erros ou warnings

? **Testes Unitários:** 
- `DistanciaParaPenalidade_DeveSerDez()`
- `CustoKmEquipe_DeveSerPositivo()`
- `CustoFixoEquipe_DeveSerNaoNegativo()`

? **Validação de Parâmetros:** 
- Todos os três parâmetros são obrigatórios
- `ConversaoConstantes.Validar()` lança exceção se algum estiver faltando

? **Carregamento Dinâmico:**
- Suporta carregamento via MongoDB
- Suporta sobrescrita via testes
- Registra chaves carregadas internamente

---

## 10. Notas de Implementação

### Conversão de Unidades
- **DistanciaParaPenalidade:** Recebido em quilômetros (inteiro), convertido para metros na API (`* 1000`)
- **CustoKmEquipe:** Recebido em R$/km (decimal com até 2 casas)
- **CustoFixoEquipe:** Recebido em R$ (decimal com até 2 casas)

### Penalidade de Distância
- **Estratégia:** Utiliza `TransitionAttributes.DistanceLimit` com `SoftMaxMeters` e `CostPerKilometerAboveSoftMax`
- **Valor de Penalidade:** 100 R$/km (acima do limite) - valor conservador que desestimula sem tornar inviável
- **Comportamento:** Soft constraint - penaliza mas não impede a construção da rota se necessário operacionalmente

### Compatibilidade
- ? .NET 8
- ? Google Cloud Route Optimization API v1
- ? MongoDB (dados estruturados como strings no formato `Invariant Culture`)
- ? Protobuf (tipos estruturados do Google)

---

## 11. Próximos Passos

1. **Validação em Produção:** Carregar parâmetros do MongoDB em ambiente de teste/produção
2. **Monitoramento:** Registrar em logs o valor de DistanciaParaPenalidade e sua utilização
3. **Ajuste Fino:** Após análise de rotas, ajustar o valor de penalidade (atualmente 100 R$/km) conforme necessário
4. **Documentação:** Atualizar guia de usuário com instruções de parametrização no MongoDB

---

**Data de Implementação:** 2024  
**Versão:** 1.0  
**Status:** ? Pronto para Produção
