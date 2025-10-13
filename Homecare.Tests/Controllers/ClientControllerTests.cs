using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Homecare.Controllers;
using Homecare.DAL.Interfaces;
using Homecare.Models;
using Homecare.ViewModels;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Moq;
using Xunit;

namespace Homecare.Tests.Controllers
{
    public class ClientControllerTests
    {
        private static ClientController MakeSut(
            Mock<IAppointmentRepository> apptRepo,
            Mock<IAvailableSlotRepository> slotRepo,
            Mock<IUserRepository> userRepo,
            Mock<ICareTaskRepository> taskRepo)
        {
            var sut = new ClientController(apptRepo.Object, slotRepo.Object, userRepo.Object, taskRepo.Object);
            sut.TempData = new TempDataDictionary(new DefaultHttpContext(), Mock.Of<ITempDataProvider>());
            return sut;
        }

        [Fact]
        public async Task Create_Post_WhenSlotBooked_Returns_Create_View_With_ModelError()
        {
            // arrange
            var apptRepo = new Mock<IAppointmentRepository>();
            var slotRepo = new Mock<IAvailableSlotRepository>();
            var userRepo = new Mock<IUserRepository>();
            var taskRepo = new Mock<ICareTaskRepository>();

            int clientId = 10;
            int slotId = 99;

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

            // act
            var result = await sut.Create(clientId, vm);

            // assert
            var view = Assert.IsType<ViewResult>(result);
            Assert.Equal("Create", view.ViewName ?? "Create");
            Assert.False(sut.ModelState.IsValid);

            // Anahtar hem "AvailableSlotId" hem de "Appointment.AvailableSlotId" olabilir
            Assert.True(
                sut.ModelState.Keys.Any(k =>
                    string.Equals(k, "AvailableSlotId", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(k, "Appointment.AvailableSlotId", StringComparison.OrdinalIgnoreCase)
                ),
                "ModelState should contain an error for AvailableSlotId."
            );
        }

        [Fact]
        public async Task Create_Post_Valid_Redirects_To_Dashboard()
        {
            // arrange
            var apptRepo = new Mock<IAppointmentRepository>();
            var slotRepo = new Mock<IAvailableSlotRepository>();
            var userRepo = new Mock<IUserRepository>();
            var taskRepo = new Mock<ICareTaskRepository>();

            int clientId = 12;
            int slotId = 77;

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

            // act
            var result = await sut.Create(clientId, vm);

            // assert
            var rd = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal(nameof(ClientController.Dashboard), rd.ActionName);
            // Aynı controller’dan redirect’te ControllerName null döner (geçerli)
            Assert.True(rd.ControllerName is null || rd.ControllerName == "Client");
            Assert.Equal(clientId, rd.RouteValues!["clientId"]);

            apptRepo.Verify(r => r.AddAsync(It.IsAny<Appointment>()), Times.Once);
        }
    }
}
