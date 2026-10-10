using System.Text;
using hotjob.Components;
using hotjob.Data;
using hotjob.Models.Users;
using hotjob.Repositories.Company;
using hotjob.Repositories.Employer;
using hotjob.Repositories.Job;
using hotjob.Services.Company;
using hotjob.Services.Employer;
using hotjob.Services.Job;
using hotjob.Services.Account;
using hotjob.Services.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

var envLocalPath = Path.Combine(builder.Environment.ContentRootPath, ".env.local");
if (!File.Exists(envLocalPath))
    envLocalPath = Path.Combine(Directory.GetCurrentDirectory(), ".env.local");
if (!File.Exists(envLocalPath))
    envLocalPath = Path.Combine(Directory.GetCurrentDirectory(), "hotjob", ".env.local");
if (File.Exists(envLocalPath))
{
    var dotEnv = File.ReadAllLines(envLocalPath)
        .Select(l => l.Trim())
        .Where(l => l.Length > 0 && !l.StartsWith('#'))
        .Select(l => l.StartsWith("export ") ? l[7..].TrimStart() : l)
        .Select(l => l.Split('=', 2))
        .Where(p => p.Length == 2 && !string.IsNullOrWhiteSpace(p[0]))
        .ToDictionary(p => p[0].Trim().Replace("__", ":"), p => p[1].Trim().Trim('"', '\''), StringComparer.OrdinalIgnoreCase);
    builder.Configuration.AddInMemoryCollection(dotEnv!);
}

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddControllers()
    .ConfigureApiBehaviorOptions(options =>
    {
        options.InvalidModelStateResponseFactory = context =>
            new BadRequestObjectResult(context.ModelState.Values
                .SelectMany(entry => entry.Errors)
                .Select(error => string.IsNullOrWhiteSpace(error.ErrorMessage)
                    ? "The request is invalid."
                    : error.ErrorMessage)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray());
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "HotJob API",
        Version = "v1",
        Description = "Job Board + ATS backend: auth (2.1), employer job management (2.2), employer ATS (2.3)."
    });
    options.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Description = "Paste the access token from POST /api/auth/login. Example: Bearer eyJ..."
    });
    options.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
    var xml = Path.Combine(AppContext.BaseDirectory, $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml");
    if (File.Exists(xml))
    {
        options.IncludeXmlComments(xml, includeControllerXmlComments: true);
    }
});
builder.Services.AddAuthorization();
builder.Services.AddAuthorizationCore();
builder.Services.AddCascadingAuthenticationState();
builder.Services.Configure<SeedOptions>(
    builder.Configuration.GetSection(SeedOptions.SectionName));
builder.Services.AddOptions<JwtOptions>()
    .Bind(builder.Configuration.GetSection(JwtOptions.SectionName))
    .ValidateDataAnnotations()
    .Validate(options => !string.IsNullOrEmpty(options.SecretKey) && options.SecretKey.Length >= 32,
        "Jwt:SecretKey must be at least 32 characters.")
    .ValidateOnStart();
builder.Services.AddSingleton(TimeProvider.System);

var jwtSection = builder.Configuration.GetSection(JwtOptions.SectionName);
var jwtSecret = jwtSection["SecretKey"]
    ?? throw new InvalidOperationException("Missing Jwt:SecretKey.");
var jwtIssuer = jwtSection["Issuer"];
var jwtAudience = jwtSection["Audience"];
builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
    {
        options.User.RequireUniqueEmail = true;
        options.Password.RequiredLength = 6;
        options.Password.RequireDigit = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireNonAlphanumeric = true;
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();
// NB: AddIdentity registers cookie schemes as the default; JWT must be wired
// AFTER it so Bearer stays the default challenge for [Authorize] API endpoints.
builder.Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            ValidateIssuer = true,
            ValidIssuer = jwtIssuer,
            ValidateAudience = true,
            ValidAudience = jwtAudience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    });

var corsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options =>
    options.AddPolicy("Frontend", policy => policy
        .WithOrigins(corsOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials()));

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Missing ConnectionStrings:DefaultConnection.");
var serverVersion = new MySqlServerVersion(new Version(8, 0, 36));
builder.Services.AddDbContext<ApplicationDbContext>(o =>
    o.UseMySql(connectionString, serverVersion));
builder.Services.AddScoped<IJobPostingRepository, JobPostingRepository>();
builder.Services.AddScoped<IApplicantRepository, ApplicantRepository>();
builder.Services.AddScoped<ICompanyRepository, CompanyRepository>();
builder.Services.AddScoped<ICompanyService, CompanyService>();
builder.Services.AddScoped<IJobService, JobService>();
builder.Services.AddScoped<IApplicantService, ApplicantService>();
builder.Services.AddScoped<IInterviewService, InterviewService>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<IAccountService, AccountService>();
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<AuthSession>();
builder.Services.AddScoped<HotJobAuthStateProvider>();
builder.Services.AddScoped<AuthenticationStateProvider>(services =>
    services.GetRequiredService<HotJobAuthStateProvider>());
builder.Services.AddHttpClient(nameof(hotjob.Services.Api.ApiClientBase));
builder.Services.AddScoped<hotjob.Services.Api.EmployerJobsApiClient>();
builder.Services.AddScoped<hotjob.Services.Api.EmployerApplicantsApiClient>();
builder.Services.AddScoped<hotjob.Services.Api.EmployerInterviewsApiClient>();
builder.Services.AddScoped<hotjob.Services.Api.AccountApiClient>();
builder.Services.AddScoped<hotjob.Services.Api.CompanyApiClient>();
builder.Services.AddScoped<hotjob.Services.Employer.ICompanyService, hotjob.Services.Employer.CompanyService>();

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var services = scope.ServiceProvider;
    var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("StartupSeeding");
    var cancellationToken = app.Lifetime.ApplicationStopping;
    var db = services.GetRequiredService<ApplicationDbContext>();

    await db.Database.MigrateAsync(cancellationToken);

    await DatabaseSeeder.SeedAsync(
        db,
        logger,
        cancellationToken);
    await IdentitySeeder.SeedAsync(
        services.GetRequiredService<RoleManager<IdentityRole>>(),
        services.GetRequiredService<UserManager<ApplicationUser>>(),
            db,
        services.GetRequiredService<IOptions<SeedOptions>>().Value,
        logger,
        cancellationToken);
    await DemoSeeder.SeedAsync(
        db,
        services.GetRequiredService<UserManager<ApplicationUser>>(),
        services.GetRequiredService<RoleManager<IdentityRole>>(),
        services.GetRequiredService<IOptions<SeedOptions>>().Value,
        logger,
        cancellationToken);
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseWhen(
        context => !context.Request.Path.StartsWithSegments("/api"),
        branch => branch.UseExceptionHandler("/Error", createScopeForErrors: true));
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseWhen(
    context => !context.Request.Path.StartsWithSegments("/api"),
    branch => branch.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true));
app.UseHttpsRedirection();

app.UseCors("Frontend");
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapControllers();
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
