using System;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using OrderService.gRPC.Data;
using OrderService.gRPC.Services;
using Serilog;

// Configurar Serilog
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateLogger();

try
{
    Log.Information("Iniciando OrderService.gRPC");

    var builder = WebApplication.CreateBuilder(args);

    // Agregar Serilog
    builder.Host.UseSerilog();

    // Configurar DbContext
    builder.Services.AddDbContext<OrderDbContext>(options =>
        options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

    // Configurar MassTransit con RabbitMQ
    builder.Services.AddMassTransit(x =>
    {
        x.UsingRabbitMq((context, cfg) =>
        {
            // Leer configuración de RabbitMQ
            var rabbitHost = builder.Configuration["RabbitMQ:Host"] ?? "localhost";
            var rabbitUser = builder.Configuration["RabbitMQ:Username"] ?? "admin";
            var rabbitPass = builder.Configuration["RabbitMQ:Password"] ?? "admin123";

            cfg.Host(rabbitHost, "/", h =>
            {
                h.Username(rabbitUser);
                h.Password(rabbitPass);
            });

            cfg.ConfigureEndpoints(context);
        });
    });

    // Registrar servicios gRPC
    builder.Services.AddGrpc(options =>
    {
        options.EnableDetailedErrors = builder.Environment.IsDevelopment();
        options.MaxReceiveMessageSize = 4 * 1024 * 1024; // 4 MB
        options.MaxSendMessageSize = 4 * 1024 * 1024;    // 4 MB
    });

    // Health checks
    //builder.Services.AddHealthChecks()
    //    .AddDbContextCheck<OrderDbContext>()
    //    .AddRabbitMQ(rabbitConnectionString:
    //        $"amqp://{builder.Configuration["RabbitMQ:Username"]}:{builder.Configuration["RabbitMQ:Password"]}@{builder.Configuration["RabbitMQ:Host"]}/");

    // Configurar reflexión de gRPC para herramientas de desarrollo
    if (builder.Environment.IsDevelopment())
    {
        builder.Services.AddGrpcReflection();
    }

    var app = builder.Build();


    using (var scope = app.Services.CreateScope())
    {
        var services = scope.ServiceProvider;
        try
        {
            var context = services.GetRequiredService<OrderDbContext>();
            var logger = services.GetRequiredService<ILogger<Program>>();
            await DbInitializer.InitializeAsync(context, logger);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error al inicializar la base de datos");
            throw;
        }
    }

    // Aplicar migraciones automáticamente en desarrollo
    //if (app.Environment.IsDevelopment())
    //{
    //    using var scope = app.Services.CreateScope();
    //    var dbContext = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
    //    dbContext.Database.Migrate();
    //    Log.Information("Migraciones aplicadas correctamente");
    //}

    // Configurar pipeline HTTP
    if (app.Environment.IsDevelopment())
    {
        app.MapGrpcReflectionService();
    }

    // Configurar pipeline HTTP
    app.MapGrpcService<OrderGrpcService>();

    app.MapHealthChecks("/health");

    app.MapGet("/", () => "OrderService gRPC running with MassTransit. Use a gRPC client to connect on port 7003");

    Log.Information("OrderService.gRPC iniciado en puerto 7003 con integración a RabbitMQ");

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "La aplicación falló al iniciar");
}
finally
{
    Log.CloseAndFlush();
}
