# SQL debug — formato de região diverge do NoSQL
> Colunas de região do SQL não passam por `FormatCodRegiao`

Entrada: `Services/SqlDataService.cs` — `MapearOrdemServico()`, `MapearEquipeComRegioes()`, `ParseRegioes()`

Formato no SQL (sem polo):
- OS: `REGIAO_DESPACHO = re.codregiao` (`Sql/query_ordens_servico.sql`, 2 ocorrências; idem `_old.sql`)
- Equipe: `REGIOES_EQUIPE = LISTAGG(emp.cod_empresa || '_' || er.codregiao)` (`Sql/query_equipes.sql`; idem `_old.sql`)
- `reg_prefixo` (`query_equipes.sql`): regiões fixas por prefixo (`CGA`, `CGB`, `CGM`, `CGO`) só com número
- `id_unico_regiao_despacho` (`empresa_codregiao`) existe na query de OS; C# não lê

Formato no NoSQL: `{empresa}_{polo}-{região}` ([regiao-codigo-idexterno](../domain/regiao-codigo-idexterno.md))

Efeito:
- SQL debug segue com a colisão por polo (empresa 20) — decisão do dev: não ajustar
- Dentro do SQL OS e equipe seguem coerentes (`RegiaoCompativel` casa número puro com o trecho após `_`)
- Não misturar OS de um formato com equipes do outro

`EquipeGmpro.CodRegiao` (SQL: `REGIAO_DESPACHO`; NoSQL: 1ª região) não é lido na compatibilidade.

Atualizado: 2026-10-07
