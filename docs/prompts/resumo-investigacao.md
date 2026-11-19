# Contexto de Investigação - MsGodOtimizador / GMPro

## Objetivo

Investigar falhas na geração de inputs enviados para a API de otimização de rotas da Google (GMPro / Route Optimization API), identificando inconsistências de dados geradas pelo MsGodOtimizador antes do envio para processamento.

---

# Histórico da Investigação

## Problema 1 - Coordenadas inválidas de veículos

### Sintoma

A otimização estava falhando devido a coordenadas inválidas presentes no objeto `vehicles` do input enviado para a Google.

### Veículo afetado inicialmente

**Índice:** 181

```json
{
  "label": "I-PCOMO01, PERFIL: 69d50c725e117ca3ac2b4869",
  "latitude": -7.19695,
  "longitude": -3792820
}
```

### Problema encontrado

Longitude:

```json
-3792820
```

Valor fora da faixa permitida:

```text
Longitude válida: -180 até 180
```

Tudo indica que o ponto decimal foi perdido durante alguma transformação de dados.

Possível valor esperado:

```json
-37.92820
```

---

## Problema 2 - Nova geração do input

Após correção do primeiro veículo foi gerado um novo input.

Durante a análise foi encontrado outro veículo com o mesmo problema.

### Veículo afetado

**Índice:** 72

**Label:**

```text
I-PCOCR01, PERFIL: 69ce6963217bac6d1cb06edb
```

### Coordenadas encontradas

```json
{
  "latitude": -7.19695,
  "longitude": -3792820
}
```

### Problema

Longitude inválida:

```json
-3792820
```

Novamente o valor está muito fora do intervalo permitido pela Google.

### Hipótese

O cadastro de origem possui coordenada correta, porém alguma transformação realizada pelo MsGodOtimizador está removendo o separador decimal.

Possíveis causas:

- Conversão incorreta de string para número.
- CultureInfo incorreto.
- Remoção indevida de caracteres especiais.
- Sanitização de coordenadas.
- Mapeamento incorreto da entidade de equipe para o JSON da Google.

---

# Problema Atual

## Erro retornado pela GMPro

```json
{
  "code": 3,
  "message": "INVALID_REQUEST: Validation caught 2 error(s). Here is the first one: In `transition_attributes[1]`: `src_tag` and `excluded_src_tag` are both empty"
}
```

---

# Diagnóstico

A validação da Google identificou um objeto inválido dentro de:

```json
transitionAttributes[1]
```

---

## Estrutura válida

```json
{
  "srcTag": "limit-transition",
  "dstTag": "ASDF",
  "distanceLimit": {
    "softMaxMeters": "2000",
    "costPerKilometerAboveSoftMax": 1,
    "costPerKilometerBelowSoftMax": 100
  }
}
```

Existe pelo menos uma tag de origem:

```json
srcTag
```

ou

```json
excludedSrcTag
```

---

## Estrutura inválida encontrada

```json
{
  "distanceLimit": {
    "softMaxMeters": "100000",
    "costPerKilometerAboveSoftMax": 100,
    "costPerKilometerBelowSoftMax": 0
  }
}
```

O objeto foi criado sem:

```json
srcTag
```

e sem:

```json
excludedSrcTag
```

---

# Causa Provável

Existe alguma regra dentro do MsGodOtimizador que está gerando um objeto de transição parcialmente preenchido.

Exemplo de implementação problemática:

```csharp
transitionAttributes.Add(new TransitionAttribute
{
    DistanceLimit = ...
});
```

Ao invés de:

```csharp
if (!string.IsNullOrWhiteSpace(srcTag))
{
    transitionAttributes.Add(...);
}
```

ou

```csharp
if (!string.IsNullOrWhiteSpace(srcTag) ||
    !string.IsNullOrWhiteSpace(excludedSrcTag))
{
    transitionAttributes.Add(...);
}
```

---

# Ponto Muito Importante

A mensagem da Google informa:

```text
Validation caught 2 error(s)
```

Porém apenas o primeiro erro foi exibido.

Portanto:

- Existe pelo menos mais um erro de validação no input.
- Após corrigir o problema do `transitionAttributes[1]`, será necessário reenviar a otimização.
- Um novo erro provavelmente será retornado.

---

# Áreas do Código que Devem Ser Investigadas

## 1. Geração de Veículos

Responsável por:

```json
model.vehicles[]
```

Verificar:

- Latitude.
- Longitude.
- Conversão numérica.
- Formatação decimal.
- CultureInfo utilizado.
- Dados retornados do banco.
- Mapeamento da entidade de equipe para o modelo GMPro.

---

## 2. Geração de Transition Attributes

Responsável por:

```json
model.transitionAttributes[]
```

Verificar:

- srcTag
- excludedSrcTag
- dstTag
- excludedDstTag
- distanceLimit
- delay
- custos adicionais

Garantir que nenhum objeto seja criado sem pelo menos uma tag de origem.

---

# Melhorias Recomendadas no MsGodOtimizador

## Validação Pré-Envio

Executar uma validação completa do JSON antes da chamada da API do Google.

### Coordenadas

```csharp
longitude >= -180 && longitude <= 180
latitude >= -90 && latitude <= 90
```

### Transition Attributes

```csharp
!string.IsNullOrWhiteSpace(srcTag) ||
!string.IsNullOrWhiteSpace(excludedSrcTag)
```

### Vehicles

```csharp
vehicles.Count > 0
```

### Shipments

```csharp
shipments.Count > 0
```

---

## Log Estruturado

Registrar:

```text
ID da otimização
ID da equipe
VehicleIndex
Label do veículo
Latitude recebida
Longitude recebida
Latitude enviada
Longitude enviada
```

---

## Testes de Contrato

Criar testes automatizados que validem o JSON final antes do envio.

Validar:

```text
Campos obrigatórios
Tipos numéricos
Coordenadas válidas
TransitionAttributes válidos
AllowedVehicleIndices válidos
Regras exigidas pela API do Google
```

---

# Requisitos para o Futuro Agente "Bug Hunter GMPro"

O agente deverá receber um JSON de input de otimização e produzir um relatório completo de inconsistências antes do envio para a Google.

## Análise de Coordenadas

Identificar:

- Latitude fora da faixa.
- Longitude fora da faixa.
- Coordenadas nulas.
- Coordenadas zeradas.
- Coordenadas suspeitas.
- Coordenadas duplicadas em excesso.
- Possível perda de separador decimal.

Exemplo:

```text
longitude = -3792820
Possível valor correto: -37.92820
```

---

## Análise de Veículos

Identificar:

- Índices duplicados.
- Labels duplicados.
- Capacity inválida.
- Janela de trabalho inconsistente.
- Janela de fim anterior à janela de início.
- Coordenadas inválidas.

---

## Análise de Shipments

Identificar:

- Shipments sem pickup.
- Shipments sem delivery.
- AllowedVehicleIndices inexistentes.
- Referências quebradas.
- Time windows inválidas.
- Demandas incompatíveis.

---

## Análise de TransitionAttributes

Identificar:

- srcTag ausente.
- excludedSrcTag ausente.
- dstTag inválida.
- Objetos parcialmente preenchidos.
- Objetos vazios.
- Regras duplicadas.
- Regras conflitantes.

---

## Análise de Consistência Global

Identificar:

- Referências para veículos inexistentes.
- Referências para tags inexistentes.
- IDs duplicados.
- Campos obrigatórios ausentes.
- Valores fora dos limites aceitos pela API da Google.

---

## Relatório Esperado do Agente

O agente deve gerar saída semelhante a:

```text
CRÍTICO
---------
Vehicle[72]
Label: I-PCOCR01
Longitude inválida: -3792820

CRÍTICO
---------
TransitionAttribute[1]
srcTag ausente
excludedSrcTag ausente

ALERTA
---------
Shipment[154]
AllowedVehicleIndex referencia veículo inexistente

INFO
---------
218 veículos analisados
1432 shipments analisados
4 inconsistências encontradas
```

---

# Estado Atual da Investigação

## Concluído

✅ Identificado veículo 181 com longitude inválida.

✅ Identificado veículo 72 com longitude inválida.

✅ Confirmado padrão de perda de separador decimal em coordenadas.

✅ Identificado objeto inválido em `transitionAttributes[1]`.

✅ Confirmado que a Google rejeita o request antes de iniciar a otimização.