using Minio;
using OpenSearch.Client;
using Hangfire;
using Hangfire.Redis.StackExchange;
using StackExchange.Redis;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using System.Text;
using AspNetCoreRateLimit;
using Silo.Core.Services;
using Silo.Core.Pipeline;
using Silo.Core.Models;
using Silo.Core.Data;
using Silo.Core.Services.AI;
using Silo.Api.Services;
using Silo.Api.Services.Pipeline;
using Silo.Api.Middleware;
using Serilog;
using Serilog.Events;

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
    .MinimumLevel.Override("System", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .Enrich.WithThreadId()
    .Enrich.WithMachineName()
    .Enrich.WithEnvironmentName()
    .WriteTo.Console(
        outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}")
    .WriteTo.File(
        path: "logs/silo-.log",
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 30,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}")
    .CreateLogger();

var builder = WebApplication.CreateBuilder(args);

// Use Serilog for logging
builder.Host.UseSerilog();

try
{
    Log.Information("Starting Silo File Management System");

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

// Configure DbContext
builder.Services.AddDbContext<SiloDbContext>((provider, options) =>
{
    var dataSource = provider.GetRequiredService<NpgsqlDataSource>();
    options.UseNpgsql(dataSource, b => b.MigrationsAssembly("Silo.Api"));
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

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
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
    })
    .AddScheme<Silo.Api.Middleware.ApiKeyAuthenticationOptions, Silo.Api.Middleware.ApiKeyAuthenticationHandler>(
        Silo.Api.Middleware.ApiKeyAuthenticationOptions.DefaultSchemeName, options => { });

// Configure authorization with policies
builder.Services.AddAuthorization(options =>
{
    // File operation policies
    options.AddPolicy("FilesRead", policy =>
        policy.Requirements.Add(new Silo.Api.Authorization.PermissionRequirement("files:read")));
    
    options.AddPolicy("FilesWrite", policy =>
        policy.Requirements.Add(new Silo.Api.Authorization.PermissionRequirement("files:write")));
    
    options.AddPolicy("FilesDelete", policy =>
        policy.Requirements.Add(new Silo.Api.Authorization.PermissionRequirement("files:delete")));
    
    options.AddPolicy("FilesUpload", policy =>
        policy.Requirements.Add(new Silo.Api.Authorization.PermissionRequirement("files:upload")));
    
    options.AddPolicy("FilesDownload", policy =>
        policy.Requirements.Add(new Silo.Api.Authorization.PermissionRequirement("files:download")));

    // User management policies
    options.AddPolicy("UsersManage", policy =>
        policy.Requirements.Add(new Silo.Api.Authorization.PermissionRequirement("users:manage")));

    // Role-based policies
    options.AddPolicy("AdminOnly", policy =>
        policy.Requirements.Add(new Silo.Api.Authorization.RoleRequirement("Administrator")));
    
    options.AddPolicy("FileManagerOrAdmin", policy =>
        policy.Requirements.Add(new Silo.Api.Authorization.RoleRequirement("Administrator", "FileManager")));

    // Tenant policies
    options.AddPolicy("RequireTenant", policy =>
        policy.Requirements.Add(new Silo.Api.Authorization.TenantRequirement(true)));
});

// Register authorization handlers
builder.Services.AddSingleton<IAuthorizationHandler, Silo.Api.Authorization.PermissionHandler>();
builder.Services.AddSingleton<IAuthorizationHandler, Silo.Api.Authorization.RoleHandler>();
builder.Services.AddSingleton<IAuthorizationHandler, Silo.Api.Authorization.TenantHandler>();

// Configure Rate Limiting
builder.Services.AddMemoryCache();
builder.Services.Configure<IpRateLimitOptions>(builder.Configuration.GetSection("IpRateLimiting"));
builder.Services.AddInMemoryRateLimiting();
builder.Services.AddSingleton<IRateLimitConfiguration, RateLimitConfiguration>();

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
builder.Services.AddScoped<IAuthenticationService, DatabaseAuthenticationService>();
builder.Services.AddScoped<FileSyncService>();
builder.Services.AddScoped<BackupService>();
builder.Services.AddScoped<IFileVersioningService, FileVersioningService>();
builder.Services.AddScoped<ThumbnailService>();

// Register tenant-aware services
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITenantContextProvider, HttpTenantContextProvider>();
builder.Services.AddScoped<ITenantStorageService, TenantMinioStorageService>();
builder.Services.AddScoped<TenantOpenSearchIndexingService>();
builder.Services.AddScoped<IQuotaService, QuotaService>();
builder.Services.Configure<TenantBucketConfiguration>(builder.Configuration.GetSection("TenantBuckets"));
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
builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<AuthConfiguration>>().Value);
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

// Add health checks
builder.Services.AddHealthChecks()
    .AddNpgSql(
        builder.Configuration.GetConnectionString("DefaultConnection") ?? "Host=localhost;Database=silo;Username=postgres;Password=postgres",
        name: "database",
        tags: new[] { "db", "sql", "postgres" })
    .AddRedis(
        builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379",
        name: "redis",
        tags: new[] { "cache", "redis" })
    .AddCheck<Silo.Api.HealthChecks.MinioHealthCheck>(
        "minio",
        tags: new[] { "storage", "minio" })
    .AddCheck<Silo.Api.HealthChecks.OpenSearchHealthCheck>(
        "opensearch",
        tags: new[] { "search", "opensearch" })
    .AddCheck<Silo.Api.HealthChecks.HangfireHealthCheck>(
        "hangfire",
        tags: new[] { "jobs", "hangfire" });

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    // app.UseSwagger();
    // app.UseSwaggerUI();
}

// Add Serilog request logging
app.UseSerilogRequestLogging(options =>
{
    options.MessageTemplate = "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms";
    options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
    {
        diagnosticContext.Set("RequestHost", httpContext.Request.Host.Value);
        diagnosticContext.Set("RequestScheme", httpContext.Request.Scheme);
        diagnosticContext.Set("UserAgent", httpContext.Request.Headers["User-Agent"].ToString());
        
        // Add tenant context if available
        var tenantClaim = httpContext.User.FindFirst("tenant_id");
        if (tenantClaim != null)
        {
            diagnosticContext.Set("TenantId", tenantClaim.Value);
        }
    };
});

// Add correlation ID middleware
app.UseCorrelationId();

app.UseHttpsRedirection();
app.UseCors();

// Use rate limiting
app.UseIpRateLimiting();

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

// Health check endpoints
app.MapHealthChecks("/health", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = _ => true,
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        var response = new
        {
            status = report.Status.ToString(),
            timestamp = DateTime.UtcNow,
            duration = report.TotalDuration,
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                description = e.Value.Description,
                duration = e.Value.Duration,
                tags = e.Value.Tags
            })
        };
        await context.Response.WriteAsJsonAsync(response);
    }
});

app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("db") || check.Tags.Contains("cache"),
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new { status = report.Status.ToString() });
    }
});

app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = _ => false // Only application liveness
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

    Log.Information("Silo File Management System started successfully");
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application failed to start");
    throw;
}
finally
{
    Log.Information("Shutting down Silo File Management System");
    Log.CloseAndFlush();
}

