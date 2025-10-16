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

        // Entry point: show only login when anonymous, otherwise jump to role landing
        public IActionResult Index()
        {
            if (!User.Identity?.IsAuthenticated ?? true)
            {
                // Identity Razor Page
                return RedirectToPage("/Account/Login", new { area = "Identity" });
            }

            return RedirectToAction(nameof(AfterLogin));
        }

        // Decide where to send the user after a successful login
        [Authorize]
        public async Task<IActionResult> AfterLogin()
        {
            try
            {
                // Admin goes to global dashboard
                if (User.IsInRole("Admin"))
                    return RedirectToAction("Dashboard", "Admin");

                var email = User.Identity?.Name ?? string.Empty;

                // Client: map Identity email -> domain client id
                if (User.IsInRole("Client"))
                {
                    var clients = await _userRepo.GetByRoleAsync(UserRole.Client);
                    var me = clients.FirstOrDefault(c =>
                        string.Equals(c.Email, email, StringComparison.OrdinalIgnoreCase));

                    if (me != null)
                        return RedirectToAction("Dashboard", "Client", new { clientId = me.UserId });

                    // fallback if mapping fails
                    TempData["Error"] = "Client profile not found.";
                    return RedirectToPage("/Account/Logout", new { area = "Identity" });
                }

                // Personnel: map Identity email -> domain personnel id
                if (User.IsInRole("Personnel"))
                {
                    var personnels = await _userRepo.GetByRoleAsync(UserRole.Personnel);
                    var me = personnels.FirstOrDefault(p =>
                        string.Equals(p.Email, email, StringComparison.OrdinalIgnoreCase));

                    if (me != null)
                        return RedirectToAction("Dashboard", "Personnel", new { personnelId = me.UserId });

                    TempData["Error"] = "Personnel profile not found.";
                    return RedirectToPage("/Account/Logout", new { area = "Identity" });
                }

                // Unknown role → safest is login
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
    }
}
