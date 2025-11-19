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

                logger.LogInformation("Inicializando datos de prueba...");

                // NOTA: Asumimos que ya existen estos usuarios en UserService
                // UserId 1: admin@ecommerce.com
                // UserId 2: juan.perez@email.com  
                // UserId 3: maria.gonzalez@email.com

                // NOTA: Asumimos que ya existen estos productos en ProductService
                // ProductId 1: Laptop Dell XPS 15 - $1299.99
                // ProductId 2: Mouse Logitech MX Master 3 - $99.99
                // ProductId 3: Teclado Mecánico Corsair K95 - $189.99
                // ProductId 4: Monitor Samsung 27" 4K - $449.99
                // ProductId 7: Auriculares Sony WH-1000XM4 - $349.99

                // ========================================
                // PEDIDO 1: Pedido completado de Juan Pérez
                // ========================================
                var order1 = new Order
                {
                    UserId = 2, // juan.perez@email.com
                    Status = "Completed",
                    TotalAmount = 1499.98m, // Laptop + Mouse
                    ShippingAddress = "Avenida Central 123, San José, Costa Rica",
                    CreatedAt = DateTime.UtcNow.AddDays(-5),
                    CompletedAt = DateTime.UtcNow.AddDays(-3),
                    Items = new List<OrderItem>
                    {
                        new OrderItem
                        {
                            ProductId = 1,
                            ProductName = "Laptop Dell XPS 15",
                            Quantity = 1,
                            UnitPrice = 1299.99m
                        },
                        new OrderItem
                        {
                            ProductId = 2,
                            ProductName = "Mouse Logitech MX Master 3",
                            Quantity = 2,
                            UnitPrice = 99.99m
                        }
                    }
                };

                // ========================================
                // PEDIDO 2: Pedido en proceso de María González
                // ========================================
                var order2 = new Order
                {
                    UserId = 3, // maria.gonzalez@email.com
                    Status = "Processing",
                    TotalAmount = 989.97m, // Monitor + Teclado + Auriculares
                    ShippingAddress = "Calle 5, Heredia, Costa Rica, Apartado 456",
                    CreatedAt = DateTime.UtcNow.AddDays(-2),
                    CompletedAt = null,
                    Items = new List<OrderItem>
                    {
                        new OrderItem
                        {
                            ProductId = 4,
                            ProductName = "Monitor Samsung 27\" 4K",
                            Quantity = 1,
                            UnitPrice = 449.99m
                        },
                        new OrderItem
                        {
                            ProductId = 3,
                            ProductName = "Teclado Mecánico Corsair K95",
                            Quantity = 1,
                            UnitPrice = 189.99m
                        },
                        new OrderItem
                        {
                            ProductId = 7,
                            ProductName = "Auriculares Sony WH-1000XM4",
                            Quantity = 1,
                            UnitPrice = 349.99m
                        }
                    }
                };

                // ========================================
                // PEDIDO 3: Pedido pendiente de Juan Pérez (PARA PRUEBAS)
                // ========================================
                var order3 = new Order
                {
                    UserId = 2, // juan.perez@email.com
                    Status = "Pending",
                    TotalAmount = 449.99m, // Solo Monitor
                    ShippingAddress = "Avenida Central 123, San José, Costa Rica",
                    CreatedAt = DateTime.UtcNow.AddHours(-2),
                    CompletedAt = null,
                    Items = new List<OrderItem>
                    {
                        new OrderItem
                        {
                            ProductId = 4,
                            ProductName = "Monitor Samsung 27\" 4K",
                            Quantity = 1,
                            UnitPrice = 449.99m
                        }
                    }
                };

                // ========================================
                // PEDIDO 4: Pedido cancelado de Admin
                // ========================================
                var order4 = new Order
                {
                    UserId = 1, // admin@ecommerce.com
                    Status = "Cancelled",
                    TotalAmount = 189.99m,
                    ShippingAddress = "Oficina Central, San José, Costa Rica",
                    CreatedAt = DateTime.UtcNow.AddDays(-10),
                    CompletedAt = null,
                    Items = new List<OrderItem>
                    {
                        new OrderItem
                        {
                            ProductId = 3,
                            ProductName = "Teclado Mecánico Corsair K95",
                            Quantity = 1,
                            UnitPrice = 189.99m
                        }
                    }
                };

                // Agregar pedidos al contexto
                await context.Orders.AddRangeAsync(order1, order2, order3, order4);

                // Guardar cambios
                var savedRecords = await context.SaveChangesAsync();

                logger.LogInformation(
                    "Inicialización completada. {OrderCount} pedidos con {ItemCount} items creados.",
                    savedRecords,
                    order1.Items.Count + order2.Items.Count + order3.Items.Count + order4.Items.Count
                );

                // Mostrar resumen de datos creados
                logger.LogInformation("=== RESUMEN DE DATOS INICIALES ===");
                logger.LogInformation("Pedido 1 (ID: {Id}) - Usuario: {UserId}, Estado: {Status}, Total: ${Total}",
                    order1.Id, order1.UserId, order1.Status, order1.TotalAmount);
                logger.LogInformation("Pedido 2 (ID: {Id}) - Usuario: {UserId}, Estado: {Status}, Total: ${Total}",
                    order2.Id, order2.UserId, order2.Status, order2.TotalAmount);
                logger.LogInformation("Pedido 3 (ID: {Id}) - Usuario: {UserId}, Estado: {Status}, Total: ${Total}",
                    order3.Id, order3.UserId, order3.Status, order3.TotalAmount);
                logger.LogInformation("Pedido 4 (ID: {Id}) - Usuario: {UserId}, Estado: {Status}, Total: ${Total}",
                    order4.Id, order4.UserId, order4.Status, order4.TotalAmount);
                logger.LogInformation("===================================");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error al inicializar la base de datos.");
                throw;
            }
        }
    }
}
