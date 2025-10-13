using Microsoft.EntityFrameworkCore;
using Homecare.DAL;
using Homecare.DAL.Interfaces;
using Homecare.DAL.Repositories;
using Serilog;
using Serilog.Events;
using Homecare.Models;
using Microsoft.Extensions.Logging.Console;

var builder = WebApplication.CreateBuilder(args);

// MVC
builder.Services.AddControllersWithViews();

// SQLite dosya yolu (App_Data/homecare.db)
var dataDir = Path.Combine(builder.Environment.ContentRootPath, "App_Data");
Directory.CreateDirectory(dataDir);
var defaultSqlite = $"Data Source={Path.Combine(dataDir, "homecare.db")}";
var conn = builder.Configuration.GetConnectionString("AppDbContextConnection") ?? defaultSqlite;
Console.WriteLine($"[Homecare] SQLite -> {conn}");

// DbContext
builder.Services.AddDbContext<AppDbContext>(opt => opt.UseSqlite(conn));

// Repositories
builder.Services.AddScoped<IAvailableSlotRepository, AvailableSlotRepository>();
builder.Services.AddScoped<IAppointmentRepository, AppointmentRepository>();
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<ICareTaskRepository, CareTaskRepository>();

// ---------- Serilog: Sadece WARNING ve üzeri, dosyaya yaz ----------
var logger = new LoggerConfiguration()
    .MinimumLevel.Warning()                                      // INFO'lar gelmez
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)   // Framework logları da kısılsın
    .Filter.ByExcluding(e =>
        e.Properties.TryGetValue("SourceContext", out var _)
        && e.Level == LogEventLevel.Information
        && e.MessageTemplate.Text.Contains("Executed DbCommand")) // EF 'Executed DbCommand' filtre (zaten INFO ama dursun)
    .WriteTo.File($"Logs/app_{DateTime.Now:yyyyMMdd_HHmmss}.log") // konsola yazma, sadece dosya
    .CreateLogger();

builder.Logging.ClearProviders();   // default Console provider'ı kapat
builder.Logging.AddSerilog(logger); // Serilog'u bağla
builder.Logging.AddSimpleConsole();
builder.Logging.AddFilter<ConsoleLoggerProvider>("Microsoft.Hosting.Lifetime", LogLevel.Information);
builder.Logging.AddFilter<ConsoleLoggerProvider>("", LogLevel.None); // diğer kategorileri sustur
var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
    DBInit.Seed(app); // EnsureCreated + seed
}

app.UseStaticFiles();

// (auth ekleyince açarız)
// app.UseAuthentication();
app.UseAuthorization();

app.MapDefaultControllerRoute();

app.Run();
