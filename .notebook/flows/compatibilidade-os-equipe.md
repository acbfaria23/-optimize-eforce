# Compatibilidade OS ↔ Equipe
> `VerificarCompatibilidade` → `IndicesEquipesPermitidas` → `AllowedVehicleIndices`

Entrada: `Services/EforceParaGmproOrchestrator.cs:VerificarCompatibilidade()` (`private static bool`)
Cadeia: `AtribuirEquipes()` → `ObterEquipesCompativeis()` → `VerificarCompatibilidade()`
- Chamado em `ConverterComDetalhes()` (passo 5/5) e `ProcessarNovasOrdensParaReotimizacao()`
- Não há predicado inverso; "equipe compatível com OS" é o mesmo método

Regras (curto-circuito, 1ª que falha → `false`):
1. Perfil: se `Equipe.PerfilEquipe` preenchido → `os.PerfisEquipe` não vazio e contém o perfil
2. Região: algum `CodigosRegiao` casa em `RegiaoCompativel()`
3. Atuação: `AtuacaoEquipe != "U/R"` → deve igualar `Os.Localizacao` (trim/upper)
4. Corte restrito: `TipoCorte` (lote `X`: `DescricaoTipoCorte`) contém item de `ConversaoConstantes.ServicosRestritos1Pessoa` e `NPessoas == 1` → `false`

Atribuição (`AtribuirEquipes()`):
- Lote (`TipoOs == "X"`): equipe compatível mais próxima (`ConversaoHelper.Haversine`), 1 índice
- Avulsa: todos os índices compatíveis
- Nenhuma compatível → `LogWarning`, lista vazia

Origem de `os.PerfisEquipe`: `CriarOsProcessada()` ← `perfisPorCodserv[os.Codserv]` (de `DadosEforce.PerfisEquipe`)
- Reotimização: `ReotimizacaoDataService.cs` monta `PerfilEquipeGmpro` por (equipe selecionada × codserv das novas OS)

Efeito: `Services/GmProBuilderService.cs:ConstruirShipments()` ignora OS com lista vazia; demais → `shipment.AllowedVehicleIndices`
- Reotimização loga OS "não atendidas por incompatibilidade" em `OtimizacaoPipelineService.cs`

Motivo da rejeição não é logado por regra — só o aviso "nenhuma equipe compatível".

Ver: [regiao-equipe-fluxo](regiao-equipe-fluxo.md)

Atualizado: 2026-10-07
