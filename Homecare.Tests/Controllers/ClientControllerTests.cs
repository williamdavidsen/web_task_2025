using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Homecare.Controllers;
using Homecare.DAL.Interfaces;
using Homecare.Models;
using Homecare.ViewModels;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Logging;   // <- important
using Moq;
using Xunit;

namespace Homecare.Tests.Controllers
{
    public class ClientControllerTests
    {
        // Helper: build controller with all mocks (logger included)
        private static ClientController MakeSut(
            Mock<IAppointmentRepository> apptRepo,
            Mock<IAvailableSlotRepository> slotRepo,
            Mock<IUserRepository> userRepo,
            Mock<ICareTaskRepository> taskRepo)
        {
            var logger = new Mock<ILogger<ClientController>>();
            var sut = new ClientController(
                apptRepo.Object, slotRepo.Object, userRepo.Object, taskRepo.Object, logger.Object);

            // TempData is used for messages
            sut.TempData = new TempDataDictionary(new DefaultHttpContext(), Mock.Of<ITempDataProvider>());
            return sut;
        }

        // ---------- POSITIVE TESTS ----------

        // P1: When data is valid -> we save and go to Dashboard
        [Fact]
        public async Task Create_Post_Valid_Redirects_To_Dashboard()
        {
            var apptRepo = new Mock<IAppointmentRepository>();
            var slotRepo = new Mock<IAvailableSlotRepository>();
            var userRepo = new Mock<IUserRepository>();
            var taskRepo = new Mock<ICareTaskRepository>();

            const int clientId = 12;
            const int slotId = 77;

            apptRepo.Setup(r => r.SlotIsBookedAsync(slotId, null)).ReturnsAsync(false);
            apptRepo.Setup(r => r.AddAsync(It.IsAny<Appointment>())).Returns(Task.CompletedTask);

            var sut = MakeSut(apptRepo, slotRepo, userRepo, taskRepo);

            var vm = new AppointmentCreateViewModel
            {
                Appointment = new Appointment
                {
                    ClientId = clientId,
                    AvailableSlotId = slotId,
                    Description = "ok",
                    Status = AppointmentStatus.Scheduled
                }
            };

            var result = await sut.Create(clientId, vm);

            var rd = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal(nameof(ClientController.Dashboard), rd.ActionName);
            Assert.Equal(clientId, rd.RouteValues!["clientId"]);
            apptRepo.Verify(r => r.AddAsync(It.IsAny<Appointment>()), Times.Once);
        }

        // P2: SlotsForDay with a valid date -> returns json with data
        [Fact]
        public async Task SlotsForDay_ValidDate_Returns_Json_With_Slots()
        {
            var apptRepo = new Mock<IAppointmentRepository>();
            var slotRepo = new Mock<IAvailableSlotRepository>();
            var userRepo = new Mock<IUserRepository>();
            var taskRepo = new Mock<ICareTaskRepository>();

            var day = DateOnly.FromDateTime(DateTime.Today.AddDays(1));
            slotRepo.Setup(r => r.GetFreeSlotsByDayAsync(day))
                    .ReturnsAsync(new List<AvailableSlot>
                    {
                        new AvailableSlot
                        {
                            AvailableSlotId = 1,
                            Day = day,
                            StartTime = new TimeOnly(9,0),
                            EndTime = new TimeOnly(11,0),
                            Personnel = new User { Name = "Nurse A" }
                        }
                    });

            var sut = MakeSut(apptRepo, slotRepo, userRepo, taskRepo);

            var result = await sut.SlotsForDay(day.ToString("yyyy-MM-dd"));
            var json = Assert.IsType<JsonResult>(result);

            var s = JsonSerializer.Serialize(json.Value);
            Assert.Contains("id", s);
            Assert.Contains("label", s);
            Assert.Contains("Nurse A", s);
        }

        // P3: Dashboard loads and returns a View
        [Fact]
        public async Task Dashboard_Returns_View()
        {
            var apptRepo = new Mock<IAppointmentRepository>();
            var slotRepo = new Mock<IAvailableSlotRepository>();
            var userRepo = new Mock<IUserRepository>();
            var taskRepo = new Mock<ICareTaskRepository>();

            userRepo.Setup(r => r.GetByRoleAsync(UserRole.Client))
                    .ReturnsAsync(new List<User> { new User { UserId = 10, Name = "Client X" } });
            apptRepo.Setup(r => r.GetByClientAsync(10))
                    .ReturnsAsync(new List<Appointment>());

            var sut = MakeSut(apptRepo, slotRepo, userRepo, taskRepo);

            var result = await sut.Dashboard(10);
            Assert.IsType<ViewResult>(result);
        }

        // ---------- NEGATIVE TESTS ----------

        // N1: If slot is already booked -> stay on Create and show ModelState error
        [Fact]
        public async Task Create_Post_SlotBooked_Shows_ModelError_On_Create_View()
        {
            var apptRepo = new Mock<IAppointmentRepository>();
            var slotRepo = new Mock<IAvailableSlotRepository>();
            var userRepo = new Mock<IUserRepository>();
            var taskRepo = new Mock<ICareTaskRepository>();

            const int clientId = 10;
            const int slotId = 99;

            apptRepo.Setup(r => r.SlotIsBookedAsync(slotId, null)).ReturnsAsync(true);
            slotRepo.Setup(r => r.GetFreeDaysAsync(It.IsAny<int>()))
                    .ReturnsAsync(new List<DateOnly> { DateOnly.FromDateTime(DateTime.Today.AddDays(1)) });
            taskRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<CareTask>());

            var sut = MakeSut(apptRepo, slotRepo, userRepo, taskRepo);

            var vm = new AppointmentCreateViewModel
            {
                Appointment = new Appointment
                {
                    ClientId = clientId,
                    AvailableSlotId = slotId,
                    Description = "x",
                    Status = AppointmentStatus.Scheduled
                }
            };

            var result = await sut.Create(clientId, vm);

            var view = Assert.IsType<ViewResult>(result);
            Assert.False(sut.ModelState.IsValid);
            Assert.True(
                sut.ModelState.Keys.Any(k =>
                    string.Equals(k, "AvailableSlotId", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(k, "Appointment.AvailableSlotId", StringComparison.OrdinalIgnoreCase)),
                "ModelState should contain an error for AvailableSlotId.");
        }

        // N2: SlotsForDay gets a bad date -> returns empty json array
        [Fact]
        public async Task SlotsForDay_InvalidDate_Returns_EmptyJson()
        {
            var apptRepo = new Mock<IAppointmentRepository>();
            var slotRepo = new Mock<IAvailableSlotRepository>();
            var userRepo = new Mock<IUserRepository>();
            var taskRepo = new Mock<ICareTaskRepository>();

            var sut = MakeSut(apptRepo, slotRepo, userRepo, taskRepo);

            var result = await sut.SlotsForDay("not-a-date");
            var json = Assert.IsType<JsonResult>(result);

            var s = JsonSerializer.Serialize(json.Value);
            Assert.Equal("[]", s);
        }

        // N3: Repository throws while creating -> redirect to Dashboard and set error
        [Fact]
        public async Task Create_Post_WhenRepoThrows_Redirects_With_Error()
        {
            var apptRepo = new Mock<IAppointmentRepository>();
            var slotRepo = new Mock<IAvailableSlotRepository>();
            var userRepo = new Mock<IUserRepository>();
            var taskRepo = new Mock<ICareTaskRepository>();

            const int clientId = 10;

            apptRepo.Setup(r => r.SlotIsBookedAsync(It.IsAny<int>(), null)).ReturnsAsync(false);
            apptRepo.Setup(r => r.AddAsync(It.IsAny<Appointment>()))
                    .ThrowsAsync(new Exception("db failure"));

            var sut = MakeSut(apptRepo, slotRepo, userRepo, taskRepo);

            var vm = new AppointmentCreateViewModel
            {
                Appointment = new Appointment
                {
                    ClientId = clientId,
                    AvailableSlotId = 5,
                    Status = AppointmentStatus.Scheduled
                }
            };

            var result = await sut.Create(clientId, vm);

            var rd = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal(nameof(ClientController.Dashboard), rd.ActionName);
            Assert.Equal(clientId, rd.RouteValues!["clientId"]);
            Assert.True(sut.TempData.ContainsKey("Error"));
        }
    }
}
