using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Serilog;
using Serilog.Events;

using Homecare.DAL;
using Homecare.DAL.Interfaces;
using Homecare.DAL.Repositories;

var builder = WebApplication.CreateBuilder(args);

// --- MVC ---
builder.Services.AddControllersWithViews();

// --- DbContext (SQLite) ---
builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseSqlite(builder.Configuration["ConnectionStrings:HomecareDbConnection"]);
});

// --- Identity (Default UI + Roles) ---
builder.Services
    .AddDefaultIdentity<IdentityUser>(/* default options (dev) */)
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<AppDbContext>();

// Sadece [Authorize] istenen yerler login ister (Home/Index serbest)
builder.Services.AddAuthorization(o => o.FallbackPolicy = null);

// --- Repositories (DI) ---
builder.Services.AddScoped<IAvailableSlotRepository, AvailableSlotRepository>();
builder.Services.AddScoped<IAppointmentRepository, AppointmentRepository>();
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<ICareTaskRepository, CareTaskRepository>();

// --- Razor Pages (Identity UI) + Session ---
builder.Services.AddRazorPages();
builder.Services.AddSession();
// Eğer özel ayar gerekirse (opsiyonel):
// builder.Services.AddSession(options => {
//     options.Cookie.Name = ".Homecare.Session";
//     options.IdleTimeout = TimeSpan.FromMinutes(30);
//     options.Cookie.IsEssential = true;
// });

// --- Serilog (hocanın filtresi ile) ---
var loggerConfiguration = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.File($"Logs/app_{DateTime.Now:yyyyMMdd_HHmmss}.log");

loggerConfiguration.Filter.ByExcluding(e =>
    e.Properties.TryGetValue("SourceContext", out var _)
    && e.Level == LogEventLevel.Information
    && e.MessageTemplate.Text.Contains("Executed DbCommand"));

var logger = loggerConfiguration.CreateLogger();
builder.Logging.AddSerilog(logger);

var app = builder.Build();

// --- Dev seeding ---
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
    DBInit.Seed(app);
}

app.UseStaticFiles();
app.UseSession();

app.UseAuthentication();
app.UseAuthorization();

// --- Routes (hocanın gibi) ---
app.MapDefaultControllerRoute();  // /{controller=Home}/{action=Index}/{id?}
app.MapRazorPages();              // Identity UI

app.Run();
