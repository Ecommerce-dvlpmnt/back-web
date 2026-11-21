using System.Diagnostics;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using EcommerceBackend.Application.Interfaces;
using EcommerceBackend.Application.Security;
using EcommerceBackend.Application.Services;
using EcommerceBackend.Data;
using EcommerceBackend.Data.Repositories;
using EcommerceBackend.Domain.Enums;
using EcommerceBackend.Infrastructure.Logging;
using EcommerceBackend.Infrastructure.Storage;
using EcommerceBackend.Middleware;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Npgsql;
using Serilog;
using Serilog.Events;
using Swashbuckle.AspNetCore.SwaggerGen;

static RateLimitPartition<string> CreateFixedWindowPartition(HttpContext context, int permitLimit)
{
    var partitionKey = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
    {
        PermitLimit = permitLimit,
        Window = TimeSpan.FromMinutes(1),
        QueueLimit = 0,
        AutoReplenishment = true
    });
}

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

var builder = WebApplication.CreateBuilder(args);

var allowedOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>()
    ?.Where(origin => !string.IsNullOrWhiteSpace(origin))
    .ToArray() ?? [];
var defaultOrigins = new[] { "http://localhost:3000", "http://localhost:5173" };

builder.Host.UseSerilog((context, services, configuration) => configuration
    .MinimumLevel.Is(context.HostingEnvironment.IsDevelopment()
        ? LogEventLevel.Debug
        : LogEventLevel.Information)
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Command", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .Enrich.With<SensitiveDataMaskingEnricher>()
    .WriteTo.Console()
    .WriteTo.File(
        path: "logs/app-.log",
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 14));

var connectionString = DatabaseConnectionHelper.ResolveConnectionString(builder.Configuration);

var nameTranslator = new ExactMatchNameTranslator();
var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
dataSourceBuilder.MapEnum<PaymentMethod>("payment_method", nameTranslator);
dataSourceBuilder.MapEnum<OrderStatus>("order_status", nameTranslator);
dataSourceBuilder.MapEnum<PaymentStatus>("payment_status", nameTranslator);
dataSourceBuilder.MapEnum<ShipmentStatus>("shipment_status", nameTranslator);
dataSourceBuilder.MapEnum<ReviewStatus>("review_status", nameTranslator);
var dataSource = dataSourceBuilder.Build();

builder.Services.AddSingleton<NpgsqlDataSource>(_ => dataSource);
builder.Services.AddCors(options => options.AddPolicy("Frontend", policy =>
{
    if (allowedOrigins.Length > 0)
    {
        policy.WithOrigins(allowedOrigins);
    }
    else if (builder.Environment.IsDevelopment())
    {
        policy.AllowAnyOrigin();
    }
    else
    {
        policy.WithOrigins(defaultOrigins);
    }

    policy.AllowAnyMethod().AllowAnyHeader();
}));

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy("auth-limit", context => CreateFixedWindowPartition(context, permitLimit: 5));
    options.AddPolicy("order-limit", context => CreateFixedWindowPartition(context, permitLimit: 10));

    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;

        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            context.HttpContext.Response.Headers["Retry-After"] =
                ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString();
        }

        await context.HttpContext.Response.WriteAsJsonAsync(
            new ErrorResponse(
                StatusCodes.Status429TooManyRequests,
                "Занадто багато запитів. Спробуйте пізніше.",
                Activity.Current?.TraceId.ToString() ?? context.HttpContext.TraceIdentifier),
            cancellationToken);
    };
});

// JWT Authentication
var jwtSecret = builder.Configuration["Jwt:Secret"]
    ?? builder.Configuration["JWT_SECRET"];

if (string.IsNullOrWhiteSpace(jwtSecret) || jwtSecret.Length < 32)
{
    if (builder.Environment.IsDevelopment())
    {
        jwtSecret = "DevelopmentOnlyInsecureKeyMustBeReplacedInProductionMin32Chars!";
        Log.Warning("SECURITY WARNING: Using development fallback JWT key. Set 'Jwt:Secret' or 'Jwt__Secret' / 'JWT_SECRET' before deploying to production.");
    }
    else
    {
        throw new InvalidOperationException(
            "CRITICAL SECURITY FAILURE: 'Jwt:Secret' is not configured or is shorter than 32 characters. " +
            "Set the 'Jwt:Secret' configuration or the 'Jwt__Secret' / 'JWT_SECRET' environment variable.");
    }
}
var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "EcommerceBackend";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "EcommerceAdmin";

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
        ValidIssuer = jwtIssuer,
        ValidAudience = jwtAudience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
        ClockSkew = TimeSpan.Zero
    };
});

builder.Services.AddAuthorization();

builder.Services.AddControllers()
    .AddJsonOptions(options =>
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddEndpointsApiExplorer();

builder.Services.AddDbContext<AppDbContext>((services, options) =>
    options.UseNpgsql(
        services.GetRequiredService<NpgsqlDataSource>(),
        o =>
        {
            o.MapEnum<PaymentMethod>("payment_method", nameTranslator: nameTranslator);
            o.MapEnum<OrderStatus>("order_status", nameTranslator: nameTranslator);
            o.MapEnum<PaymentStatus>("payment_status", nameTranslator: nameTranslator);
            o.MapEnum<ShipmentStatus>("shipment_status", nameTranslator: nameTranslator);
            o.MapEnum<ReviewStatus>("review_status", nameTranslator: nameTranslator);
        }));

// Repositories & Domain Services
builder.Services.AddMemoryCache();
builder.Services.AddHttpContextAccessor();
builder.Services.AddHttpClient<ICurrencyRateService, NbuCurrencyRateService>();
builder.Services.AddHttpClient<INovaPoshtaClient, NovaPoshtaClient>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(60);
});
builder.Services.AddScoped<IProductRepository, ProductRepository>();
builder.Services.AddScoped<IProductService, ProductService>();
builder.Services.AddScoped<ICategoryRepository, CategoryRepository>();
builder.Services.AddScoped<ICategoryService, CategoryService>();
builder.Services.AddScoped<ISupplierRepository, SupplierRepository>();
builder.Services.AddScoped<ISupplierService, SupplierService>();
builder.Services.AddScoped<IReviewRepository, ReviewRepository>();
builder.Services.AddScoped<IReviewService, ReviewService>();
builder.Services.AddScoped<IOrderService, OrderService>();
builder.Services.AddScoped<IAuditLogService, AuditLogService>();
builder.Services.AddScoped<IFinanceService, FinanceService>();
builder.Services.AddScoped<IAnalyticsService, AnalyticsService>();
builder.Services.AddSingleton<IImageStorageService, LocalImageStorageService>();

// Admin & Security Services
builder.Services.AddSingleton<IPasswordHasher, PasswordHasher>();
builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();
builder.Services.AddScoped<IAdminAuthService, AdminAuthService>();

// Swagger with JWT Bearer support
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Ecommerce Backend API",
        Version = "v1"
    });

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Введите JWT токен (без слова Bearer, Swagger добавит его сам):"
    });

    options.OperationFilter<AuthorizeCheckOperationFilter>();
});

// Validation error formatting
builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        var errors = context.ModelState
            .Where(entry => entry.Value?.Errors.Count > 0)
            .ToDictionary(
                entry => entry.Key,
                entry => entry.Value!.Errors.Select(error => error.ErrorMessage).ToArray());

        return new BadRequestObjectResult(new
        {
            code = StatusCodes.Status400BadRequest,
            message = "Validation failed.",
            errors,
            traceId = Activity.Current?.TraceId.ToString() ?? context.HttpContext.TraceIdentifier
        });
    };
});

var app = builder.Build();

app.UseMiddleware<GlobalExceptionHandlingMiddleware>();
app.UseSerilogRequestLogging(options =>
{
    options.MessageTemplate = "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms";
    options.GetLevel = (httpContext, _, ex) =>
    {
        if (ex != null || httpContext.Response.StatusCode >= 500)
            return LogEventLevel.Error;
        if (httpContext.Response.StatusCode >= 400)
            return LogEventLevel.Warning;
        return LogEventLevel.Information;
    };
});

var migrateOnStartup = app.Configuration.GetValue<bool>("Database:MigrateOnStartup");
if (migrateOnStartup)
{
    await using var migrationScope = app.Services.CreateAsyncScope();
    var db = migrationScope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
}

if (args.Contains("--seed-only") || app.Environment.IsDevelopment())
{
    using var seedScope = app.Services.CreateScope();
    var db = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();
    await DbSeeder.SeedAsync(db);

    if (args.Contains("--seed-only"))
    {
        Console.WriteLine("SEEDING_COMPLETED_SUCCESSFULLY");
        return;
    }
}

var swaggerEnabled = app.Environment.IsDevelopment() ||
                     app.Configuration.GetValue<bool>("Swagger:Enabled");

if (swaggerEnabled)
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "EcommerceBackend v1");
    });
}

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseStaticFiles();

app.UseRouting();
app.UseCors("Frontend");
app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }))
    .WithTags("Health")
    .WithName("HealthCheck");

if (swaggerEnabled)
{
    app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();
}

app.MapControllers();

app.Run();

/// <summary>
/// Preserves exact PascalCase names (e.g. "New", "CashOnDelivery") as defined in PostgreSQL enums,
/// preventing default snake_case translation.
/// </summary>
public sealed class ExactMatchNameTranslator : INpgsqlNameTranslator
{
    public string TranslateTypeName(string clrName) => clrName;
    public string TranslateMemberName(string clrName) => clrName;
}

/// <summary>
/// Отображает требование авторизации (замочек в Swagger) только для методов и контроллеров с атрибутом [Authorize].
/// Открытые методы витрины и методы с [AllowAnonymous] отображаются без замка.
/// </summary>
public sealed class AuthorizeCheckOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var declaringType = context.MethodInfo.DeclaringType;
        var hasAuthorize = (declaringType != null && declaringType.GetCustomAttributes(true).OfType<AuthorizeAttribute>().Any())
            || context.MethodInfo.GetCustomAttributes(true).OfType<AuthorizeAttribute>().Any()
            || context.ApiDescription.RelativePath?.StartsWith("api/admin", StringComparison.OrdinalIgnoreCase) == true;

        var hasAllowAnonymous = context.MethodInfo.GetCustomAttributes(true).OfType<AllowAnonymousAttribute>().Any()
            || (declaringType != null && declaringType.GetCustomAttributes(true).OfType<AllowAnonymousAttribute>().Any());

        if (hasAuthorize && !hasAllowAnonymous)
        {
            operation.Responses ??= new OpenApiResponses();
            operation.Responses.TryAdd(StatusCodes.Status401Unauthorized.ToString(), new OpenApiResponse { Description = "Unauthorized" });
            operation.Responses.TryAdd(StatusCodes.Status403Forbidden.ToString(), new OpenApiResponse { Description = "Forbidden" });

            operation.Security =
            [
                new OpenApiSecurityRequirement
                {
                    [new OpenApiSecuritySchemeReference("Bearer", context.Document)] = []
                }
            ];
        }
    }
}
