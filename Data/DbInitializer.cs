using Microsoft.EntityFrameworkCore;
using OrderService.gRPC.Data;
using OrderService.gRPC.Models;

namespace OrderService.gRPC.Data
{

    /// <summary>
    /// Inicializador de base de datos con datos de prueba
    /// </summary>
    public static class DbInitializer
    {
        /// <summary>
        /// Inicializa la base de datos y carga datos de prueba si está vacía
        /// </summary>
        public static async Task InitializeAsync(OrderDbContext context, ILogger logger)
        {
            try
            {
                // Asegurar que la base de datos esté creada
                logger.LogInformation("Verificando existencia de base de datos...");
                await context.Database.EnsureCreatedAsync();

                // Aplicar migraciones pendientes
                if (context.Database.GetPendingMigrations().Any())
                {
                    logger.LogInformation("Aplicando migraciones pendientes...");
                    await context.Database.MigrateAsync();
                }

                // Verificar si ya existen productos
                if (await context.Orders.AnyAsync())
                {
                    logger.LogInformation("Base de datos ya contiene usuarios. Omitiendo inicialización.");
                    return;
                }

                //logger.LogInformation("Inicializando base de datos con datos de prueba...");

                //// Crear ordenes de prueba
                //var orders = new List<Order>
                //{
                //    new Order
                //{
                //    //Id = 1,
                //    Email = "admin@ecommerce.com",
                //    FirstName = "Admin",
                //    LastName = "System",
                //    PasswordHash = BCrypt.Net.BCrypt.HashPassword("Admin123!"),
                //    Role = "Admin",
                //    CreatedAt = DateTime.UtcNow,
                //    IsActive = true
                //},
                //new Order
                //{
                //    //Id = 2,
                //    Email = "juan.perez@email.com",
                //    FirstName = "Juan",
                //    LastName = "Pérez",
                //    PasswordHash = BCrypt.Net.BCrypt.HashPassword("Password123!"),
                //    Role = "Customer",
                //    CreatedAt = DateTime.UtcNow,
                //    IsActive = true
                //},
                //new Order
                //{
                //    //Id = 3,
                //    Email = "maria.gonzalez@email.com",
                //    FirstName = "María",
                //    LastName = "González",
                //    PasswordHash = BCrypt.Net.BCrypt.HashPassword("Password123!"),
                //    Role = "Premium",
                //    CreatedAt = DateTime.UtcNow,
                //    IsActive = true
                //}
                //};

                //// Agregar productos a la base de datos
                //await context.Orders.AddRangeAsync(orders);
                //await context.SaveChangesAsync();

                //logger.LogInformation($"Base de datos inicializada exitosamente con {users.Count} usuarios.");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error al inicializar la base de datos.");
                throw;
            }
        }
    }
}
