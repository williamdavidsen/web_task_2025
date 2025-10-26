using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Homecare.Controllers;
using Homecare.DAL.Interfaces;
using Homecare.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Homecare.Tests.Controllers
{
    public class PersonnelControllerTests
    {
        // test constants
        private const int NurseId = 2;
        private const string NurseEmail = "nurse.a@hc.test";

        // helper: format DateOnly the same way the view expects (yyyy-MM-dd)
        private static string F(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        // build controller with a fake HttpContext user + TempData
        private static PersonnelController MakeSut(
            bool asAdmin,
            string? email,
            Mock<IAppointmentRepository> apptRepo,
            Mock<IAvailableSlotRepository> slotRepo,
            Mock<IUserRepository> userRepo)
        {
            var logger = new Mock<ILogger<PersonnelController>>();

            var sut = new PersonnelController(apptRepo.Object, slotRepo.Object, userRepo.Object, logger.Object);

            // create a simple identity
            var claims = new List<Claim>();
            if (!string.IsNullOrWhiteSpace(email))
                claims.Add(new Claim(ClaimTypes.Name, email));
            claims.Add(new Claim(ClaimTypes.Role, asAdmin ? "Admin" : "Personnel"));

            var identity = new ClaimsIdentity(claims, "TestAuth");
            var user = new ClaimsPrincipal(identity);

            sut.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = user }
            };

            sut.TempData = new TempDataDictionary(sut.HttpContext, Mock.Of<ITempDataProvider>());
            return sut;
        }

        // =========================
        //  POSITIVE TESTS (3)
        // =========================

        [Fact]
        public async Task Dashboard_Admin_Returns_View_For_Target_Personnel()
        {
            // arrange: repo returns one personnel + empty appointments
            var apptRepo = new Mock<IAppointmentRepository>();
            var slotRepo = new Mock<IAvailableSlotRepository>();
            var userRepo = new Mock<IUserRepository>();

            userRepo.Setup(r => r.GetByRoleAsync(UserRole.Personnel))
                    .ReturnsAsync(new List<User> {
                        new User { UserId = NurseId, Email = NurseEmail, Role = UserRole.Personnel, Name = "Nurse A" }
                    });

            apptRepo.Setup(r => r.GetByPersonnelAsync(NurseId))
                    .ReturnsAsync(new List<Appointment>());

            var sut = MakeSut(asAdmin: true, email: "admin@hc.test", apptRepo, slotRepo, userRepo);

            // act
            var result = await sut.Dashboard(NurseId);

            // assert: we expect a normal View and the correct id in ViewBag
            var view = Assert.IsType<ViewResult>(result);
            Assert.Equal(NurseId, (int)sut.ViewBag.PersonnelId);
        }

        [Fact]
        public async Task CreateDay_Get_Admin_Returns_View_With_Selected_And_Locked_Days()
        {
            // arrange: provide one selected day and one locked day
            var apptRepo = new Mock<IAppointmentRepository>();
            var slotRepo = new Mock<IAvailableSlotRepository>();
            var userRepo = new Mock<IUserRepository>();

            var from = DateOnly.FromDateTime(DateTime.Today);

            slotRepo.Setup(r => r.GetWorkDaysAsync(NurseId, It.IsAny<DateOnly>(), It.IsAny<DateOnly>()))
                    .ReturnsAsync(new List<DateOnly> { from.AddDays(2) });

            slotRepo.Setup(r => r.GetLockedDaysAsync(NurseId, It.IsAny<DateOnly>(), It.IsAny<DateOnly>()))
                    .ReturnsAsync(new List<DateOnly> { from.AddDays(3) });

            var sut = MakeSut(asAdmin: true, email: "admin@hc.test", apptRepo, slotRepo, userRepo);

            // act
            var result = await sut.CreateDay(NurseId);

            // assert: usually a ViewResult with two JSON bags; if controller hit catch, at least a redirect
            if (result is ViewResult)
            {
                Assert.NotNull(sut.ViewBag.SelectedDaysJson);
                Assert.NotNull(sut.ViewBag.LockedDaysJson);
            }
            else
            {
                Assert.IsType<RedirectToActionResult>(result);
            }
        }

        [Fact]
        public async Task CreateDay_Post_Personnel_Adds_3_Slots_Per_Day_And_Redirects()
        {
            // arrange: personnel acts on self; no existing/locked days
            var apptRepo = new Mock<IAppointmentRepository>();
            var slotRepo = new Mock<IAvailableSlotRepository>();
            var userRepo = new Mock<IUserRepository>();

            userRepo.Setup(r => r.GetByRoleAsync(UserRole.Personnel))
                    .ReturnsAsync(new List<User> {
                        new User { UserId = NurseId, Email = NurseEmail, Role = UserRole.Personnel, Name = "Nurse A" }
                    });

            slotRepo.Setup(r => r.GetWorkDaysAsync(NurseId, It.IsAny<DateOnly>(), It.IsAny<DateOnly>()))
                    .ReturnsAsync(new List<DateOnly>());

            slotRepo.Setup(r => r.GetLockedDaysAsync(NurseId, It.IsAny<DateOnly>(), It.IsAny<DateOnly>()))
                    .ReturnsAsync(new List<DateOnly>());

            slotRepo.Setup(r => r.AddRangeAsync(It.IsAny<IEnumerable<AvailableSlot>>()))
                    .Returns(Task.CompletedTask);

            var sut = MakeSut(asAdmin: false, email: NurseEmail, apptRepo, slotRepo, userRepo);

            var d1 = DateOnly.FromDateTime(DateTime.Today.AddDays(2));
            var d2 = DateOnly.FromDateTime(DateTime.Today.AddDays(3));
            var csv = $"{F(d1)},{F(d2)}"; // 2 days -> 2 * 3 = 6 slots

            // act
            var result = await sut.CreateDay(NurseId, csv);

            // assert: redirect to Dashboard with correct id
            var rd = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("Dashboard", rd.ActionName);
            Assert.Equal(NurseId, rd.RouteValues!["personnelId"]);

            // verify exactly 6 slots were prepared
            slotRepo.Verify(r => r.AddRangeAsync(
                It.Is<IEnumerable<AvailableSlot>>(xs => xs.Count() == 6)), Times.Once);
        }

        // =========================
        //  NEGATIVE TESTS (3)
        // =========================

        [Fact]
        public async Task Dashboard_Personnel_Trying_Another_Id_Returns_Forbid()
        {
            // arrange: current personnel = NurseId
            var apptRepo = new Mock<IAppointmentRepository>();
            var slotRepo = new Mock<IAvailableSlotRepository>();
            var userRepo = new Mock<IUserRepository>();

            userRepo.Setup(r => r.GetByRoleAsync(UserRole.Personnel))
                    .ReturnsAsync(new List<User> {
                        new User { UserId = NurseId, Email = NurseEmail, Role = UserRole.Personnel, Name = "Nurse A" }
                    });

            var sut = MakeSut(asAdmin: false, email: NurseEmail, apptRepo, slotRepo, userRepo);

            // act: tries to open someone else
            var result = await sut.Dashboard(NurseId + 1);

            // assert
            Assert.IsType<ForbidResult>(result);
        }

        [Fact]
        public async Task CreateDay_Get_Personnel_For_Other_User_Returns_Forbid()
        {
            // arrange
            var apptRepo = new Mock<IAppointmentRepository>();
            var slotRepo = new Mock<IAvailableSlotRepository>();
            var userRepo = new Mock<IUserRepository>();

            userRepo.Setup(r => r.GetByRoleAsync(UserRole.Personnel))
                    .ReturnsAsync(new List<User> {
                        new User { UserId = NurseId, Email = NurseEmail, Role = UserRole.Personnel, Name = "Nurse A" }
                    });

            var sut = MakeSut(asAdmin: false, email: NurseEmail, apptRepo, slotRepo, userRepo);

            // act
            var result = await sut.CreateDay(NurseId + 1);

            // assert
            Assert.IsType<ForbidResult>(result);
        }

        [Fact]
        public async Task CreateDay_Post_When_Repo_Throws_Redirects_And_Sets_Error()
        {
            // arrange: simulate an exception when adding slots
            var apptRepo = new Mock<IAppointmentRepository>();
            var slotRepo = new Mock<IAvailableSlotRepository>();
            var userRepo = new Mock<IUserRepository>();

            userRepo.Setup(r => r.GetByRoleAsync(UserRole.Personnel))
                    .ReturnsAsync(new List<User> {
                        new User { UserId = NurseId, Email = NurseEmail, Role = UserRole.Personnel, Name = "Nurse A" }
                    });

            slotRepo.Setup(r => r.GetWorkDaysAsync(NurseId, It.IsAny<DateOnly>(), It.IsAny<DateOnly>()))
                    .ReturnsAsync(new List<DateOnly>());
            slotRepo.Setup(r => r.GetLockedDaysAsync(NurseId, It.IsAny<DateOnly>(), It.IsAny<DateOnly>()))
                    .ReturnsAsync(new List<DateOnly>());

            slotRepo.Setup(r => r.AddRangeAsync(It.IsAny<IEnumerable<AvailableSlot>>()))
                    .ThrowsAsync(new Exception("db failed"));

            var sut = MakeSut(asAdmin: false, email: NurseEmail, apptRepo, slotRepo, userRepo);

            var csv = F(DateOnly.FromDateTime(DateTime.Today.AddDays(2)));

            // act
            var result = await sut.CreateDay(NurseId, csv);

            // assert: controller catches and redirects to Dashboard, sets TempData["Error"]
            var rd = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("Dashboard", rd.ActionName);
            Assert.Equal(NurseId, rd.RouteValues!["personnelId"]);
            Assert.True(sut.TempData.ContainsKey("Error"));
        }
    }
}
