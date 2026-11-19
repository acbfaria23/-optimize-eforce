using Energisa.Eforce.Otimizador.Configuration;
using Energisa.Eforce.Otimizador.Services;
using Energisa.Eforce.Otimizador.Services.Interface;
using Energisa.God.Domain.DependencyInjection;
using Energisa.God.Domain.Repository.Alocacao.Interface;
using Energisa.God.InfraData.NoSql.DependencyInjection;
using Energisa.God.InfraData.Observabilidade.DependencyInjection;
using Energisa.Message.DependencyInjection;
using Microsoft.OpenApi.Models;
using MongoDB.Driver;
using System.Reflection;
using System.Text.Json.Serialization;


var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDominioComun();
builder.Services.AddControllers()
    .AddJsonOptions(options =>
        options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "E-force Otimizador API",
        Version = "v1",
        Description = "Converte dados do sistema E-force para o modelo Google Cloud Route Optimization (GMPRO), "
                    + "executa a otimização e pós-processa os resultados com indicadores operacionais.",
        Contact = new OpenApiContact
        {
            Name = "Operação e Despacho",
            Email = "teccom.operacao@energisa.com.br"
        }
    });

    var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath))
        options.IncludeXmlComments(xmlPath);

    var libXmlFile = "Energisa.Eforce.Otimizador.xml";
    var libXmlPath = Path.Combine(AppContext.BaseDirectory, libXmlFile);
    if (File.Exists(libXmlPath))
        options.IncludeXmlComments(libXmlPath);

    });

// Infraestrutura e domínio LibGod
builder.Services.AddInfraestruturaAlocacao();
builder.Services.AddDominioAlocacao();
builder.Services.AddInfraestruturaOrdemServico();
builder.Services.AddInfraestruturaModelo();
builder.Services.AddLoggingEnergisa();
builder.Services.AddBrokerProvider();

builder.Services.AddSingleton<IMongoDatabase>(sp =>
{
    var section = sp.GetRequiredService<IConfiguration>()
                    .GetSection("OrdemServicoContext:Mongo");

    var database = section["Database"]
        ?? throw new InvalidOperationException("OrdemServicoContext:Mongo:Database não configurado.");
    var user = Uri.EscapeDataString(section["ApplicationUser"] ?? "");
    var pass = Uri.EscapeDataString(section["ApplicationPassword"] ?? "");
    var hosts = (section["Hosts"] ?? "").Replace("|", ","); // LibGod usa "|" como separador

    var connectionString = $"mongodb://{user}:{pass}@{hosts}/{database}";

    return new MongoClient(connectionString).GetDatabase(database);
});

// Configurações do Otimizador
builder.Services.Configure<OtimizacaoSettings>(
    builder.Configuration.GetSection(OtimizacaoSettings.SectionName));
builder.Services.Configure<GcsSettings>(
    builder.Configuration.GetSection(GcsSettings.SectionName));
builder.Services.Configure<EforceGeoAmbienteSettings>(
    builder.Configuration.GetSection(EforceGeoAmbienteSettings.SectionName));
builder.Services.Configure<ReotimizacaoSettings>(
    builder.Configuration.GetSection(ReotimizacaoSettings.SectionName));
builder.Services.Configure<ElegibilidadeOsSettings>(
    builder.Configuration.GetSection(ElegibilidadeOsSettings.SectionName));

// Serviços do pipeline de otimização
builder.Services.AddScoped<NoSqlDataService>();
builder.Services.AddScoped<FiltroElegibilidadeOsService>();
builder.Services.AddSingleton<EforceParaGmproOrchestrator>();
builder.Services.AddSingleton<GmProBuilderService>();
builder.Services.AddSingleton<PosProcessamentoService>();
builder.Services.AddSingleton<FiltroValidacaoOtimizacaoService>();
builder.Services.AddScoped<OtimizacaoPipelineService>();
builder.Services.AddSingleton<DespachoAutomaticoProducerService>();
builder.Services.AddScoped<IResultadoOtimizacaoEnriquecimentoService, ResultadoOtimizacaoEnriquecimentoService>();
builder.Services.AddSingleton(sp =>
{
    var gcsSettings = builder.Configuration.GetSection(GcsSettings.SectionName).Get<GcsSettings>();
    var credential = gcsSettings?.GetGoogleCredential();
    return credential is not null
        ? Google.Cloud.Storage.V1.StorageClient.Create(credential)
        : Google.Cloud.Storage.V1.StorageClient.Create();
});
builder.Services.AddSingleton<GoogleStorageDownloadService>();
builder.Services.AddSingleton<GoogleCloudStorageService>();
builder.Services.AddHttpClient<EforceGeoAmbienteApiClient>();

// Serviços de reotimização intradiária (§20). Builders e classificadores puros: Singleton.
builder.Services.AddSingleton<ClassificadorOrdensReotimizacaoService>();
builder.Services.AddSingleton<ReotimizacaoRequestBuilderService>();
builder.Services.AddScoped<ReotimizacaoDataService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsProduction())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "E-force Otimizador API v1");
        options.DocumentTitle = "E-force Otimizador - Swagger";
    });
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

// Garantir índice único em DescricaoOtimizador
using (var scope = app.Services.CreateScope())
{
    var otimizadorRepo = scope.ServiceProvider.GetRequiredService<IOtimizadorRotasRepository>();
    await otimizadorRepo.EnsureIndexesAsync();
}

app.Run();
