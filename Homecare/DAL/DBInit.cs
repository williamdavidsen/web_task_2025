// DAL/DBInit.cs
using Homecare.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;

namespace Homecare.DAL
{
    public static class DBInit
    {
        public static void Seed(WebApplication app)
        {
            using var scope = app.Services.CreateScope();
            var sp = scope.ServiceProvider;

            var db = sp.GetRequiredService<AppDbContext>();

            // create database/tables on first run (dev)
            db.Database.EnsureCreated();

            // ---- 1) Seed domain tables ----
            SeedDomainUsers(db);
            SeedCareTasks(db);
            SeedSlotsAndAppointments(db);
            db.SaveChanges();

            // ---- 2) Seed Identity (roles + accounts) ----
            var userMgr = sp.GetRequiredService<UserManager<IdentityUser>>();
            var roleMgr = sp.GetRequiredService<RoleManager<IdentityRole>>();
            EnsureIdentityAsync(db, userMgr, roleMgr).GetAwaiter().GetResult();
        }

        // ---------------- Identity seeding ----------------
        private static async Task EnsureIdentityAsync(
            AppDbContext db,
            UserManager<IdentityUser> userMgr,
            RoleManager<IdentityRole> roleMgr)
        {
            // make sure roles exist
            string[] roles = { "Admin", "Client", "Personnel" };
            foreach (var r in roles)
            {
                if (!await roleMgr.RoleExistsAsync(r))
                {
                    var rc = await roleMgr.CreateAsync(new IdentityRole(r));
                    if (!rc.Succeeded) throw new Exception("Role create failed: " + r);
                }
            }

            // create/login users to match domain table (dev passwords = "1234")
            foreach (var du in db.DomainUsers.AsNoTracking().ToList())
            {
                var email = (du.Email ?? "").Trim();
                if (string.IsNullOrWhiteSpace(email)) continue;

                var iu = await userMgr.FindByEmailAsync(email);
                if (iu == null)
                {
                    iu = new IdentityUser
                    {
                        UserName = email,
                        Email = email,
                        EmailConfirmed = true
                    };
                    var uc = await userMgr.CreateAsync(iu, "1234"); // DEV ONLY
                    if (!uc.Succeeded)
                        throw new Exception($"User create failed: {email} -> {string.Join(",", uc.Errors.Select(e => e.Description))}");
                }

                var roleName = du.Role switch
                {
                    UserRole.Admin => "Admin",
                    UserRole.Personnel => "Personnel",
                    _ => "Client"
                };

                if (!await userMgr.IsInRoleAsync(iu, roleName))
                {
                    var ad = await userMgr.AddToRoleAsync(iu, roleName);
                    if (!ad.Succeeded)
                        throw new Exception($"AddToRole failed: {email} -> {roleName}");
                }
            }
        }

        // ---------------- Domain seeding ----------------

        private static TimeOnly H(int hour) => new(hour, 0);

        private static void SeedSlotsAndAppointments(AppDbContext db)
        {
            // create 3 preset slots (yesterday + tomorrow) for each personnel
            var yesterday = DateOnly.FromDateTime(DateTime.Today.AddDays(-1));
            var tomorrow = DateOnly.FromDateTime(DateTime.Today.AddDays(1));

            var personnelIds = db.DomainUsers
                                 .Where(u => u.Role == UserRole.Personnel)
                                 .Select(u => u.UserId)
                                 .ToList();

            foreach (var day in new[] { yesterday, tomorrow })
            {
                foreach (var pid in personnelIds)
                {
                    EnsureSlot(db, pid, day, 9, 11);
                    EnsureSlot(db, pid, day, 12, 14);
                    EnsureSlot(db, pid, day, 16, 18);
                }
            }

            // sample appointments: past (Completed) + future (Scheduled)
            UpsertAppointmentBySlot(db, Slot(db, 2, yesterday, 9),
                clientId: 10, status: AppointmentStatus.Completed,
                description: "Morning check & shopping", taskIds: new[] { 1, 3 });

            UpsertAppointmentBySlot(db, Slot(db, 3, yesterday, 12),
                clientId: 11, status: AppointmentStatus.Completed,
                description: "Noon visit", taskIds: new[] { 2 });

            UpsertAppointmentBySlot(db, Slot(db, 4, yesterday, 16),
                clientId: 12, status: AppointmentStatus.Completed,
                description: "Evening clean-up", taskIds: new[] { 4 });

            UpsertAppointmentBySlot(db, Slot(db, 2, tomorrow, 9),
                clientId: 10, status: AppointmentStatus.Scheduled,
                description: "Initial visit", taskIds: new[] { 1, 3 });

            UpsertAppointmentBySlot(db, Slot(db, 3, tomorrow, 12),
                clientId: 11, status: AppointmentStatus.Scheduled,
                description: "Skin check", taskIds: new[] { 2, 4 });

            UpsertAppointmentBySlot(db, Slot(db, 4, tomorrow, 16),
                clientId: 12, status: AppointmentStatus.Scheduled,
                description: "Shopping help", taskIds: new[] { 3 });
        }

        private static void SeedDomainUsers(AppDbContext db)
        {
            if (db.DomainUsers.Any()) return;

            // Plain text here is just sample; real auth is Identity above.
            db.DomainUsers.AddRange(
                new User { UserId = 1, Name = "Admin One", Email = "admin@hc.test", PasswordHash = "1234", Role = UserRole.Admin },
                new User { UserId = 2, Name = "Nurse A", Email = "nurse.a@hc.test", PasswordHash = "1234", Role = UserRole.Personnel },
                new User { UserId = 3, Name = "Nurse B", Email = "nurse.b@hc.test", PasswordHash = "1234", Role = UserRole.Personnel },
                new User { UserId = 4, Name = "Nurse C", Email = "nurse.c@hc.test", PasswordHash = "1234", Role = UserRole.Personnel },
                new User { UserId = 5, Name = "Nurse D", Email = "nurse.d@hc.test", PasswordHash = "1234", Role = UserRole.Personnel },
                new User { UserId = 6, Name = "Nurse E", Email = "nurse.e@hc.test", PasswordHash = "1234", Role = UserRole.Personnel },
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

        private static AvailableSlot EnsureSlot(AppDbContext db, int personnelId, DateOnly day, int startHour, int endHour)
        {
            var start = H(startHour);
            var end = H(endHour);

            var slot = db.AvailableSlots.FirstOrDefault(s =>
                s.PersonnelId == personnelId && s.Day == day && s.StartTime == start);

            if (slot == null)
            {
                slot = new AvailableSlot
                {
                    PersonnelId = personnelId,
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

        private static AvailableSlot Slot(AppDbContext db, int personnelId, DateOnly day, int startHour)
        {
            var start = H(startHour);
            var slot = db.AvailableSlots.FirstOrDefault(s =>
                s.PersonnelId == personnelId && s.Day == day && s.StartTime == start);

            return slot ?? throw new InvalidOperationException(
                $"Seed: Slot not found. Personnel:{personnelId}, Day:{day}, Start:{start}");
        }

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
                db.SaveChanges();
            }
            else
            {
                appt.ClientId = clientId;
                appt.Status = status;
                appt.Description = description;
                db.SaveChanges();
            }

            // replace task list
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
    }
}
