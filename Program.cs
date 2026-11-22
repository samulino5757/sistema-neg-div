using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Negocia;

var builder = WebApplication.CreateBuilder(args);
var culture = CultureInfo.GetCultureInfo("pt-BR");
CultureInfo.DefaultThreadCurrentCulture = culture;
CultureInfo.DefaultThreadCurrentUICulture = culture;

var store = Path.Combine(builder.Environment.ContentRootPath, "App_Data");
Directory.CreateDirectory(store);
var connectionString = new SqliteConnectionStringBuilder
{
    DataSource = Path.Combine(store, "negocia.db"),
    ForeignKeys = true
}.ToString();
builder.Services.AddDbContext<NegociaDb>(options => options.UseSqlite(connectionString)
    .UseAsyncSeeding(async (context, _, cancellationToken) =>
    {
        var db = (NegociaDb)context;
        if (!await db.Clientes.AnyAsync(cancellationToken))
            await DemoData.Seed(db, Brasil.Hoje, cancellationToken);
    }));
builder.Services.AddControllersWithViews(options => options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()));

var app = builder.Build();
app.UseExceptionHandler(handler => handler.Run(async context =>
{
    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
    await context.Response.WriteAsync("O sistema encontrou um erro. Volte ao painel em / e tente novamente.");
}));
app.UseStaticFiles();
app.MapGet("/", (IWebHostEnvironment environment) =>
{
    var index = environment.WebRootFileProvider.GetFileInfo("index.html");
    return index.PhysicalPath is { } path
        ? Results.File(path, "text/html; charset=utf-8")
        : Results.Redirect("/Painel");
});
app.MapControllerRoute("painel", "Painel/{action=Index}/{id?}", new { controller = "Painel" });

await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<NegociaDb>();
    await db.Database.EnsureCreatedAsync();
}

await app.RunAsync();
