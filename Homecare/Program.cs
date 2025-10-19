using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Serilog;
using Serilog.Events;

using Homecare.DAL;
using Homecare.DAL.Interfaces;
using Homecare.DAL.Repositories;

var builder = WebApplication.CreateBuilder(args);

// MVC controllers + views
builder.Services.AddControllersWithViews();

// EF Core (SQLite)
builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseSqlite(builder.Configuration["ConnectionStrings:HomecareDbConnection"]);
});

// Identity (Default UI) + Roles, using the same AppDbContext
builder.Services
    .AddDefaultIdentity<IdentityUser>()      // dev-friendly defaults
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<AppDbContext>();

// IMPORTANT: do not force auth globally (only actions with [Authorize] require login)
builder.Services.AddAuthorization(o => o.FallbackPolicy = null);

// Repositories (DI)
builder.Services.AddScoped<IAvailableSlotRepository, AvailableSlotRepository>();
builder.Services.AddScoped<IAppointmentRepository, AppointmentRepository>();
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<ICareTaskRepository, CareTaskRepository>();

// Razor Pages (for Identity UI) + Session
builder.Services.AddRazorPages();
builder.Services.AddSession();

// Serilog (filter out EF command noise)
var loggerConfiguration = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.File($"Logs/app_{DateTime.Now:yyyyMMdd_HHmmss}.log");

loggerConfiguration.Filter.ByExcluding(e =>
    e.Properties.TryGetValue("SourceContext", out var _)
    && e.Level == LogEventLevel.Information
    && e.MessageTemplate.Text.Contains("Executed DbCommand"));

var logger = loggerConfiguration.CreateLogger();
builder.Logging.ClearProviders();
builder.Logging.AddSerilog(logger);

var app = builder.Build();

// Dev-time: detailed errors + seed demo data
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
    DBInit.Seed(app);
}
else
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection(); // safe default (no-op on http in dev)
app.UseStaticFiles();
app.UseSession();

app.UseAuthentication();
app.UseAuthorization();

// Conventional MVC route + Identity Razor Pages
app.MapDefaultControllerRoute();   // /{controller=Home}/{action=Index}/{id?}
app.MapRazorPages();               // /Identity/...

app.Run();
