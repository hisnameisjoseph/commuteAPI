using CommuteApi.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Every form post must carry an antiforgery token; no action can forget [ValidateAntiForgeryToken].
builder.Services.AddControllersWithViews(options =>
    options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()));

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<TimestampInterceptor>();

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException(
        "ConnectionStrings:Default is not set. Locally, set it with dotnet user-secrets (see README).");

builder.Services.AddDbContext<AppDbContext>((services, options) => options
    .UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure())
    .AddInterceptors(services.GetRequiredService<TimestampInterceptor>()));

builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>("database");

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseAuthorization();

app.MapStaticAssets();

// Anonymous by design: Azure App Service health probes and uptime checks call it.
app.MapHealthChecks("/health");

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();

// Lets WebApplicationFactory<Program> in the test project reach the entry point.
public partial class Program;
