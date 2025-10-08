using Minio;
using OpenSearch.Client;
using Hangfire;
using Hangfire.Redis.StackExchange;
using StackExchange.Redis;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Extensions.Options;
using Npgsql;
using System.Text;
using Silo.Core.Services;
using Silo.Core.Pipeline;
using Silo.Core.Models;
using Silo.Core.Services.AI;
using Silo.Api.Services;
using Silo.Api.Services.Pipeline;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
// builder.Services.AddEndpointsApiExplorer();
// builder.Services.AddSwaggerGen();

// Configure MinIO
builder.Services.AddSingleton<IMinioClient>(provider =>
{
    var configuration = provider.GetRequiredService<IConfiguration>();
    var endpoint = configuration.GetConnectionString("MinIO") ?? "localhost:9000";
    var accessKey = configuration.GetValue<string>("MinIO:AccessKey") ?? "dev";
    var secretKey = configuration.GetValue<string>("MinIO:SecretKey") ?? "devpassword";
    
    return new MinioClient()
        .WithEndpoint(endpoint)
        .WithCredentials(accessKey, secretKey)
        .Build();
});

// Configure OpenSearch
builder.Services.AddSingleton<OpenSearchClient>(provider =>
{
    var configuration = provider.GetRequiredService<IConfiguration>();
    var connectionString = configuration.GetConnectionString("OpenSearch") ?? "http://localhost:9200";
    
    var settings = new ConnectionSettings(new Uri(connectionString))
        .DefaultIndex("files")
        .DisableDirectStreaming()
        .ThrowExceptions();
    
    return new OpenSearchClient(settings);
});

// Configure PostgreSQL data source
builder.Services.AddSingleton(provider =>
{
    var configuration = provider.GetRequiredService<IConfiguration>();
    var connectionString = configuration.GetConnectionString("Database");

    if (string.IsNullOrWhiteSpace(connectionString))
    {
        throw new InvalidOperationException("Database connection string is not configured.");
    }

    var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
    return dataSourceBuilder.Build();
});

// Configure Redis for Hangfire
builder.Services.AddSingleton<IConnectionMultiplexer>(provider =>
{
    var configuration = provider.GetRequiredService<IConfiguration>();
    var connectionString = configuration.GetConnectionString("Redis") ?? "localhost:6379";
    return ConnectionMultiplexer.Connect(connectionString);
});

// Configure JWT Authentication
var jwtSecretKey = builder.Configuration.GetValue<string>("Authentication:JwtSecretKey");
if (string.IsNullOrEmpty(jwtSecretKey))
{
    throw new InvalidOperationException("JWT secret key is not configured");
}

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration.GetValue<string>("Authentication:JwtIssuer"),
            ValidAudience = builder.Configuration.GetValue<string>("Authentication:JwtAudience"),
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecretKey))
        };
    });

builder.Services.AddAuthorization();

// Configure Hangfire
builder.Services.AddHangfire((provider, configuration) =>
{
    var redis = provider.GetRequiredService<IConnectionMultiplexer>();
    
    configuration
        .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
        .UseSimpleAssemblyNameTypeSerializer()
        .UseRecommendedSerializerSettings()
        .UseRedisStorage(redis, new RedisStorageOptions
        {
            Prefix = "silo:hangfire:",
            Db = 1
        });
});

builder.Services.AddHangfireServer(options =>
{
    options.Queues = new[] { "default", "file-processing", "backup", "ai-processing" };
    options.WorkerCount = Environment.ProcessorCount;
});

// Register application services
builder.Services.AddScoped<IStorageService, MinioStorageService>();
builder.Services.AddScoped<IMinioStorageService, MinioStorageServiceImpl>();
builder.Services.AddScoped<IOpenSearchIndexingService, OpenSearchIndexingService>();
builder.Services.AddScoped<ISearchService, SearchServiceAdapter>();
builder.Services.AddSingleton<IFileHashIndex, PostgresFileHashIndex>();

// Register pipeline services
builder.Services.AddScoped<PipelineOrchestrator>();
builder.Services.AddScoped<IPipelineStep, FileHashingStep>();
builder.Services.AddScoped<IPipelineStep, FileHashIndexingStep>();
builder.Services.AddScoped<IPipelineStep, MalwareScanningStep>();
builder.Services.AddScoped<IPipelineStep, FileStorageStep>();
builder.Services.AddScoped<IPipelineStep, ThumbnailGenerationStep>();
builder.Services.AddScoped<IPipelineStep, AIMetadataExtractionStep>();
builder.Services.AddScoped<IPipelineStep, FileIndexingStep>();
builder.Services.AddScoped<IPipelineStep, FileVersioningStep>();

// Register concrete pipeline step types for orchestrator
builder.Services.AddScoped<FileHashingStep>();
builder.Services.AddScoped<FileHashIndexingStep>();
builder.Services.AddScoped<MalwareScanningStep>();
builder.Services.AddScoped<FileStorageStep>();
builder.Services.AddScoped<ThumbnailGenerationStep>();
builder.Services.AddScoped<AIMetadataExtractionStep>();
builder.Services.AddScoped<FileIndexingStep>();
builder.Services.AddScoped<FileVersioningStep>();

// Register additional services  
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<AuthenticationService>();
builder.Services.AddScoped<FileSyncService>();
builder.Services.AddScoped<BackupService>();
builder.Services.AddScoped<IFileVersioningService, FileVersioningService>();
builder.Services.AddScoped<ThumbnailService>();
builder.Services.AddScoped<IClamAvService>(serviceProvider =>
{
    var logger = serviceProvider.GetRequiredService<ILogger<ClamAvService>>();
    var config = serviceProvider.GetRequiredService<IOptions<ClamAvConfiguration>>().Value;
    return new ClamAvService(logger, config);
});

// Register orchestrator interfaces
builder.Services.AddScoped<IPipelineOrchestrator, PipelineOrchestrator>();

// Configure settings and configuration classes
builder.Services.Configure<AuthConfiguration>(builder.Configuration.GetSection("Authentication"));
builder.Services.Configure<VersioningConfiguration>(builder.Configuration.GetSection("Versioning"));
builder.Services.Configure<BackupConfiguration>(builder.Configuration.GetSection("Backup"));
builder.Services.Configure<ThumbnailConfiguration>(builder.Configuration.GetSection("Thumbnails"));
builder.Services.Configure<PipelineConfiguration>(builder.Configuration.GetSection("Pipeline"));
builder.Services.Configure<ClamAvConfiguration>(builder.Configuration.GetSection("ClamAV"));
builder.Services.Configure<FileSyncConfiguration>(builder.Configuration.GetSection("FileSync"));

// Configure AI services
builder.Services.Configure<AIConfiguration>(builder.Configuration.GetSection("AI"));

// Register AI services
builder.Services.AddHttpClient<OpenAIMetadataService>();
builder.Services.AddHttpClient<OllamaMetadataService>();
builder.Services.AddHttpClient<AzureOpenAIMetadataService>();
builder.Services.AddScoped<OpenAIMetadataService>();
builder.Services.AddScoped<OllamaMetadataService>();
builder.Services.AddScoped<AzureOpenAIMetadataService>();
builder.Services.AddScoped<IAIMetadataServiceFactory, AIMetadataServiceFactory>();

// Register AI background jobs
builder.Services.AddScoped<AIMetadataBackgroundJob>();

// Register configuration instances
builder.Services.AddSingleton(serviceProvider =>
    serviceProvider.GetRequiredService<IOptions<AuthConfiguration>>().Value);
builder.Services.AddSingleton(serviceProvider =>
    serviceProvider.GetRequiredService<IOptions<VersioningConfiguration>>().Value);
builder.Services.AddSingleton(serviceProvider =>
    serviceProvider.GetRequiredService<IOptions<BackupConfiguration>>().Value);
builder.Services.AddSingleton(serviceProvider =>
    serviceProvider.GetRequiredService<IOptions<ThumbnailConfiguration>>().Value);
builder.Services.AddSingleton(serviceProvider =>
    serviceProvider.GetRequiredService<IOptions<PipelineConfiguration>>().Value);
builder.Services.AddSingleton(serviceProvider =>
    serviceProvider.GetRequiredService<IOptions<ClamAvConfiguration>>().Value);
builder.Services.AddSingleton(serviceProvider =>
    serviceProvider.GetRequiredService<IOptions<FileSyncConfiguration>>().Value);

// Configure CORS
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// Configure logging
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    // app.UseSwagger();
    // app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors();

// Use authentication and authorization
app.UseAuthentication();
app.UseAuthorization();

// Use Hangfire Dashboard
app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    Authorization = new[] { new Hangfire.Dashboard.LocalRequestsOnlyAuthorizationFilter() },
    DashboardTitle = "Silo File Management System - Jobs"
});

app.MapControllers();

// Health check endpoint
app.MapGet("/health", (IStorageService storageService, ISearchService searchService) =>
{
    var healthStatus = new
    {
        Status = "Healthy",
        Timestamp = DateTime.UtcNow,
        Services = new
        {
            Storage = "OK",
            Search = "OK",
            Background = "OK",
            Pipeline = "OK"
        }
    };
    
    return Results.Ok(healthStatus);
});

// Initialize services
using (var scope = app.Services.CreateScope())
{
    var searchService = scope.ServiceProvider.GetRequiredService<ISearchService>();
    await searchService.InitializeIndexAsync();
    
    // Start file sync service if enabled
    var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
    var fileSyncService = scope.ServiceProvider.GetService<FileSyncService>();
    if (fileSyncService != null)
    {
        var monitoredPaths = configuration.GetSection("FileSync:MonitoredPaths").Get<string[]>();
        if (monitoredPaths?.Length > 0)
        {
            await fileSyncService.StartMonitoringAsync();
        }
    }
}

app.Run();
