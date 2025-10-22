// DAL/DBInit.cs
using Homecare.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;

namespace Homecare.DAL
{
    public static class DBInit
    {
        // ✅ Program.cs'ten await ile çağırılacak ana giriş
        public static async Task SeedAsync(WebApplication app)
        {
            using var scope = app.Services.CreateScope();
            var sp = scope.ServiceProvider;

            var db = sp.GetRequiredService<AppDbContext>();
            var userMgr = sp.GetRequiredService<UserManager<IdentityUser>>();
            var roleMgr = sp.GetRequiredService<RoleManager<IdentityRole>>();
            var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("DBInit");

            logger.LogInformation("DBInit: starting…");

            // ✅ Veritabanını (ve tabloları) oluştur/migrate
            await db.Database.EnsureCreatedAsync();
            // alternatif: await db.Database.MigrateAsync();

            // ---- 1) Domain tablolarını doldur ----
            await SeedDomainUsersAsync(db);
            await SeedCareTasksAsync(db);
            await SeedSlotsAndAppointmentsAsync(db);
            await db.SaveChangesAsync();

            // ---- 2) Identity (roller + hesaplar) ----
            await EnsureIdentityAsync(db, userMgr, roleMgr);

            logger.LogInformation("DBInit: done.");
        }

        // ---------------- Identity seeding ----------------
        private static async Task EnsureIdentityAsync(
            AppDbContext db,
            UserManager<IdentityUser> userMgr,
            RoleManager<IdentityRole> roleMgr)
        {
            string[] roles = { "Admin", "Client", "Personnel" };
            foreach (var r in roles)
            {
                if (!await roleMgr.RoleExistsAsync(r))
                {
                    var rc = await roleMgr.CreateAsync(new IdentityRole(r));
                    if (!rc.Succeeded)
                        throw new Exception("Role create failed: " + r);
                }
            }

            // Domain kullanıcılarına karşılık Identity kullanıcıları
            var domainUsers = await db.DomainUsers.AsNoTracking().ToListAsync();
            foreach (var du in domainUsers)
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

        private static async Task SeedSlotsAndAppointmentsAsync(AppDbContext db)
        {
            // 3 hazır slot (dün + yarın) — her personel için
            var yesterday = DateOnly.FromDateTime(DateTime.Today.AddDays(-1));
            var tomorrow = DateOnly.FromDateTime(DateTime.Today.AddDays(1));

            var personnelIds = await db.DomainUsers
                                       .Where(u => u.Role == UserRole.Personnel)
                                       .Select(u => u.UserId)
                                       .ToListAsync();

            foreach (var day in new[] { yesterday, tomorrow })
            {
                foreach (var pid in personnelIds)
                {
                    await EnsureSlotAsync(db, pid, day, 9, 11);
                    await EnsureSlotAsync(db, pid, day, 12, 14);
                    await EnsureSlotAsync(db, pid, day, 16, 18);
                }
            }

            // Örnek randevular: geçmiş (Completed) + gelecek (Scheduled)
            await UpsertAppointmentBySlotAsync(db, await SlotAsync(db, 2, yesterday, 9),
                clientId: 10, status: AppointmentStatus.Completed,
                description: "Morning check & shopping", taskIds: new[] { 1, 3 });

            await UpsertAppointmentBySlotAsync(db, await SlotAsync(db, 3, yesterday, 12),
                clientId: 11, status: AppointmentStatus.Completed,
                description: "Noon visit", taskIds: new[] { 2 });

            await UpsertAppointmentBySlotAsync(db, await SlotAsync(db, 4, yesterday, 16),
                clientId: 12, status: AppointmentStatus.Completed,
                description: "Evening clean-up", taskIds: new[] { 4 });

            await UpsertAppointmentBySlotAsync(db, await SlotAsync(db, 2, tomorrow, 9),
                clientId: 10, status: AppointmentStatus.Scheduled,
                description: "Initial visit", taskIds: new[] { 1, 3 });

            await UpsertAppointmentBySlotAsync(db, await SlotAsync(db, 3, tomorrow, 12),
                clientId: 11, status: AppointmentStatus.Scheduled,
                description: "Skin check", taskIds: new[] { 2, 4 });

            await UpsertAppointmentBySlotAsync(db, await SlotAsync(db, 4, tomorrow, 16),
                clientId: 12, status: AppointmentStatus.Scheduled,
                description: "Shopping help", taskIds: new[] { 3 });
        }

        private static async Task SeedDomainUsersAsync(AppDbContext db)
        {
            if (await db.DomainUsers.AnyAsync()) return;

            await db.DomainUsers.AddRangeAsync(
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
            await db.SaveChangesAsync();
        }

        private static async Task SeedCareTasksAsync(AppDbContext db)
        {
            if (await db.CareTasks.AnyAsync()) return;

            await db.CareTasks.AddRangeAsync(
                new CareTask { CareTaskId = 1, Description = "Medication reminder" },
                new CareTask { CareTaskId = 2, Description = "Assistance with daily living" },
                new CareTask { CareTaskId = 3, Description = "Shopping / groceries" },
                new CareTask { CareTaskId = 4, Description = "Light cleaning" }
            );
            await db.SaveChangesAsync();
        }

        private static async Task<AvailableSlot> EnsureSlotAsync(AppDbContext db, int personnelId, DateOnly day, int startHour, int endHour)
        {
            var start = H(startHour);
            var end = H(endHour);

            var slot = await db.AvailableSlots.FirstOrDefaultAsync(s =>
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
                await db.AvailableSlots.AddAsync(slot);
                await db.SaveChangesAsync();
            }
            else if (slot.EndTime != end)
            {
                slot.EndTime = end;
                await db.SaveChangesAsync();
            }

            return slot;
        }

        private static async Task<AvailableSlot> SlotAsync(AppDbContext db, int personnelId, DateOnly day, int startHour)
        {
            var start = H(startHour);
            var slot = await db.AvailableSlots.FirstOrDefaultAsync(s =>
                s.PersonnelId == personnelId && s.Day == day && s.StartTime == start);

            return slot ?? throw new InvalidOperationException(
                $"Seed: Slot not found. Personnel:{personnelId}, Day:{day}, Start:{start}");
        }

        private static async Task<Appointment> UpsertAppointmentBySlotAsync(
            AppDbContext db,
            AvailableSlot slot,
            int clientId,
            AppointmentStatus status,
            string description,
            IEnumerable<int> taskIds)
        {
            var appt = await db.Appointments
                               .Include(a => a.Tasks)
                               .FirstOrDefaultAsync(a => a.AvailableSlotId == slot.AvailableSlotId);

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
                await db.Appointments.AddAsync(appt);
                await db.SaveChangesAsync();
            }
            else
            {
                appt.ClientId = clientId;
                appt.Status = status;
                appt.Description = description;
                await db.SaveChangesAsync();
            }

            // görev listesini yeniden yaz
            var existing = await db.TaskLists.Where(t => t.AppointmentId == appt.AppointmentId).ToListAsync();
            if (existing.Count > 0)
            {
                db.TaskLists.RemoveRange(existing);
                await db.SaveChangesAsync();
            }

            foreach (var tid in taskIds.Distinct())
            {
                await db.TaskLists.AddAsync(new TaskList
                {
                    AppointmentId = appt.AppointmentId,
                    CareTaskId = tid
                });
            }

            await db.SaveChangesAsync();
            return appt;
        }
    }
}
