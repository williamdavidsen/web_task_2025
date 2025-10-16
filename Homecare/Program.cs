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
builder.Services.AddRazorPages();

// Single DbContext (domain + Identity)
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("HomecareDbConnection")));

// Identity + Roles (dev-friendly password rules)
builder.Services
    .AddDefaultIdentity<IdentityUser>(opt =>
    {
        opt.SignIn.RequireConfirmedAccount = false;
        opt.Password.RequireLowercase = false;

        opt.Password.RequireNonAlphanumeric = false;
        opt.Password.RequireUppercase = false;
        opt.Password.RequireDigit = false;
        opt.Password.RequiredLength = 3;
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<AppDbContext>();

builder.Services.AddSession();

// Repositories (DI)
builder.Services.AddScoped<IAvailableSlotRepository, AvailableSlotRepository>();
builder.Services.AddScoped<IAppointmentRepository, AppointmentRepository>();
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<ICareTaskRepository, CareTaskRepository>();

// Serilog (filter EF Core SQL noise)
var logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .Filter.ByExcluding(e =>
        e.Properties.TryGetValue("SourceContext", out _)
        && e.Level == LogEventLevel.Information
        && e.MessageTemplate.Text.Contains("Executed DbCommand"))
    .WriteTo.File($"Logs/app_{DateTime.Now:yyyyMMdd_HHmmss}.log")
    .CreateLogger();

builder.Logging.ClearProviders();
builder.Logging.AddSerilog(logger);

var app = builder.Build();

// Dev: detailed errors + seed sample data
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

app.UseHttpsRedirection();
app.UseStaticFiles();

// Auth pipeline
app.UseAuthentication();
app.UseAuthorization();
app.UseSession();

// Endpoints
app.MapDefaultControllerRoute();
app.MapRazorPages(); // Identity endpoints

app.Run();
