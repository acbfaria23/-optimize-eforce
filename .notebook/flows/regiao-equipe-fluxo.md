# Regiões da equipe — origem e uso
> Carga → `DadosEforce.Regioes` → `EquipeIndexada.CodigosRegiao`

Entrada (NoSQL): `Services/NoSqlDataService.cs` (etapa `"ExtracaoRegioes"`) → `SourceMapper.ToLocalRegioes()`
- Uma `RegiaoGmpro { CodigoEquipe, TipoEquipe, CodRegiao }` por região da equipe
- Vazio → só `LogWarning` (`SemRegioesEquipe`), carga segue

Vínculo: `Services/EforceParaGmproOrchestrator.cs:ProcessarEquipes()`
- Agrupa por `(CodigoEquipe, TipoEquipe)` → `EquipeProcessada.CodigosRegiao`
- Equipe sem região → `LogWarning` e **sai do pipeline** (não vira vehicle)
- Match depende de `EquipeGmpro.CodigoEquipe/TipoEquipe` = `RegiaoGmpro.CodigoEquipe/TipoEquipe`

Indexação: `EforceParaGmproOrchestrator.cs:ConverterComDetalhes()` (passo 5/5) → `EquipeIndexada { IndiceVehicle, Equipe, CodigosRegiao }`

Reotimização (não passa por `DadosEforce`):
- `Services/ReotimizacaoDataService.cs:MontarEquipeReotimizacao()` → `EquipeReotimizacao.RegioesAtuais`
- `ReotimizacaoDataService.cs` (L704) → `EquipeIndexada.CodigosRegiao = RegioesAtuais`
- Novas OS: `ReotimizacaoDataService.cs` (L605) via `SourceMapper.ToLocal(OrdemServico)`
- Ambos os lados usam `FormatCodRegiao` → mesmo formato

Consumo: `RegiaoCompativel()` em `EforceParaGmproOrchestrator.cs`
- Compara `OS.RegiaoDespacho` com o código inteiro OU com o trecho após o 1º `_` (OrdinalIgnoreCase)
- Trecho após `_` existe p/ SQL debug (OS = número puro, equipe = `empresa_número`)

Filtro prévio: OS com `RegiaoDespacho is null` removida em `FiltrarOrdensServico()`
- `FormatCodRegiao` devolve `""` (não null) p/ IdExterno inválido → OS passa o filtro e falha em `RegiaoCompativel`

Ver: [compatibilidade-os-equipe](compatibilidade-os-equipe.md), [regiao-codigo-idexterno](../domain/regiao-codigo-idexterno.md)

Atualizado: 2026-10-07
