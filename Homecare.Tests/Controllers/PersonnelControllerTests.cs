// Homecare.Tests/Controllers/PersonnelControllerTests.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Homecare.Controllers;
using Homecare.DAL.Interfaces;
using Homecare.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Homecare.Tests.Controllers
{
        /// <summary>
        /// Very small, student-friendly test suite that matches the current PersonnelController.
        /// </summary>
        public class PersonnelControllerTests
        {
                // --- helpers ---------------------------------------------------------

                // Creates a fake ClaimsPrincipal with optional Admin/Personnel role
                private static ClaimsPrincipal MakeUser(string email, bool isAdmin, bool isPersonnel)
                {
                        var claims = new List<Claim> { new Claim(ClaimTypes.Name, email) };
                        if (isAdmin) claims.Add(new Claim(ClaimTypes.Role, "Admin"));
                        if (isPersonnel) claims.Add(new Claim(ClaimTypes.Role, "Personnel"));
                        var identity = new ClaimsIdentity(claims, "TestAuth");
                        return new ClaimsPrincipal(identity);
                }

                // Builds the SUT with common plumbing
                private static PersonnelController MakeSut(
                    Mock<IAppointmentRepository> apptRepo,
                    Mock<IAvailableSlotRepository> slotRepo,
                    Mock<IUserRepository> userRepo,
                    bool asAdmin = true,
                    string email = "nurse.a@hc.test")
                {
                        var logger = new Mock<ILogger<PersonnelController>>();

                        var sut = new PersonnelController(
                            apptRepo.Object, slotRepo.Object, userRepo.Object, logger.Object);

                        sut.ControllerContext = new ControllerContext
                        {
                                HttpContext = new DefaultHttpContext
                                {
                                        User = MakeUser(email, isAdmin: asAdmin, isPersonnel: !asAdmin)
                                }
                        };

                        return sut;
                }

                // Returns a minimal personnel-domain user list
                private static List<User> PersonnelList(params (int id, string email, string name)[] items)
                    => items.Select(x => new User { UserId = x.id, Email = x.email, Name = x.name, Role = UserRole.Personnel }).ToList();

                // --- positive tests --------------------------------------------------

                [Fact]
                public async Task Dashboard_WithId_Returns_View_And_Sets_ViewBag()
                {
                        // arrange: Admin can open any personnel's dashboard
                        var apptRepo = new Mock<IAppointmentRepository>();
                        var slotRepo = new Mock<IAvailableSlotRepository>();
                        var userRepo = new Mock<IUserRepository>();

                        // keep it simple: no appointments
                        apptRepo.Setup(r => r.GetByPersonnelAsync(2)).ReturnsAsync(new List<Appointment>());

                        // return at least one personnel so Admin fallback logic never throws
                        userRepo.Setup(r => r.GetByRoleAsync(UserRole.Personnel))
                                .ReturnsAsync(PersonnelList((2, "nurse.a@hc.test", "Nurse A")));

                        var sut = MakeSut(apptRepo, slotRepo, userRepo, asAdmin: true);

                        // act
                        var result = await sut.Dashboard(2);

                        // assert
                        var view = Assert.IsType<ViewResult>(result);
                        Assert.Equal(2, (int)view.ViewData["PersonnelId"]);
                }

                [Fact]
                public async Task CreateDay_Get_Returns_View_With_Selected_And_Locked_Days()
                {
                        // arrange
                        var apptRepo = new Mock<IAppointmentRepository>();
                        var slotRepo = new Mock<IAvailableSlotRepository>();
                        var userRepo = new Mock<IUserRepository>();

                        userRepo.Setup(r => r.GetAsync(2))
                                .ReturnsAsync(new User { UserId = 2, Name = "Nurse A", Role = UserRole.Personnel });

                        slotRepo.Setup(r => r.GetWorkDaysAsync(2, It.IsAny<DateOnly>(), It.IsAny<DateOnly>()))
                                .ReturnsAsync(new List<DateOnly> { DateOnly.FromDateTime(DateTime.Today.AddDays(1)) });

                        slotRepo.Setup(r => r.GetLockedDaysAsync(2, It.IsAny<DateOnly>(), It.IsAny<DateOnly>()))
                                .ReturnsAsync(new List<DateOnly>());

                        var sut = MakeSut(apptRepo, slotRepo, userRepo, asAdmin: true);

                        // act
                        var result = await sut.CreateDay(2);

                        // assert
                        var view = Assert.IsType<ViewResult>(result);
                        // JSON strings should be present on ViewBag (serialized in controller)
                        Assert.False(string.IsNullOrWhiteSpace(view.ViewData["SelectedDaysJson"]?.ToString()));
                        Assert.NotNull(view.ViewData["PersonnelId"]);
                }

                [Fact]
                public async Task CreateDay_Post_Adds_Preset_Slots_For_New_Days()
                {
                        // arrange
                        var apptRepo = new Mock<IAppointmentRepository>();
                        var slotRepo = new Mock<IAvailableSlotRepository>();
                        var userRepo = new Mock<IUserRepository>();

                        // existing work days: empty -> both days are new
                        slotRepo.Setup(r => r.GetWorkDaysAsync(2, It.IsAny<DateOnly>(), It.IsAny<DateOnly>()))
                                .ReturnsAsync(new List<DateOnly>());

                        // no locked days
                        slotRepo.Setup(r => r.GetLockedDaysAsync(2, It.IsAny<DateOnly>(), It.IsAny<DateOnly>()))
                                .ReturnsAsync(new List<DateOnly>());

                        List<AvailableSlot>? captured = null;
                        slotRepo.Setup(r => r.AddRangeAsync(It.IsAny<IEnumerable<AvailableSlot>>()))
                                .Callback<IEnumerable<AvailableSlot>>(xs => captured = xs?.ToList())
                                .Returns(Task.CompletedTask);

                        var sut = MakeSut(apptRepo, slotRepo, userRepo, asAdmin: true);

                        // 2 days selected -> 3 preset slots per day = 6 total
                        var chosen = $"{DateOnly.FromDateTime(DateTime.Today.AddDays(1)):yyyy-MM-dd}," +
                                     $"{DateOnly.FromDateTime(DateTime.Today.AddDays(2)):yyyy-MM-dd}";

                        // act
                        var result = await sut.CreateDay(personnelId: 2, days: chosen);

                        // assert
                        var rd = Assert.IsType<RedirectToActionResult>(result);
                        Assert.Equal(nameof(PersonnelController.Dashboard), rd.ActionName);
                        Assert.NotNull(captured);                 // was called
                        Assert.Equal(6, captured!.Count);         // 2 days * 3 slots
                        Assert.All(captured!, s => Assert.Equal(2, s.PersonnelId));
                }

                // --- negative tests --------------------------------------------------

                [Fact]
                public async Task Dashboard_WhenRepoThrows_Redirects_To_Home_Index()
                {
                        // arrange
                        var apptRepo = new Mock<IAppointmentRepository>();
                        var slotRepo = new Mock<IAvailableSlotRepository>();
                        var userRepo = new Mock<IUserRepository>();

                        // Admin branch used; ensure personnel list not empty
                        userRepo.Setup(r => r.GetByRoleAsync(UserRole.Personnel))
                                .ReturnsAsync(PersonnelList((2, "nurse.a@hc.test", "Nurse A")));

                        // force an exception inside try { ... }
                        apptRepo.Setup(r => r.GetByPersonnelAsync(2)).ThrowsAsync(new Exception("boom"));

                        var sut = MakeSut(apptRepo, slotRepo, userRepo, asAdmin: true);

                        // act
                        var result = await sut.Dashboard(2);

                        // assert: controller catches and redirects to Home/Index
                        var rd = Assert.IsType<RedirectToActionResult>(result);
                        Assert.Equal("Index", rd.ActionName);
                        Assert.Equal("Home", rd.ControllerName);
                }

                [Fact]
                public async Task Dashboard_NonAdmin_DifferentId_Forbid()
                {
                        // arrange: normal personnel logs in with id=5, tries to open id=7
                        var apptRepo = new Mock<IAppointmentRepository>();
                        var slotRepo = new Mock<IAvailableSlotRepository>();
                        var userRepo = new Mock<IUserRepository>();

                        // current user e-mail -> id=5
                        userRepo.Setup(r => r.GetByRoleAsync(UserRole.Personnel))
                                .ReturnsAsync(PersonnelList(
                                    (5, "nurse.me@hc.test", "Me"),
                                    (7, "nurse.other@hc.test", "Other")));

                        var sut = MakeSut(apptRepo, slotRepo, userRepo,
                                          asAdmin: false, email: "nurse.me@hc.test");

                        // act
                        var result = await sut.Dashboard(7);

                        // assert
                        Assert.IsType<ForbidResult>(result);
                }

                [Fact]
                public async Task CreateDay_Post_ForPersonnel_NotSelf_Forbid()
                {
                        // arrange: non-admin tries to edit another personnel's days
                        var apptRepo = new Mock<IAppointmentRepository>();
                        var slotRepo = new Mock<IAvailableSlotRepository>();
                        var userRepo = new Mock<IUserRepository>();

                        userRepo.Setup(r => r.GetByRoleAsync(UserRole.Personnel))
                                .ReturnsAsync(PersonnelList(
                                    (5, "nurse.me@hc.test", "Me"),
                                    (7, "nurse.other@hc.test", "Other")));

                        var sut = MakeSut(apptRepo, slotRepo, userRepo,
                                          asAdmin: false, email: "nurse.me@hc.test");

                        // act
                        var result = await sut.CreateDay(personnelId: 7, days: null);

                        // assert
                        Assert.IsType<ForbidResult>(result);
                }
        }
}
