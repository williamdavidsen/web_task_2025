using Homecare.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Homecare.DAL
{
    // Use IdentityDbContext so ASP.NET Identity tables live in the same database
    public class AppDbContext : IdentityDbContext<IdentityUser>
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        // Domain tables
        public DbSet<Appointment> Appointments { get; set; } = default!;
        public DbSet<AvailableSlot> AvailableSlots { get; set; } = default!;
        public DbSet<CareTask> CareTasks { get; set; } = default!;
        public DbSet<TaskList> TaskLists { get; set; } = default!;

        // Important: do not clash with Identity's AspNetUsers.
        // This is our domain “User” table with a different DbSet name.
        public DbSet<User> DomainUsers { get; set; } = default!;

        protected override void OnModelCreating(ModelBuilder b)
        {
            base.OnModelCreating(b);

            // -------- Domain User --------
            b.Entity<User>(e =>
            {
                e.HasIndex(x => x.Email).IsUnique();   // fast lookup + unique emails
                e.Property(x => x.Role).HasConversion<int>(); // store enum as int
            });

            // -------- AvailableSlot --------
            b.Entity<AvailableSlot>(e =>
            {
                // Each slot belongs to one Personnel (domain user)
                e.HasOne(s => s.Personnel)
                 .WithMany(u => u.AvailableSlotsAsPersonnel)
                 .HasForeignKey(s => s.PersonnelId)
                 .OnDelete(DeleteBehavior.Restrict);

                // Prevent duplicate identical slots per personnel/day/time
                e.HasIndex(x => new { x.PersonnelId, x.Day, x.StartTime, x.EndTime }).IsUnique();

                // Basic time sanity: End > Start
                e.ToTable(tb => tb.HasCheckConstraint("CK_AvailableSlot_TimeRange", "[EndTime] > [StartTime]"));
            });

            // -------- Appointment --------
            b.Entity<Appointment>(e =>
            {
                // 1 slot ↔ 1 appointment
                e.HasOne(a => a.AvailableSlot)
                 .WithOne(s => s.Appointment)
                 .HasForeignKey<Appointment>(a => a.AvailableSlotId)
                 .OnDelete(DeleteBehavior.Restrict);

                // Appointment belongs to a Client (domain user)
                e.HasOne(a => a.Client)
                 .WithMany(u => u.AppointmentsAsClient)
                 .HasForeignKey(a => a.ClientId)
                 .OnDelete(DeleteBehavior.Restrict);

                e.Property(a => a.Status).HasConversion<int>(); // store enum as int

                // Enforce one-appointment-per-slot at DB level too
                e.HasIndex(a => a.AvailableSlotId).IsUnique();
            });

            // -------- CareTask --------
            b.Entity<CareTask>(e =>
            {
                e.Property(t => t.Description)
                 .HasMaxLength(300)
                 .IsRequired();
            });

            // -------- TaskList (many-to-many link) --------
            b.Entity<TaskList>(e =>
            {
                e.ToTable("TaskList");
                e.HasKey(x => new { x.AppointmentId, x.CareTaskId });

                e.HasOne(x => x.Appointment)
                 .WithMany(a => a.Tasks)
                 .HasForeignKey(x => x.AppointmentId)
                 .OnDelete(DeleteBehavior.Cascade);    // delete links when appointment is deleted

                e.HasOne(x => x.CareTask)
                 .WithMany(t => t.TaskLinks)
                 .HasForeignKey(x => x.CareTaskId)
                 .OnDelete(DeleteBehavior.Restrict);   // keep task master data safe
            });
        }
    }
}
