using hotjob.Components;
using hotjob.Data;
using hotjob.Models.Users;
using hotjob.Repositories.User;
using hotjob.Services.Account;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

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
builder.Services.AddAuthorization();
builder.Services.Configure<SeedOptions>(
    builder.Configuration.GetSection(SeedOptions.SectionName));

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Missing ConnectionStrings:DefaultConnection.");
var serverVersion = new MySqlServerVersion(new Version(8, 0, 36));
builder.Services.AddDbContext<ApplicationDbContext>(o =>
    o.UseMySql(connectionString, serverVersion));
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
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IAccountService, AccountService>();

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

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapControllers();
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
