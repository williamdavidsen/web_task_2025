using Homecare.DAL.Interfaces;
using Homecare.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Homecare.Controllers
{
    public class HomeController : Controller
    {
        private readonly IUserRepository _userRepo;
        private readonly ILogger<HomeController> _logger;

        public HomeController(IUserRepository userRepo, ILogger<HomeController> logger)
        {
            _userRepo = userRepo;
            _logger = logger;
        }

        // Public landing page – no login required
        [AllowAnonymous]
        public IActionResult Index()
        {
            // just render Views/Home/Index.cshtml
            return View();
        }

        // After successful login, route user to the right dashboard
        [Authorize]
        public async Task<IActionResult> AfterLogin()
        {
            try
            {
                if (User.IsInRole("Admin"))
                    return RedirectToAction("Dashboard", "Admin");

                var email = User.Identity?.Name ?? string.Empty;

                if (User.IsInRole("Client"))
                {
                    var me = (await _userRepo.GetByRoleAsync(UserRole.Client))
                        .FirstOrDefault(c => string.Equals(c.Email, email, StringComparison.OrdinalIgnoreCase));
                    if (me != null) return RedirectToAction("Dashboard", "Client", new { clientId = me.UserId });
                    TempData["Error"] = "Client profile not found.";
                    return RedirectToPage("/Account/Logout", new { area = "Identity" });
                }

                if (User.IsInRole("Personnel"))
                {
                    var me = (await _userRepo.GetByRoleAsync(UserRole.Personnel))
                        .FirstOrDefault(p => string.Equals(p.Email, email, StringComparison.OrdinalIgnoreCase));
                    if (me != null) return RedirectToAction("Dashboard", "Personnel", new { personnelId = me.UserId });
                    TempData["Error"] = "Personnel profile not found.";
                    return RedirectToPage("/Account/Logout", new { area = "Identity" });
                }

                TempData["Error"] = "Unauthorized role.";
                return RedirectToPage("/Account/Login", new { area = "Identity" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[HomeController] AfterLogin failed");
                TempData["Error"] = "Could not route to dashboard.";
                return RedirectToPage("/Account/Login", new { area = "Identity" });
            }
        }

        [AllowAnonymous]
        public IActionResult Ping() => Content("pong");
    }
}
