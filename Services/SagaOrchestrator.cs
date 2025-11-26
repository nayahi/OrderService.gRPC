using System.Text.Json;
using ECommerceGRPC.NotificationService;
using ECommerceGRPC.PaymentService;
using ECommerceGRPC.ShippingService;
using global::OrderService.gRPC.Data;
using global::OrderService.gRPC.Models;
using Grpc.Net.Client;
using Microsoft.EntityFrameworkCore;


namespace OrderService.gRPC.Services
{
    /// <summary>
    /// Orquestador de saga distribuida para proceso de compra
    /// Coordina: Reserva de Stock → Pago → Notificación → Envío
    /// Con compensación automática en caso de fallo
    /// </summary>
    public class SagaOrchestrator
    {
        private readonly OrderDbContext _context;
        private readonly ILogger<SagaOrchestrator> _logger;
        private readonly IConfiguration _configuration;

        // Clientes gRPC
        private readonly GrpcChannel _productChannel;
        private readonly GrpcChannel _paymentChannel;
        private readonly GrpcChannel _notificationChannel;
        private readonly GrpcChannel _shippingChannel;

        public SagaOrchestrator(
            OrderDbContext context,
            ILogger<SagaOrchestrator> logger,
            IConfiguration configuration)
        {
            _context = context;
            _logger = logger;
            _configuration = configuration;

            // Inicializar canales gRPC (en producción usar factory con DI)
            _productChannel = GrpcChannel.ForAddress("http://productservice:7001");
            _paymentChannel = GrpcChannel.ForAddress("http://paymentservice:7004");
            _notificationChannel = GrpcChannel.ForAddress("http://notificationservice:7005");
            _shippingChannel = GrpcChannel.ForAddress("http://shippingservice:7006");
        }

        /// <summary>
        /// Ejecuta la saga completa para una orden
        /// </summary>
        public async Task<SagaState> ExecuteSagaAsync(Order order)
        {
            _logger.LogInformation("🚀 INICIANDO SAGA para Order {OrderId}", order.Id);

            // Crear estado de saga
            var saga = new SagaState
            {
                SagaId = Guid.NewGuid().ToString(),
                OrderId = order.Id,
                Status = SagaStatus.Started,
                StartedAt = DateTime.UtcNow
            };

            await _context.SagaStates.AddAsync(saga);
            await _context.SaveChangesAsync();

            try
            {
                saga.Status = SagaStatus.InProgress;
                await _context.SaveChangesAsync();

                // PASO 1: Reservar Stock
                var stockSuccess = await ReserveStockAsync(saga, order);
                if (!stockSuccess)
                {
                    await CompensateSagaAsync(saga, "Stock reservation failed");
                    return saga;
                }

                // PASO 2: Procesar Pago
                var paymentSuccess = await ProcessPaymentAsync(saga, order);
                if (!paymentSuccess)
                {
                    await CompensateSagaAsync(saga, "Payment processing failed");
                    return saga;
                }

                // PASO 3: Confirmar Reserva de Stock (commit)
                var confirmSuccess = await ConfirmStockReservationAsync(saga);
                if (!confirmSuccess)
                {
                    await CompensateSagaAsync(saga, "Stock confirmation failed");
                    return saga;
                }

                // PASO 4: Enviar Notificación (no crítico)
                await SendNotificationAsync(saga, order);

                // PASO 5: Crear Envío
                var shipmentSuccess = await CreateShipmentAsync(saga, order);
                if (!shipmentSuccess)
                {
                    _logger.LogWarning("⚠️ Shipment creation failed, but order is valid");
                }

                // Saga completada exitosamente
                saga.Status = SagaStatus.Completed;
                saga.CompletedAt = DateTime.UtcNow;

                // Actualizar orden a Completed
                order.Status = OrderStatus.Completed;
                order.CompletedAt = DateTime.UtcNow;

                _context.SagaStates.Update(saga);
                _context.Orders.Update(order);
                await _context.SaveChangesAsync();

                _logger.LogInformation("✅ SAGA COMPLETADA exitosamente para Order {OrderId}", order.Id);
                return saga;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error inesperado en saga para Order {OrderId}", order.Id);
                await CompensateSagaAsync(saga, $"Unexpected error: {ex.Message}");
                return saga;
            }
        }

        /// <summary>
        /// PASO 1: Reservar stock en ProductService
        /// </summary>
        private async Task<bool> ReserveStockAsync(SagaState saga, Order order)
        {
            var step = await CreateStepAsync(saga, SagaStepName.ReserveStock, 1);

            try
            {
                _logger.LogInformation("📦 PASO 1: Reservando stock para Order {OrderId}", order.Id);

                var client = new ECommerceGRPC.ProductService.ProductService.ProductServiceClient(_productChannel);

                foreach (var item in order.Items)
                {
                    var request = new ECommerceGRPC.ProductService.ReserveStockRequest
                    {
                        ProductId = item.ProductId,
                        Quantity = item.Quantity,
                        OrderId = order.Id
                    };

                    var response = await client.ReserveStockAsync(request);

                    if (!response.Success)
                    {
                        await CompleteStepAsync(step, StepStatus.Failed, null, response.Message);
                        _logger.LogWarning("❌ Reserva de stock FALLIDA - Product {ProductId}: {Message}",
                            item.ProductId, response.Message);
                        return false;
                    }

                    // Guardar ReservationId para compensación
                    saga.ReservationId = response.ReservationId;
                    await _context.SaveChangesAsync();
                }

                await CompleteStepAsync(step, StepStatus.Completed, saga.ReservationId, "Stock reserved");
                _logger.LogInformation("✓ Stock reservado - ReservationId: {ReservationId}", saga.ReservationId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al reservar stock");
                await CompleteStepAsync(step, StepStatus.Failed, null, ex.Message);
                return false;
            }
        }

        /// <summary>
        /// PASO 2: Procesar pago en PaymentService
        /// </summary>
        private async Task<bool> ProcessPaymentAsync(SagaState saga, Order order)
        {
            var step = await CreateStepAsync(saga, SagaStepName.ProcessPayment, 2);

            try
            {
                _logger.LogInformation("💳 PASO 2: Procesando pago para Order {OrderId}", order.Id);

                var client = new PaymentService.PaymentServiceClient(_paymentChannel);

                var request = new ProcessPaymentRequest
                {
                    OrderId = order.Id,
                    UserId = order.UserId,
                    Amount = (double)order.TotalAmount,
                    PaymentMethod = "CreditCard",
                    Currency = "USD"
                };

                var response = await client.ProcessPaymentAsync(request);

                if (response.Status == "Failed")
                {
                    await CompleteStepAsync(step, StepStatus.Failed, null, response.FailureReason);
                    _logger.LogWarning("❌ Pago FALLIDO - Reason: {Reason}", response.FailureReason);
                    return false;
                }

                // Guardar PaymentId para compensación
                saga.PaymentId = response.PaymentId.ToString();
                await _context.SaveChangesAsync();

                await CompleteStepAsync(step, StepStatus.Completed, saga.PaymentId, "Payment processed");
                _logger.LogInformation("✓ Pago procesado - PaymentId: {PaymentId}", saga.PaymentId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al procesar pago");
                await CompleteStepAsync(step, StepStatus.Failed, null, ex.Message);
                return false;
            }
        }

        /// <summary>
        /// PASO 3: Confirmar reserva de stock (commit)
        /// </summary>
        private async Task<bool> ConfirmStockReservationAsync(SagaState saga)
        {
            var step = await CreateStepAsync(saga, SagaStepName.ConfirmReservation, 3);

            try
            {
                _logger.LogInformation("✅ PASO 3: Confirmando reserva de stock");

                var client = new ECommerceGRPC.ProductService.ProductService.ProductServiceClient(_productChannel);

                var request = new ECommerceGRPC.ProductService.ConfirmReservationRequest
                {
                    ReservationId = saga.ReservationId
                };

                var response = await client.ConfirmReservationAsync(request);

                if (!response.Success)
                {
                    await CompleteStepAsync(step, StepStatus.Failed, null, response.Message);
                    _logger.LogWarning("❌ Confirmación de reserva FALLIDA");
                    return false;
                }

                await CompleteStepAsync(step, StepStatus.Completed, saga.ReservationId, "Reservation confirmed");
                _logger.LogInformation("✓ Reserva confirmada - Stock descontado");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al confirmar reserva");
                await CompleteStepAsync(step, StepStatus.Failed, null, ex.Message);
                return false;
            }
        }

        /// <summary>
        /// PASO 4: Enviar notificación (no crítico)
        /// </summary>
        private async Task SendNotificationAsync(SagaState saga, Order order)
        {
            var step = await CreateStepAsync(saga, SagaStepName.SendNotification, 4);

            try
            {
                _logger.LogInformation("📧 PASO 4: Enviando notificación");

                var client = new NotificationService.NotificationServiceClient(_notificationChannel);

                var request = new SendEmailRequest
                {
                    UserId = order.UserId,
                    OrderId = order.Id,
                    EmailTo = "customer@email.com", // En producción, obtener del UserService
                    Subject = $"Order #{order.Id} Confirmed",
                    Body = $"Your order for ${order.TotalAmount} has been confirmed and will be shipped soon.",
                    Template = "OrderConfirmation"
                };

                var response = await client.SendEmailAsync(request);

                saga.NotificationId = response.NotificationId.ToString();
                await _context.SaveChangesAsync();

                await CompleteStepAsync(step, StepStatus.Completed, saga.NotificationId, "Notification sent");
                _logger.LogInformation("✓ Notificación enviada");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ Error al enviar notificación (no crítico)");
                await CompleteStepAsync(step, StepStatus.Failed, null, ex.Message);
                // No retornamos false porque la notificación no es crítica
            }
        }

        /// <summary>
        /// PASO 5: Crear envío
        /// </summary>
        private async Task<bool> CreateShipmentAsync(SagaState saga, Order order)
        {
            var step = await CreateStepAsync(saga, SagaStepName.CreateShipment, 5);

            try
            {
                _logger.LogInformation("📦 PASO 5: Creando envío");

                var client = new ShippingService.ShippingServiceClient(_shippingChannel);

                var request = new CreateShipmentRequest
                {
                    OrderId = order.Id,
                    UserId = order.UserId,
                    ShippingAddress = "Default Address", // En producción, obtener del UserService
                    City = "San José",
                    Country = "Costa Rica",
                    ZipCode = "10101",
                    PhoneNumber = "+50612345678",
                    ShippingMethod = "Standard"
                };

                var response = await client.CreateShipmentAsync(request);

                saga.ShipmentId = response.ShipmentId.ToString();
                await _context.SaveChangesAsync();

                await CompleteStepAsync(step, StepStatus.Completed, saga.ShipmentId, "Shipment created");
                _logger.LogInformation("✓ Envío creado - ShipmentId: {ShipmentId}", saga.ShipmentId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ Error al crear envío");
                await CompleteStepAsync(step, StepStatus.Failed, null, ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Compensa (rollback) la saga en caso de fallo
        /// </summary>
        private async Task CompensateSagaAsync(SagaState saga, string reason)
        {
            _logger.LogWarning("🔄 COMPENSANDO SAGA para Order {OrderId} - Reason: {Reason}",
                saga.OrderId, reason);

            saga.Status = SagaStatus.Compensating;
            saga.FailureReason = reason;
            await _context.SaveChangesAsync();

            try
            {
                // Compensar en orden inverso

                // 1. Cancelar envío si existe
                if (!string.IsNullOrEmpty(saga.ShipmentId))
                {
                    await CancelShipmentAsync(saga);
                }

                // 2. Reembolsar pago si existe
                if (!string.IsNullOrEmpty(saga.PaymentId))
                {
                    await RefundPaymentAsync(saga);
                }

                // 3. Liberar reserva de stock si existe
                if (!string.IsNullOrEmpty(saga.ReservationId))
                {
                    await ReleaseStockAsync(saga);
                }

                saga.Status = SagaStatus.Compensated;
                saga.CompletedAt = DateTime.UtcNow;

                // Marcar orden como cancelada
                var order = await _context.Orders.FindAsync(saga.OrderId);
                if (order != null)
                {
                    order.Status = OrderStatus.Cancelled;
                    _context.Orders.Update(order);
                }

                await _context.SaveChangesAsync();

                _logger.LogInformation("✓ SAGA COMPENSADA exitosamente para Order {OrderId}", saga.OrderId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error durante compensación de saga");
                saga.Status = SagaStatus.Failed;
                await _context.SaveChangesAsync();
            }
        }

        private async Task<SagaStep> CreateStepAsync(SagaState saga, string stepName, int sequence)
        {
            var step = new SagaStep
            {
                SagaId = saga.SagaId,
                StepName = stepName,
                Status = StepStatus.InProgress,
                Sequence = sequence,
                StartedAt = DateTime.UtcNow
            };

            saga.Steps.Add(step);
            await _context.SaveChangesAsync();
            return step;
        }

        private async Task CompleteStepAsync(SagaStep step, string status, string? response, string? message)
        {
            step.Status = status;
            step.CompletedAt = DateTime.UtcNow;
            step.Response = response;
            if (status == StepStatus.Failed)
            {
                step.ErrorMessage = message;
            }
            await _context.SaveChangesAsync();
        }

        //modificados
        private async Task ReleaseStockAsync(SagaState saga)
        {
            try
            {
                // Verificar que ReservationId no sea null
                if (string.IsNullOrEmpty(saga.ReservationId))
                {
                    _logger.LogWarning("No hay ReservationId para liberar en saga {SagaId}", saga.SagaId);
                    return;
                }

                _logger.LogInformation("📦 Liberando stock - ReservationId: {ReservationId}", saga.ReservationId);

                var client = new ECommerceGRPC.ProductService.ProductService.ProductServiceClient(_productChannel);

                var request = new ECommerceGRPC.ProductService.ReleaseReservationRequest
                {
                    ReservationId = saga.ReservationId,
                    Reason = saga.FailureReason ?? "Saga compensation"
                };

                await client.ReleaseReservationAsync(request);
                _logger.LogInformation("✓ Stock liberado");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al liberar stock");
            }
        }

        private async Task RefundPaymentAsync(SagaState saga)
        {
            try
            {
                // Verificar que PaymentId no sea null
                if (string.IsNullOrEmpty(saga.PaymentId))
                {
                    _logger.LogWarning("No hay PaymentId para reembolsar en saga {SagaId}", saga.SagaId);
                    return;
                }

                _logger.LogInformation("💳 Reembolsando pago - PaymentId: {PaymentId}", saga.PaymentId);

                var client = new PaymentService.PaymentServiceClient(_paymentChannel);

                // Parsear PaymentId de forma segura
                if (!int.TryParse(saga.PaymentId, out int paymentId))
                {
                    _logger.LogError("PaymentId inválido: {PaymentId}", saga.PaymentId);
                    return;
                }

                // Primero obtener el pago para saber el monto
                var getRequest = new GetPaymentRequest { PaymentId = paymentId };
                var payment = await client.GetPaymentAsync(getRequest);

                var request = new RefundPaymentRequest
                {
                    PaymentId = paymentId,
                    Reason = saga.FailureReason ?? "Saga compensation",
                    Amount = payment.Amount
                };

                await client.RefundPaymentAsync(request);
                _logger.LogInformation("✓ Pago reembolsado");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al reembolsar pago");
            }
        }

        private async Task CancelShipmentAsync(SagaState saga)
        {
            try
            {
                // Verificar que ShipmentId no sea null
                if (string.IsNullOrEmpty(saga.ShipmentId))
                {
                    _logger.LogWarning("No hay ShipmentId para cancelar en saga {SagaId}", saga.SagaId);
                    return;
                }

                _logger.LogInformation("📦 Cancelando envío - ShipmentId: {ShipmentId}", saga.ShipmentId);

                var client = new ShippingService.ShippingServiceClient(_shippingChannel);

                // Parsear ShipmentId de forma segura
                if (!int.TryParse(saga.ShipmentId, out int shipmentId))
                {
                    _logger.LogError("ShipmentId inválido: {ShipmentId}", saga.ShipmentId);
                    return;
                }

                var request = new CancelShipmentRequest
                {
                    ShipmentId = shipmentId,
                    Reason = saga.FailureReason ?? "Saga compensation"
                };

                await client.CancelShipmentAsync(request);
                _logger.LogInformation("✓ Envío cancelado");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al cancelar envío");
            }
        }
    }
}
