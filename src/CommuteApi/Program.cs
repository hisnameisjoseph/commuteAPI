var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();

// Phase 1 adds a database check (AddDbContextCheck) so /health covers SQL too.
builder.Services.AddHealthChecks();

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
