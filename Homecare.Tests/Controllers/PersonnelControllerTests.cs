using System;
using System.Collections.Generic;
using System.Linq;
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
                // helper: make controller with all deps + TempData
                private static PersonnelController MakeSut(
                    Mock<IAppointmentRepository> apptRepo,
                    Mock<IAvailableSlotRepository> slotRepo,
                    Mock<IUserRepository> userRepo)
                {
                        var logger = new Mock<ILogger<PersonnelController>>();
                        var sut = new PersonnelController(
                            apptRepo.Object,
                            slotRepo.Object,
                            userRepo.Object,
                            logger.Object);

                        sut.TempData = new TempDataDictionary(new DefaultHttpContext(), Mock.Of<ITempDataProvider>());
                        return sut;
                }

                // -------------------- POSITIVE TESTS --------------------

                // P1: Dashboard returns a View and sets PersonnelId on ViewBag
                [Fact]
                public async Task Dashboard_WithId_Returns_View_And_Sets_ViewBag()
                {
                        var apptRepo = new Mock<IAppointmentRepository>();
                        var slotRepo = new Mock<IAvailableSlotRepository>();
                        var userRepo = new Mock<IUserRepository>();

                        // give back empty lists (fine for this test)
                        apptRepo.Setup(r => r.GetByPersonnelAsync(2)).ReturnsAsync(new List<Appointment>());

                        var sut = MakeSut(apptRepo, slotRepo, userRepo);

                        var result = await sut.Dashboard(2);

                        var view = Assert.IsType<ViewResult>(result);
                        Assert.Equal(2, sut.ViewBag.PersonnelId);
                }

                // P2: CreateDay(GET) returns a View and fills JSON strings
                [Fact]
                public async Task CreateDay_Get_Returns_View_With_Selected_And_Locked_Days()
                {
                        var apptRepo = new Mock<IAppointmentRepository>();
                        var slotRepo = new Mock<IAvailableSlotRepository>();
                        var userRepo = new Mock<IUserRepository>();

                        var today = DateOnly.FromDateTime(DateTime.Today);
                        slotRepo.Setup(r => r.GetWorkDaysAsync(2, It.IsAny<DateOnly>(), It.IsAny<DateOnly>()))
                                .ReturnsAsync(new List<DateOnly> { today.AddDays(1), today.AddDays(3) });
                        slotRepo.Setup(r => r.GetLockedDaysAsync(2, It.IsAny<DateOnly>(), It.IsAny<DateOnly>()))
                                .ReturnsAsync(new List<DateOnly> { today.AddDays(5) });

                        var sut = MakeSut(apptRepo, slotRepo, userRepo);

                        var result = await sut.CreateDay(2);

                        var view = Assert.IsType<ViewResult>(result);
                        // just check JSON strings exist
                        Assert.NotNull(sut.ViewBag.SelectedDaysJson);
                        Assert.NotNull(sut.ViewBag.LockedDaysJson);
                }

                // P3: CreateDay(POST) adds new slots when new days are selected
                [Fact]
                public async Task CreateDay_Post_Adds_Preset_Slots_For_New_Days()
                {
                        var apptRepo = new Mock<IAppointmentRepository>();
                        var slotRepo = new Mock<IAvailableSlotRepository>();
                        var userRepo = new Mock<IUserRepository>();

                        // currently no work days
                        slotRepo.Setup(r => r.GetWorkDaysAsync(2, It.IsAny<DateOnly>(), It.IsAny<DateOnly>()))
                                .ReturnsAsync(new List<DateOnly>());

                        // no locked days
                        slotRepo.Setup(r => r.GetLockedDaysAsync(2, It.IsAny<DateOnly>(), It.IsAny<DateOnly>()))
                                .ReturnsAsync(new List<DateOnly>());

                        // capture what we add
                        List<AvailableSlot>? added = null;
                        slotRepo.Setup(r => r.AddRangeAsync(It.IsAny<IEnumerable<AvailableSlot>>()))
                                .Callback<IEnumerable<AvailableSlot>>(e => added = e.ToList())
                                .Returns(Task.CompletedTask);

                        var sut = MakeSut(apptRepo, slotRepo, userRepo);

                        // select 2 new days (CSV)
                        var d1 = DateOnly.FromDateTime(DateTime.Today.AddDays(2)).ToString("yyyy-MM-dd");
                        var d2 = DateOnly.FromDateTime(DateTime.Today.AddDays(3)).ToString("yyyy-MM-dd");

                        var result = await sut.CreateDay(2, $"{d1},{d2}");

                        var rd = Assert.IsType<RedirectToActionResult>(result);
                        Assert.Equal(nameof(PersonnelController.Dashboard), rd.ActionName);

                        // each day should create 3 preset slots → total 6
                        Assert.NotNull(added);
                        Assert.Equal(6, added!.Count);
                }

                // -------------------- NEGATIVE TESTS --------------------

                // N1: Dashboard throws inside -> redirect to Appointment/Table with error
                [Fact]
                public async Task Dashboard_WhenRepoThrows_Redirects_To_Appointment_Table()
                {
                        var apptRepo = new Mock<IAppointmentRepository>();
                        var slotRepo = new Mock<IAvailableSlotRepository>();
                        var userRepo = new Mock<IUserRepository>();

                        // make GetByPersonnelAsync throw so catch block runs
                        apptRepo.Setup(r => r.GetByPersonnelAsync(2))
                                .ThrowsAsync(new Exception("boom"));

                        var sut = MakeSut(apptRepo, slotRepo, userRepo);

                        var result = await sut.Dashboard(2);

                        var rd = Assert.IsType<RedirectToActionResult>(result);
                        Assert.Equal("Table", rd.ActionName);
                        Assert.Equal("Appointment", rd.ControllerName);
                        Assert.True(sut.TempData.ContainsKey("Error"));
                }

                // N2: CreateDay(GET) fails -> redirect back to Dashboard with error
                [Fact]
                public async Task CreateDay_Get_WhenRepoThrows_Redirects_With_Error()
                {
                        var apptRepo = new Mock<IAppointmentRepository>();
                        var slotRepo = new Mock<IAvailableSlotRepository>();
                        var userRepo = new Mock<IUserRepository>();

                        slotRepo.Setup(r => r.GetWorkDaysAsync(2, It.IsAny<DateOnly>(), It.IsAny<DateOnly>()))
                                .ThrowsAsync(new Exception("db fail"));

                        var sut = MakeSut(apptRepo, slotRepo, userRepo);

                        var result = await sut.CreateDay(2);

                        var rd = Assert.IsType<RedirectToActionResult>(result);
                        Assert.Equal(nameof(PersonnelController.Dashboard), rd.ActionName);
                        Assert.Equal(2, rd.RouteValues!["personnelId"]);
                        Assert.True(sut.TempData.ContainsKey("Error"));
                }

                // N3: CreateDay(POST) tries to remove a locked day -> do not remove, set error
                [Fact]
                public async Task CreateDay_Post_Removing_Locked_Day_Sets_Error_And_Does_Not_Remove()
                {
                        var apptRepo = new Mock<IAppointmentRepository>();
                        var slotRepo = new Mock<IAvailableSlotRepository>();
                        var userRepo = new Mock<IUserRepository>();

                        var today = DateOnly.FromDateTime(DateTime.Today);
                        var keepLocked = today.AddDays(2); // this day is locked

                        // existing work day is the locked one
                        slotRepo.Setup(r => r.GetWorkDaysAsync(2, It.IsAny<DateOnly>(), It.IsAny<DateOnly>()))
                                .ReturnsAsync(new List<DateOnly> { keepLocked });

                        // mark it as locked
                        slotRepo.Setup(r => r.GetLockedDaysAsync(2, It.IsAny<DateOnly>(), It.IsAny<DateOnly>()))
                                .ReturnsAsync(new List<DateOnly> { keepLocked });

                        // when controller checks the day's slots, return one with an appointment to simulate "locked by booking"
                        slotRepo.Setup(r => r.GetSlotsForPersonnelOnDayAsync(2, keepLocked))
                                .ReturnsAsync(new List<AvailableSlot>
                                {
                        new AvailableSlot
                        {
                            AvailableSlotId = 1,
                            Day = keepLocked,
                            StartTime = new TimeOnly(9,0),
                            EndTime = new TimeOnly(11,0),
                            Appointment = new Appointment { AppointmentId = 123 } // booked
                        }
                                });

                        // track if remove is called (it should NOT be)
                        var removeCalled = false;
                        slotRepo.Setup(r => r.RemoveRangeAsync(It.IsAny<IEnumerable<AvailableSlot>>()))
                                .Callback<IEnumerable<AvailableSlot>>(_ => removeCalled = true)
                                .Returns(Task.CompletedTask);

                        var sut = MakeSut(apptRepo, slotRepo, userRepo);

                        // user posts empty selection (wants to remove all), but the only day is locked
                        var result = await sut.CreateDay(2, days: "");

                        var rd = Assert.IsType<RedirectToActionResult>(result);
                        Assert.Equal(nameof(PersonnelController.Dashboard), rd.ActionName);
                        Assert.True(sut.TempData.ContainsKey("Error"));
                        Assert.False(removeCalled); // should not remove locked day
                }
        }
}
