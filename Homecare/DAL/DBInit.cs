using Homecare.Models;
using Microsoft.EntityFrameworkCore;

namespace Homecare.Models
{
    public static class DBInit
    {
        public static void Seed(WebApplication app)
        {
            using var scope = app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            db.Database.EnsureCreated();

            SeedUsers(db);
            SeedCareTasks(db);

            var yesterday = DateOnly.FromDateTime(DateTime.Today.AddDays(-1));
            var tomorrow = DateOnly.FromDateTime(DateTime.Today.AddDays(1));

            // Tüm hemşireler için dün ve yarın 3'er slot (09–11, 12–14, 16–18)
            var nurseIds = db.Users.Where(u => u.Role == UserRole.Personnel)
                                   .Select(u => u.UserId).ToList();

            foreach (var day in new[] { yesterday, tomorrow })
            {
                foreach (var nurseId in nurseIds)
                {
                    EnsureSlot(db, nurseId, day, 9, 11);
                    EnsureSlot(db, nurseId, day, 12, 14);
                    EnsureSlot(db, nurseId, day, 16, 18);
                }
            }

            // ------------------------------
            // ÖRNEK RANDEVULAR
            // ------------------------------

            // 1) Dün (Past / Completed)
            UpsertAppointmentBySlot(
                db, Slot(db, 2, yesterday, 9),
                clientId: 10,
                status: AppointmentStatus.Completed,
                description: "Morning check & shopping",
                taskIds: new[] { 1, 3 }
            );

            UpsertAppointmentBySlot(
                db, Slot(db, 3, yesterday, 12),
                clientId: 11,
                status: AppointmentStatus.Completed,
                description: "Noon visit",
                taskIds: new[] { 2 }
            );

            UpsertAppointmentBySlot(
                db, Slot(db, 4, yesterday, 16),
                clientId: 12,
                status: AppointmentStatus.Completed,
                description: "Evening clean-up",
                taskIds: new[] { 4 }
            );

            // 2) Yarın (Upcoming / Scheduled)
            UpsertAppointmentBySlot(
                db, Slot(db, 2, tomorrow, 9),
                clientId: 10,
                status: AppointmentStatus.Scheduled,
                description: "Initial visit",
                taskIds: new[] { 1, 3 }
            );

            UpsertAppointmentBySlot(
                db, Slot(db, 3, tomorrow, 12),
                clientId: 11,
                status: AppointmentStatus.Scheduled,
                description: "Skin check",
                taskIds: new[] { 2, 4 }
            );

            UpsertAppointmentBySlot(
                db, Slot(db, 4, tomorrow, 16),
                clientId: 12,
                status: AppointmentStatus.Scheduled,
                description: "Shopping help",
                taskIds: new[] { 3 }
            );

            db.SaveChanges();
        }

        // --------------------------------------------------------------------
        // Yardımcılar
        // --------------------------------------------------------------------

        private static TimeOnly H(int hour) => new(hour, 0);

        /// <summary>Slot varsa döndürür; yoksa oluşturup geri verir.</summary>
        private static AvailableSlot EnsureSlot(AppDbContext db, int nurseId, DateOnly day, int startHour, int endHour)
        {
            var start = H(startHour);
            var end = H(endHour);

            var slot = db.AvailableSlots
                         .FirstOrDefault(s => s.PersonnelId == nurseId &&
                                              s.Day == day &&
                                              s.StartTime == start);

            if (slot == null)
            {
                slot = new AvailableSlot
                {
                    PersonnelId = nurseId,
                    Day = day,
                    StartTime = start,
                    EndTime = end
                };
                db.AvailableSlots.Add(slot);
                db.SaveChanges();
            }
            else if (slot.EndTime != end)
            {
                slot.EndTime = end;
                db.SaveChanges();
            }

            return slot;
        }

        /// <summary>İstenen slot yoksa hata fırlatır (seed sırasında sorunları hemen görürsünüz).</summary>
        private static AvailableSlot Slot(AppDbContext db, int nurseId, DateOnly day, int startHour)
        {
            var start = H(startHour);
            var slot = db.AvailableSlots.FirstOrDefault(s =>
                s.PersonnelId == nurseId &&
                s.Day == day &&
                s.StartTime == start);

            return slot ?? throw new InvalidOperationException(
                $"Seed: Slot bulunamadı. Nurse:{nurseId}, Day:{day}, Start:{start}");
        }

        /// <summary>
        /// Aynı slot için randevu varsa günceller, yoksa oluşturur.
        /// (Appointments.AvailableSlotId UNIQUE hatasını engeller)
        /// </summary>
        private static Appointment UpsertAppointmentBySlot(
            AppDbContext db,
            AvailableSlot slot,
            int clientId,
            AppointmentStatus status,
            string description,
            IEnumerable<int> taskIds)
        {
            var appt = db.Appointments
                         .Include(a => a.Tasks)
                         .FirstOrDefault(a => a.AvailableSlotId == slot.AvailableSlotId);

            if (appt == null)
            {
                appt = new Appointment
                {
                    AvailableSlotId = slot.AvailableSlotId,
                    ClientId = clientId,
                    Status = status,
                    Description = description,
                    CreatedAt = DateTime.UtcNow
                };
                db.Appointments.Add(appt);
                db.SaveChanges(); // Id için
            }
            else
            {
                appt.ClientId = clientId;
                appt.Status = status;
                appt.Description = description;
                db.SaveChanges();
            }

            // TaskList’i tazele
            var existing = db.TaskLists.Where(t => t.AppointmentId == appt.AppointmentId).ToList();
            if (existing.Count > 0)
            {
                db.TaskLists.RemoveRange(existing);
                db.SaveChanges();
            }

            foreach (var tid in taskIds.Distinct())
            {
                db.TaskLists.Add(new TaskList
                {
                    AppointmentId = appt.AppointmentId,
                    CareTaskId = tid
                });
            }

            db.SaveChanges();
            return appt;
        }

        private static void SeedUsers(AppDbContext db)
        {
            if (db.Users.Any()) return;

            db.Users.AddRange(
                new User { UserId = 1, Name = "Admin One", Email = "admin@hc.test", PasswordHash = "Admin!23", Role = UserRole.Admin },
                new User { UserId = 2, Name = "Nurse A", Email = "nurse.a@hc.test", PasswordHash = "P@ss", Role = UserRole.Personnel },
                new User { UserId = 3, Name = "Nurse B", Email = "nurse.b@hc.test", PasswordHash = "P@ss", Role = UserRole.Personnel },
                new User { UserId = 4, Name = "Nurse C", Email = "nurse.c@hc.test", PasswordHash = "P@ss", Role = UserRole.Personnel },
                new User { UserId = 5, Name = "Nurse D", Email = "nurse.d@hc.test", PasswordHash = "P@ss", Role = UserRole.Personnel },
                new User { UserId = 6, Name = "Nurse E", Email = "nurse.e@hc.test", PasswordHash = "P@ss", Role = UserRole.Personnel },
                new User { UserId = 10, Name = "Client Ali", Email = "client.ali@hc.test", PasswordHash = "1234", Role = UserRole.Client },
                new User { UserId = 11, Name = "Client Eva", Email = "client.eva@hc.test", PasswordHash = "1234", Role = UserRole.Client },
                new User { UserId = 12, Name = "Client Leo", Email = "client.leo@hc.test", PasswordHash = "1234", Role = UserRole.Client },
                new User { UserId = 13, Name = "Client Mia", Email = "client.mia@hc.test", PasswordHash = "1234", Role = UserRole.Client },
                new User { UserId = 14, Name = "Client Yan", Email = "client.yan@hc.test", PasswordHash = "1234", Role = UserRole.Client }
            );
            db.SaveChanges();
        }

        private static void SeedCareTasks(AppDbContext db)
        {
            if (db.CareTasks.Any()) return;

            db.CareTasks.AddRange(
                new CareTask { CareTaskId = 1, Description = "Medication reminder" },
                new CareTask { CareTaskId = 2, Description = "Assistance with daily living" },
                new CareTask { CareTaskId = 3, Description = "Shopping / groceries" },
                new CareTask { CareTaskId = 4, Description = "Light cleaning" }
            );
            db.SaveChanges();
        }
    }
}
