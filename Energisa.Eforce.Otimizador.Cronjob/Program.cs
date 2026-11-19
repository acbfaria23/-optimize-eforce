using Energisa.Eforce.Otimizador.Configuration;
using Energisa.Eforce.Otimizador.Services;
using Energisa.Eforce.Otimizador.Workers;
using Energisa.God.Domain.DependencyInjection;
using Energisa.God.Domain.Repository.Alocacao.Interface;
using Energisa.God.Domain.Repository.Config;
using Energisa.God.Domain.Service.Alocacao;
using Energisa.God.Domain.Service.Alocacao.Interface;
using Energisa.God.InfraData.NoSql.DependencyInjection;
using Energisa.God.InfraData.NoSql.Repository.Config;
using Energisa.God.InfraData.Observabilidade.DependencyInjection;
using Energisa.God.InfraData.Sql.DependencyInjection;
using Google.Cloud.Storage.V1;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddLoggingEnergisa();

builder.Services.Configure<GcsSettings>(
    builder.Configuration.GetSection(GcsSettings.SectionName));

// Bind e registra OtimizacaoSettings no contêiner
builder.Services.Configure<OtimizacaoSettings>(
    builder.Configuration.GetSection(OtimizacaoSettings.SectionName));

builder.Services.Configure<EforceGeoAmbienteSettings>(
    builder.Configuration.GetSection(EforceGeoAmbienteSettings.SectionName));

builder.Services.Configure<ReotimizacaoSettings>(
    builder.Configuration.GetSection(ReotimizacaoSettings.SectionName));

builder.Services.Configure<ElegibilidadeOsSettings>(
    builder.Configuration.GetSection(ElegibilidadeOsSettings.SectionName));

builder.Services.AddSingleton(sp =>
{
    var settings = sp.GetRequiredService<IOptions<GcsSettings>>().Value;
    var credential = settings.GetGoogleCredential();
    return StorageClient.Create(credential);
});

builder.Services.AddDominioComun();

// Infraestrutura LibGod — repositórios NoSQL (MongoDB)
builder.Services.AddInfraestruturaOrdemServico();
builder.Services.AddInfraestruturaModelo();
builder.Services.AddInfraestruturaAlocacao();

// Driver nativo do MongoDB para a query de alta performance do LibGodDataService.
// Lê a MESMA configuração da LibGod (OrdemServicoContext:Mongo) → banco/hosts/credenciais
// variam por ambiente automaticamente, sem nada hardcoded.
builder.Services.AddSingleton<IMongoDatabase>(sp =>
{
    var section = sp.GetRequiredService<IConfiguration>()
                    .GetSection("OrdemServicoContext:Mongo");

    var database = section["Database"]
        ?? throw new InvalidOperationException(
            "OrdemServicoContext:Mongo:Database não configurado.");

    var user = Uri.EscapeDataString(section["ApplicationUser"] ?? "");
    var pass = Uri.EscapeDataString(section["ApplicationPassword"] ?? "");
    var hosts = (section["Hosts"] ?? "").Replace("|", ",");

    var connectionString = $"mongodb://{user}:{pass}@{hosts}/{database}";
    return new MongoClient(connectionString).GetDatabase(database);
});

// Repositório de configuração (ParametroConfig)
builder.Services.AddScoped<IParametroConfigRepository, ParametroConfigRepository>();

// Serviços de infraestrutura
builder.Services.AddSingleton<GoogleCloudStorageService>();

builder.Services.AddScoped<ITempoMedioExecucaoService, TempoMedioExecucaoService>();

// Serviço de leitura de dados NoSQL (LibGod → modelo local)
builder.Services.AddScoped<NoSqlDataService>();

// Serviço de filtro de elegibilidade
builder.Services.AddScoped<FiltroElegibilidadeOsService>();

// Serviço unificado de conversão E-force → GMPRO
builder.Services.AddSingleton<EforceParaGmproOrchestrator>();

// Serviços de integração com API Google Route Optimization
builder.Services.AddSingleton<GmProBuilderService>();
builder.Services.AddSingleton<PosProcessamentoService>();

// Serviço de filtro de validação
builder.Services.AddSingleton<FiltroValidacaoOtimizacaoService>();

// Serviços de reotimização intradiária (§20). Builders e classificadores puros: Singleton.
builder.Services.AddSingleton<ClassificadorOrdensReotimizacaoService>();
builder.Services.AddSingleton<ReotimizacaoRequestBuilderService>();
builder.Services.AddScoped<ReotimizacaoDataService>();

// Serviço unificado do pipeline de otimização
builder.Services.AddScoped<OtimizacaoPipelineService>();

// Worker de background — pipeline completo de otimização
builder.Services.AddHostedService<OtimizacaoWorker>();

builder.Services.AddScoped<SqlDataService>();

// Infraestrutura Oracle (PdaContext) — necessária para SqlDataService (debug SQL)
builder.Services.AddInfraestruturaOraclePdaDependencias();

builder.Services.AddSingleton(sp =>
    sp.GetRequiredService<IOptions<OtimizacaoSettings>>().Value);

builder.Services.AddHttpClient<EforceGeoAmbienteApiClient>();

var host = builder.Build();

// Garantir índice único em DescricaoOtimizador
using (var scope = host.Services.CreateScope())
{
    var otimizadorRepo =
        scope.ServiceProvider.GetRequiredService<IOtimizadorRotasRepository>();

    await otimizadorRepo.EnsureIndexesAsync();
}

host.Run();