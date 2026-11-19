# Energisa.Eforce.Otimizador.Cronjob

Worker Service que executa o pipeline de otimização de rotas de forma agendada (batch).

---

## Funcionamento

O `OtimizacaoWorker` é um `BackgroundService` que:

1. Carrega dados operacionais do MongoDB via `LibGodDataService`
2. Converte dados E-force para o modelo GMPRO via `EforceParaGmproOrchestrator`
3. Constrói a request Protobuf via `GmProBuilderService`
4. Faz upload para o Google Cloud Storage
5. Dispara a otimização na API Google Route Optimization
6. Pós-processa os resultados

---

## Executar

```bash
cd Energisa.Eforce.Otimizador.Cronjob
dotnet run --environment Development
```
