using Microsoft.EntityFrameworkCore;
using Homecare.DAL;
using Homecare.DAL.Interfaces;
using Homecare.DAL.Repositories;
using Serilog;
using Serilog.Events;
using Microsoft.AspNetCore.Identity;

var builder = WebApplication.CreateBuilder(args);

// MVC + Razor Pages (Identity UI uses Razor Pages)
builder.Services.AddControllersWithViews();
builder.Services.AddRazorPages(); // student note: Identity scaffolding uses Razor Pages

// -------------------- Single DbContext via appsettings.json --------------------
// student note: one context for both domain and Identity tables
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("HomecareDbConnection")));

// Identity uses AppDbContext now
builder.Services
    .AddDefaultIdentity<IdentityUser>(options =>
    {
        // student note: easier sign-in during development; enable confirmation in production
        options.SignIn.RequireConfirmedAccount = false;
    })
    .AddEntityFrameworkStores<AppDbContext>(); // <- keep this semicolon!
// -----------------------------------------------------------------------------

// Repositories
builder.Services.AddScoped<IAvailableSlotRepository, AvailableSlotRepository>();
builder.Services.AddScoped<IAppointmentRepository, AppointmentRepository>();
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<ICareTaskRepository, CareTaskRepository>();

// -------------------- Serilog (filter noisy EF Core SQL) ----------------------
var logger = new LoggerConfiguration()
    .MinimumLevel.Information() // student note: good signal/noise during development
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .Filter.ByExcluding(e =>
        e.Properties.TryGetValue("SourceContext", out _)
        && e.Level == LogEventLevel.Information
        && e.MessageTemplate.Text.Contains("Executed DbCommand")) // cut EF Core spam
    .WriteTo.File($"Logs/app_{DateTime.Now:yyyyMMdd_HHmmss}.log")
    .CreateLogger();

builder.Logging.ClearProviders();
builder.Logging.AddSerilog(logger);
// -----------------------------------------------------------------------------

var app = builder.Build();

// Error handling + seeding
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
    DBInit.Seed(app); // student note: creates DB if missing + inserts demo data
}
else
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseAuthentication(); // student note: required for login
app.UseAuthorization();

app.MapDefaultControllerRoute();
app.MapRazorPages(); // student note: Identity endpoints

app.Run();
