# Código da região de despacho
> IdExterno da região → `{empresa}_{polo}-{região}`

Entrada: `Energisa.Eforce.Otimizador/Mappers/SourceMapper.cs:SourceMapper.FormatCodRegiao()`

IdExterno: `codreg-codpolo-codregiao` (ex.: `1-10-1`)
- Leitura inferida do SQL (`Sql/query_*.sql`: `codreg`, `codpolo`, `codregiao`) e confirmada pelo dev
- `codreg` é fixo dentro da empresa → não entra no código

Código gerado: `20_10-1` (empresa 20, polo 10, região 1)
- Retorna `""` se `IdExterno` vazio, com < 3 partes, ou polo/região vazios

Por que o polo entra: na empresa 20 a região só é única dentro do polo
- `1-10-1` e `1-80-1` antes viravam ambos `20_1` → OS compatível com equipe de outro polo
- Regra vale para todas as empresas (decisão do dev; sem exceção por empresa)

Chamadores (todos via `FormatCodRegiao`):
- OS: `SourceMapper.ToLocal(OrdemServico)` → `RegiaoDespacho`
- Equipe: `SourceMapper.ToLocal(Equipe, ...)` → `EquipeGmpro.CodRegiao` (não lido na compatibilidade)
- Equipe: `SourceMapper.ToLocalRegioes()` → `RegiaoGmpro.CodRegiao`
- Reotimização: `ReotimizacaoDataService.cs:MontarEquipeReotimizacao()` (L492) → `RegioesAtuais`

Testes: `Tests/Mappers/SourceMapperTests.cs`, `Tests/Services/EforceParaGmproOrchestratorEdgeCaseTests.cs` (`*Polo*`)

Atualizado: 2026-10-07
