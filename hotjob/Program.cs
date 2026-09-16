using hotjob.Components;
using hotjob.Data;
using hotjob.Models.Users;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

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

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Missing ConnectionStrings:DefaultConnection.");
var serverVersion = new MySqlServerVersion(new Version(8, 0, 36));
builder.Services.AddDbContext<ApplicationDbContext>(o =>
    o.UseMySql(connectionString, serverVersion));
builder.Services.AddIdentity<ApplicationUser, IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
