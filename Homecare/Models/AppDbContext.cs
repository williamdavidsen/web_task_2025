using Homecare.Models;
using Microsoft.EntityFrameworkCore;

namespace Homecare.Models
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        public DbSet<User> Users => Set<User>();
        public DbSet<AvailableSlot> AvailableSlots => Set<AvailableSlot>();
        public DbSet<Appointment> Appointments => Set<Appointment>();
        public DbSet<CareTask> CareTasks => Set<CareTask>();
        public DbSet<TaskList> TaskLists => Set<TaskList>();

        protected override void OnModelCreating(ModelBuilder b)
        {
            base.OnModelCreating(b);

            // User
            b.Entity<User>(e =>
            {
                e.HasIndex(x => x.Email).IsUnique();
                e.Property(x => x.Role).HasConversion<int>();
            });

            // AvailableSlot
            b.Entity<AvailableSlot>(e =>
            {
                e.HasOne(s => s.Personnel)
                 .WithMany(u => u.AvailableSlotsAsPersonnel)
                 .HasForeignKey(s => s.PersonnelId)
                 .OnDelete(DeleteBehavior.Restrict);

                // Aynı personel aynı gün aynı saat aralığını tekrar açamasın
                e.HasIndex(x => new { x.PersonnelId, x.Day, x.StartTime, x.EndTime }).IsUnique();
                e.ToTable(tb => tb.HasCheckConstraint("CK_AvailableSlot_TimeRange", "[EndTime] > [StartTime]"));
            });

            // Appointment
            b.Entity<Appointment>(e =>
            {
                e.HasOne(a => a.AvailableSlot)
                 .WithOne(s => s.Appointment)
                 .HasForeignKey<Appointment>(a => a.AvailableSlotId)
                 .OnDelete(DeleteBehavior.Restrict);

                e.HasOne(a => a.Client)
                 .WithMany(u => u.AppointmentsAsClient)
                 .HasForeignKey(a => a.ClientId)
                 .OnDelete(DeleteBehavior.Restrict);

                e.Property(a => a.Status).HasConversion<int>();

                // 1 slot = 1 appointment
                e.HasIndex(a => a.AvailableSlotId).IsUnique();
            });

            // CareTask (tablo adı 'Task' da yapabilirdik; default kalsın)
            b.Entity<CareTask>(e =>
            {
                e.Property(t => t.Description).HasMaxLength(300).IsRequired();
            });

            // TaskList (junction)
            b.Entity<TaskList>(e =>
            {
                e.ToTable("TaskList");
                e.HasKey(x => new { x.AppointmentId, x.CareTaskId });

                e.HasOne(x => x.Appointment)
                 .WithMany(a => a.Tasks)
                 .HasForeignKey(x => x.AppointmentId)
                 .OnDelete(DeleteBehavior.Cascade);

                e.HasOne(x => x.CareTask)
                 .WithMany(t => t.TaskLinks)
                 .HasForeignKey(x => x.CareTaskId)
                 .OnDelete(DeleteBehavior.Restrict);
            });
        }
    }
}
