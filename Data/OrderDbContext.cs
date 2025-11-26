using Microsoft.EntityFrameworkCore;
using OrderService.gRPC.Models;

namespace OrderService.gRPC.Data
{
    public class OrderDbContext : DbContext
    {
        public OrderDbContext(DbContextOptions<OrderDbContext> options) : base(options)
        {
        }

        public DbSet<Order> Orders { get; set; }
        public DbSet<OrderItem> OrderItems { get; set; }

        public DbSet<SagaState> SagaStates { get; set; }
        public DbSet<SagaStep> SagaSteps { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Configuración Order
            modelBuilder.Entity<Order>(entity =>
            {
                entity.HasKey(e => e.Id);

                entity.Property(e => e.UserId)
                    .IsRequired();

                entity.Property(e => e.Status)
                    .IsRequired()
                    .HasMaxLength(50)
                    .HasDefaultValue("Pending");

                entity.Property(e => e.TotalAmount)
                    .HasPrecision(18, 2);

                entity.Property(e => e.ShippingAddress)
                    .IsRequired()
                    .HasMaxLength(500);

                entity.Property(e => e.CreatedAt)
                    .HasDefaultValueSql("GETUTCDATE()");

                // Relación con OrderItems
                entity.HasMany(e => e.Items)
                    .WithOne(i => i.Order)
                    .HasForeignKey(i => i.OrderId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // Configuración OrderItem
            modelBuilder.Entity<OrderItem>(entity =>
            {
                entity.HasKey(e => e.Id);

                entity.Property(e => e.ProductName)
                    .IsRequired()
                    .HasMaxLength(200);

                entity.Property(e => e.UnitPrice)
                    .HasPrecision(18, 2);
            });

            // Configuración de SagaState
            modelBuilder.Entity<SagaState>(entity =>
            {
                entity.ToTable("SagaStates");

                entity.HasKey(e => e.SagaId);

                entity.Property(e => e.SagaId)
                    .HasMaxLength(50)
                    .IsRequired();

                entity.Property(e => e.OrderId)
                    .IsRequired();

                entity.Property(e => e.Status)
                    .HasMaxLength(50)
                    .IsRequired()
                    .HasDefaultValue("Started");

                entity.Property(e => e.StartedAt)
                    .IsRequired()
                    .HasDefaultValueSql("GETUTCDATE()");

                entity.Property(e => e.CompletedAt);

                entity.Property(e => e.FailureReason)
                    .HasMaxLength(500);

                entity.Property(e => e.PaymentId)
                    .HasMaxLength(50);

                entity.Property(e => e.ReservationId)
                    .HasMaxLength(50);

                entity.Property(e => e.NotificationId)
                    .HasMaxLength(50);

                entity.Property(e => e.ShipmentId)
                    .HasMaxLength(50);

                // Relación con Order
                entity.HasOne(e => e.Order)
                    .WithMany()
                    .HasForeignKey(e => e.OrderId)
                    .OnDelete(DeleteBehavior.Restrict);

                // Índices
                entity.HasIndex(e => e.OrderId)
                    .HasDatabaseName("IX_SagaStates_OrderId");

                entity.HasIndex(e => e.Status)
                    .HasDatabaseName("IX_SagaStates_Status");

                entity.HasIndex(e => e.StartedAt)
                    .HasDatabaseName("IX_SagaStates_StartedAt");
            });

            // Configuración de SagaStep
            modelBuilder.Entity<SagaStep>(entity =>
            {
                entity.ToTable("SagaSteps");

                entity.HasKey(e => e.StepId);

                entity.Property(e => e.StepId)
                    .ValueGeneratedOnAdd();

                entity.Property(e => e.SagaId)
                    .HasMaxLength(50)
                    .IsRequired();

                entity.Property(e => e.StepName)
                    .HasMaxLength(100)
                    .IsRequired();

                entity.Property(e => e.Status)
                    .HasMaxLength(50)
                    .IsRequired()
                    .HasDefaultValue("Pending");

                entity.Property(e => e.Sequence)
                    .IsRequired();

                entity.Property(e => e.StartedAt)
                    .IsRequired()
                    .HasDefaultValueSql("GETUTCDATE()");

                entity.Property(e => e.CompletedAt);

                entity.Property(e => e.Request)
                    .HasMaxLength(1000);

                entity.Property(e => e.Response)
                    .HasMaxLength(1000);

                entity.Property(e => e.ErrorMessage)
                    .HasMaxLength(500);

                entity.Property(e => e.RetryCount)
                    .HasDefaultValue(0);

                // Relación con SagaState
                entity.HasOne(e => e.Saga)
                    .WithMany(s => s.Steps)
                    .HasForeignKey(e => e.SagaId)
                    .OnDelete(DeleteBehavior.Cascade);

                // Índices
                entity.HasIndex(e => e.SagaId)
                    .HasDatabaseName("IX_SagaSteps_SagaId");

                entity.HasIndex(e => e.StepName)
                    .HasDatabaseName("IX_SagaSteps_StepName");

                entity.HasIndex(e => e.Status)
                    .HasDatabaseName("IX_SagaSteps_Status");

                entity.HasIndex(e => new { e.SagaId, e.Sequence })
                    .HasDatabaseName("IX_SagaSteps_SagaId_Sequence");
            });
        }
    }
}
