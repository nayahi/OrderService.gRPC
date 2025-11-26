using System;
using ECommerceGRPC.ProductService;
using ECommerceGRPC.UserService;
using global::ECommerceGRPC.OrderService;
using global::ECommerceGRPC.ProductService;
using global::ECommerceGRPC.UserService;
using Grpc.Core;
using Grpc.Net.Client;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using OrderService.gRPC.Data;
using OrderService.gRPC.Events;
using OrderService.gRPC.Mappers;
using OrderService.gRPC.Models;
using FluentValidation;


namespace OrderService.gRPC.Services
{
    public class OrderGrpcService : ECommerceGRPC.OrderService.OrderService.OrderServiceBase
    {
        private readonly IValidator<ECommerceGRPC.OrderService.CreateOrderRequest> _createValidator;
        private readonly SagaOrchestrator _sagaOrchestrator;
        private readonly OrderDbContext _context;
        private readonly ILogger<OrderGrpcService> _logger;
        private readonly IPublishEndpoint _publishEndpoint;
        private readonly IConfiguration _configuration;

        public OrderGrpcService(
            OrderDbContext context,
            ILogger<OrderGrpcService> logger,
            IPublishEndpoint publishEndpoint,
            IConfiguration configuration,
            IValidator<ECommerceGRPC.OrderService.CreateOrderRequest> createValidator,
            SagaOrchestrator sagaOrchestrator)
        {
            _context = context;
            _logger = logger;
            _publishEndpoint = publishEndpoint;
            _configuration = configuration;
            _createValidator = createValidator;
            _sagaOrchestrator = sagaOrchestrator;
        }

        public override async Task<OrderResponse> GetOrder(GetOrderRequest request, ServerCallContext context)
        {
            _logger.LogInformation("GetOrder called for ID: {OrderId}", request.Id);

            if (request.Id <= 0)
            {
                throw new RpcException(new Status(StatusCode.InvalidArgument,
                    "El ID del pedido debe ser mayor a cero"));
            }

            var order = await _context.Orders
                .Include(o => o.Items)
                .AsNoTracking()
                .FirstOrDefaultAsync(o => o.Id == request.Id);

            if (order == null)
            {
                throw new RpcException(new Status(StatusCode.NotFound,
                    $"Pedido con ID {request.Id} no encontrado"));
            }

            return MapToOrderResponse(order);
        }

        public override async Task GetOrders(GetOrdersRequest request,
            IServerStreamWriter<OrderResponse> responseStream, ServerCallContext context)
        {
            _logger.LogInformation("GetOrders called - Page: {PageNumber}, Size: {PageSize}",
                request.PageNumber, request.PageSize);

            var pageNumber = request.PageNumber > 0 ? request.PageNumber : 1;
            var pageSize = request.PageSize > 0 && request.PageSize <= 100 ? request.PageSize : 10;

            var query = _context.Orders.Include(o => o.Items).AsNoTracking();

            // Aplicar filtro por estado si se especificó
            if (!string.IsNullOrWhiteSpace(request.Status))
            {
                query = query.Where(o => o.Status == request.Status);
            }

            // Paginación
            var orders = await query
                .OrderByDescending(o => o.CreatedAt)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            // Streaming de pedidos
            foreach (var order in orders)
            {
                await responseStream.WriteAsync(MapToOrderResponse(order));
            }

            _logger.LogInformation("GetOrders completed - Sent {Count} orders", orders.Count);
        }

        public override async Task GetOrdersByUser(GetOrdersByUserRequest request,
            IServerStreamWriter<OrderResponse> responseStream, ServerCallContext context)
        {
            _logger.LogInformation("GetOrdersByUser called for UserID: {UserId}", request.UserId);

            if (request.UserId <= 0)
            {
                throw new RpcException(new Status(StatusCode.InvalidArgument,
                    "El ID del usuario debe ser mayor a cero"));
            }

            var pageNumber = request.PageNumber > 0 ? request.PageNumber : 1;
            var pageSize = request.PageSize > 0 && request.PageSize <= 100 ? request.PageSize : 10;

            var orders = await _context.Orders
                .Include(o => o.Items)
                .AsNoTracking()
                .Where(o => o.UserId == request.UserId)
                .OrderByDescending(o => o.CreatedAt)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            foreach (var order in orders)
            {
                await responseStream.WriteAsync(MapToOrderResponse(order));
            }

            _logger.LogInformation("GetOrdersByUser completed - Sent {Count} orders", orders.Count);
        }

        public override async Task<OrderResponse> CreateOrder(CreateOrderRequest request, ServerCallContext context)
        {
            _logger.LogInformation("CreateOrder called for UserID: {UserId}", request.UserId);

            // Validaciones básicas
            if (request.UserId <= 0)
            {
                throw new RpcException(new Status(StatusCode.InvalidArgument,
                    "El ID del usuario debe ser mayor a cero"));
            }

            if (string.IsNullOrWhiteSpace(request.ShippingAddress))
            {
                throw new RpcException(new Status(StatusCode.InvalidArgument,
                    "La dirección de envío es requerida"));
            }

            if (request.Items == null || !request.Items.Any())
            {
                throw new RpcException(new Status(StatusCode.InvalidArgument,
                    "El pedido debe contener al menos un producto"));
            }

            // COMUNICACIÓN INTER-SERVICIO: Validar usuario con UserService.gRPC
            await ValidateUserAsync(request.UserId);

            // COMUNICACIÓN INTER-SERVICIO: Validar productos y obtener precios con ProductService.gRPC
            var orderItems = await ValidateAndBuildOrderItemsAsync(request.Items);

            // Calcular total
            decimal totalAmount = orderItems.Sum(item => item.UnitPrice * item.Quantity);

            // Crear pedido
            var order = new Order
            {
                UserId = request.UserId,
                Status = "Pending",
                TotalAmount = totalAmount,
                ShippingAddress = request.ShippingAddress,
                CreatedAt = DateTime.UtcNow,
                Items = orderItems
            };

            _context.Orders.Add(order);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Order created successfully - ID: {OrderId}, Total: {Total}",
                order.Id, order.TotalAmount);

            // PUBLICAR EVENTO A RABBITMQ: OrderCreated
            await _publishEndpoint.Publish(new OrderCreatedEvent
            {
                OrderId = order.Id,
                UserId = order.UserId,
                TotalAmount = order.TotalAmount,
                CreatedAt = order.CreatedAt,
                Items = order.Items.Select(i => new OrderItemDto
                {
                    ProductId = i.ProductId,
                    ProductName = i.ProductName,
                    Quantity = i.Quantity,
                    Price = i.UnitPrice
                }).ToList()
            });

            _logger.LogInformation("OrderCreatedEvent published for OrderID: {OrderId}", order.Id);

            return MapToOrderResponse(order);
        }

        /// <summary>
        /// Crea una orden con saga distribuida (nuevo método)
        /// </summary>
        public override async Task<OrderResponse> CreateOrderWithSaga(
            CreateOrderRequest request,
            ServerCallContext context)
        {
            _logger.LogInformation("📝 Creando orden con SAGA para User {UserId}", request.UserId);

            // Validar request
            var validationResult = await _createValidator.ValidateAsync(request);
            if (!validationResult.IsValid)
            {
                var errors = string.Join(", ", validationResult.Errors.Select(e => e.ErrorMessage));
                _logger.LogWarning("Validación fallida para CreateOrderWithSaga: {Errors}", errors);
                throw new RpcException(new Status(StatusCode.InvalidArgument, errors));
            }

            try
            {
                // Crear entidad de orden
                var order = new Order
                {
                    UserId = request.UserId,
                    ShippingAddress = request.ShippingAddress,
                    Status = OrderStatus.Pending,
                    TotalAmount = 0, // Se calculará con los items
                    CreatedAt = DateTime.UtcNow
                };

                // Agregar items
                foreach (var item in request.Items)
                {
                    var orderItem = new OrderItem
                    {
                        ProductId = item.ProductId,
                        Quantity = item.Quantity,
                        UnitPrice = (decimal)item.UnitPrice  // ← USAR unit_price del request
                    };
                    order.Items.Add(orderItem);
                    order.TotalAmount += orderItem.Quantity * orderItem.UnitPrice;
                }

                // Guardar orden inicial
                await _context.Orders.AddAsync(order);
                await _context.SaveChangesAsync();

                _logger.LogInformation("✓ Orden {OrderId} creada - Iniciando SAGA", order.Id);

                // EJECUTAR SAGA DISTRIBUIDA
                var saga = await _sagaOrchestrator.ExecuteSagaAsync(order);

                // Recargar orden actualizada
                await _context.Entry(order).ReloadAsync();
                await _context.Entry(order).Collection(o => o.Items).LoadAsync();

                _logger.LogInformation(
                    saga.Status == SagaStatus.Completed
                        ? "✅ Orden {OrderId} procesada exitosamente con SAGA"
                        : "❌ Orden {OrderId} falló en SAGA - Status: {Status}",
                    order.Id, saga.Status);

                // Mapear respuesta usando el mapper compatible
                return OrderMapper.ToOrderResponse(order);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al crear orden con saga");
                throw new RpcException(new Status(StatusCode.Internal,
                    $"Error interno al crear orden: {ex.Message}"));
            }
        }

        /// <summary>
        /// Crea una orden con saga distribuida (con validador)
        /// </summary>
        //public override async Task<OrderResponse> CreateOrderWithSaga(
        //    CreateOrderRequest request,
        //    ServerCallContext context)
        //{
        //    _logger.LogInformation("📝 Creando orden con SAGA para User {UserId}", request.UserId);

        //    // Validar request
        //    var validationResult = await _createValidator.ValidateAsync(request);
        //    if (!validationResult.IsValid)
        //    {
        //        var errors = string.Join(", ", validationResult.Errors.Select(e => e.ErrorMessage));
        //        _logger.LogWarning("Validación fallida para CreateOrder: {Errors}", errors);
        //        throw new RpcException(new Status(StatusCode.InvalidArgument, errors));
        //    }

        //    try
        //    {
        //        // Crear entidad de orden
        //        var order = new Order
        //        {
        //            UserId = request.UserId,
        //            Status = OrderStatus.Pending,
        //            TotalAmount = 0, // Se calculará con los items
        //            CreatedAt = DateTime.UtcNow
        //        };

        //        // Agregar items
        //        foreach (var item in request.Items)
        //        {
        //            var orderItem = new OrderItem
        //            {
        //                ProductId = item.ProductId,
        //                Quantity = item.Quantity,
        //                UnitPrice = (decimal)item.UnitPrice
        //            };
        //            order.Items.Add(orderItem);
        //            order.TotalAmount += orderItem.Quantity * orderItem.UnitPrice;
        //        }

        //        // Guardar orden inicial
        //        await _context.Orders.AddAsync(order);
        //        await _context.SaveChangesAsync();

        //        _logger.LogInformation("✓ Orden {OrderId} creada - Iniciando SAGA", order.Id);

        //        // EJECUTAR SAGA DISTRIBUIDA
        //        var saga = await _sagaOrchestrator.ExecuteSagaAsync(order);

        //        // Recargar orden actualizada
        //        await _context.Entry(order).ReloadAsync();

        //        _logger.LogInformation(
        //            saga.Status == SagaStatus.Completed
        //                ? "✅ Orden {OrderId} procesada exitosamente con SAGA"
        //                : "❌ Orden {OrderId} falló en SAGA - Status: {Status}",
        //            order.Id, saga.Status);

        //        // Mapear respuesta
        //        return OrderMapper.ToOrderResponse(order);
        //    }
        //    catch (Exception ex)
        //    {
        //        _logger.LogError(ex, "Error al crear orden con saga");
        //        throw new RpcException(new Status(StatusCode.Internal,
        //            $"Error interno al crear orden: {ex.Message}"));
        //    }
        //}

        public override async Task<OrderResponse> UpdateOrderStatus(
            UpdateOrderStatusRequest request, ServerCallContext context)
        {
            _logger.LogInformation("UpdateOrderStatus called for ID: {OrderId}, NewStatus: {Status}",
                request.Id, request.Status);

            if (request.Id <= 0)
            {
                throw new RpcException(new Status(StatusCode.InvalidArgument,
                    "El ID del pedido debe ser mayor a cero"));
            }

            var validStatuses = new[] { "Pending", "Processing", "Completed", "Cancelled" };
            if (!validStatuses.Contains(request.Status))
            {
                throw new RpcException(new Status(StatusCode.InvalidArgument,
                    $"Estado inválido. Estados válidos: {string.Join(", ", validStatuses)}"));
            }

            var order = await _context.Orders
                .Include(o => o.Items)
                .FirstOrDefaultAsync(o => o.Id == request.Id);

            if (order == null)
            {
                throw new RpcException(new Status(StatusCode.NotFound,
                    $"Pedido con ID {request.Id} no encontrado"));
            }

            var oldStatus = order.Status;
            order.Status = request.Status;

            if (request.Status == "Completed")
            {
                order.CompletedAt = DateTime.UtcNow;

                // PUBLICAR EVENTO: OrderCompleted
                await _publishEndpoint.Publish(new OrderCompletedEvent
                {
                    OrderId = order.Id,
                    UserId = order.UserId,
                    CompletedAt = order.CompletedAt.Value
                });
            }

            await _context.SaveChangesAsync();

            _logger.LogInformation("Order status updated - ID: {OrderId}, OldStatus: {OldStatus}, NewStatus: {NewStatus}",
                order.Id, oldStatus, request.Status);

            // PUBLICAR EVENTO: OrderStatusChanged
            await _publishEndpoint.Publish(new OrderStatusChangedEvent
            {
                OrderId = order.Id,
                OldStatus = oldStatus,
                NewStatus = request.Status,
                ChangedAt = DateTime.UtcNow
            });

            return MapToOrderResponse(order);
        }

        public override async Task<OrderResponse> CancelOrder(CancelOrderRequest request, ServerCallContext context)
        {
            _logger.LogInformation("CancelOrder called for ID: {OrderId}", request.Id);

            if (request.Id <= 0)
            {
                throw new RpcException(new Status(StatusCode.InvalidArgument,
                    "El ID del pedido debe ser mayor a cero"));
            }

            var order = await _context.Orders
                .Include(o => o.Items)
                .FirstOrDefaultAsync(o => o.Id == request.Id);

            if (order == null)
            {
                throw new RpcException(new Status(StatusCode.NotFound,
                    $"Pedido con ID {request.Id} no encontrado"));
            }

            if (order.Status == "Completed")
            {
                throw new RpcException(new Status(StatusCode.FailedPrecondition,
                    "No se puede cancelar un pedido completado"));
            }

            if (order.Status == "Cancelled")
            {
                throw new RpcException(new Status(StatusCode.FailedPrecondition,
                    "El pedido ya está cancelado"));
            }

            order.Status = "Cancelled";
            await _context.SaveChangesAsync();

            _logger.LogInformation("Order cancelled successfully - ID: {OrderId}", order.Id);

            // PUBLICAR EVENTO: OrderCancelled
            await _publishEndpoint.Publish(new OrderCancelledEvent
            {
                OrderId = order.Id,
                UserId = order.UserId,
                Reason = request.Reason ?? "Cancelled by user",
                CancelledAt = DateTime.UtcNow
            });

            return MapToOrderResponse(order);
        }

        public override async Task<ValidateOrderItemsResponse> ValidateOrderItems(
            ValidateOrderItemsRequest request, ServerCallContext context)
        {
            _logger.LogInformation("ValidateOrderItems called with {Count} items", request.Items.Count);

            var errors = new List<ValidationError>();
            var isValid = true;

            foreach (var item in request.Items)
            {
                try
                {
                    await ValidateProductAsync(item.ProductId, item.Quantity);
                }
                catch (RpcException ex)
                {
                    isValid = false;
                    errors.Add(new ValidationError
                    {
                        ProductId = item.ProductId,
                        ErrorMessage = ex.Status.Detail
                    });
                }
            }

            return new ValidateOrderItemsResponse
            {
                IsValid = isValid,
                Message = isValid ? "Todos los productos son válidos" : "Algunos productos tienen errores",
                Errors = { errors }
            };
        }

        // MÉTODOS PRIVADOS AUXILIARES

        private async Task ValidateUserAsync(int userId)
        {
            try
            {
                var userServiceUrl = _configuration["GrpcServices:UserService"] ?? "http://localhost:7002";
                using var channel = GrpcChannel.ForAddress(userServiceUrl);
                var client = new ECommerceGRPC.UserService.UserService.UserServiceClient(channel);

                var response = await client.GetUserAsync(new GetUserRequest { Id = userId });

                if (!response.IsActive)
                {
                    throw new RpcException(new Status(StatusCode.FailedPrecondition,
                        "El usuario está inactivo"));
                }

                _logger.LogInformation("User validated successfully - UserID: {UserId}", userId);
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
            {
                throw new RpcException(new Status(StatusCode.NotFound,
                    $"Usuario con ID {userId} no encontrado"));
            }
            catch (RpcException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error comunicándose con UserService");
                throw new RpcException(new Status(StatusCode.Unavailable,
                    "UserService no disponible temporalmente"));
            }
        }

        private async Task<List<OrderItem>> ValidateAndBuildOrderItemsAsync(
            IEnumerable<OrderItemRequest> itemRequests)
        {
            var orderItems = new List<OrderItem>();
            var productServiceUrl = _configuration["GrpcServices:ProductService"] ?? "http://localhost:7001";

            using var channel = GrpcChannel.ForAddress(productServiceUrl);
            var client = new ECommerceGRPC.ProductService.ProductService.ProductServiceClient(channel);

            foreach (var itemRequest in itemRequests)
            {
                if (itemRequest.Quantity <= 0)
                {
                    throw new RpcException(new Status(StatusCode.InvalidArgument,
                        $"La cantidad debe ser mayor a cero para el producto {itemRequest.ProductId}"));
                }

                try
                {
                    var productResponse = await client.GetProductAsync(
                        new GetProductRequest { Id = itemRequest.ProductId });

                    if (!productResponse.IsActive)
                    {
                        throw new RpcException(new Status(StatusCode.FailedPrecondition,
                            $"El producto {productResponse.Name} no está disponible"));
                    }

                    if (productResponse.Stock < itemRequest.Quantity)
                    {
                        throw new RpcException(new Status(StatusCode.FailedPrecondition,
                            $"Stock insuficiente para {productResponse.Name}. Disponible: {productResponse.Stock}, Solicitado: {itemRequest.Quantity}"));
                    }

                    orderItems.Add(new OrderItem
                    {
                        ProductId = itemRequest.ProductId,
                        ProductName = productResponse.Name,
                        Quantity = itemRequest.Quantity,
                        UnitPrice = (decimal)productResponse.Price
                    });

                    _logger.LogInformation("Product validated - ProductID: {ProductId}, Name: {Name}",
                        itemRequest.ProductId, productResponse.Name);
                }
                catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
                {
                    throw new RpcException(new Status(StatusCode.NotFound,
                        $"Producto con ID {itemRequest.ProductId} no encontrado"));
                }
                catch (RpcException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error comunicándose con ProductService");
                    throw new RpcException(new Status(StatusCode.Unavailable,
                        "ProductService no disponible temporalmente"));
                }
            }

            return orderItems;
        }

        private async Task ValidateProductAsync(int productId, int quantity)
        {
            var productServiceUrl = _configuration["GrpcServices:ProductService"] ?? "http://localhost:7001";

            using var channel = GrpcChannel.ForAddress(productServiceUrl);
            var client = new ECommerceGRPC.ProductService.ProductService.ProductServiceClient(channel);

            var productResponse = await client.GetProductAsync(
                new GetProductRequest { Id = productId });

            if (!productResponse.IsActive)
            {
                throw new RpcException(new Status(StatusCode.FailedPrecondition,
                    $"El producto {productResponse.Name} no está disponible"));
            }

            if (productResponse.Stock < quantity)
            {
                throw new RpcException(new Status(StatusCode.FailedPrecondition,
                    $"Stock insuficiente para {productResponse.Name}"));
            }
        }

        private OrderResponse MapToOrderResponse(Order order)
        {
            var response = new OrderResponse
            {
                Id = order.Id,
                UserId = order.UserId,
                Status = order.Status,
                TotalAmount = (double)order.TotalAmount,
                ShippingAddress = order.ShippingAddress,
                CreatedAt = order.CreatedAt.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                CompletedAt = order.CompletedAt?.ToString("yyyy-MM-ddTHH:mm:ssZ") ?? string.Empty
            };

            foreach (var item in order.Items)
            {
                response.Items.Add(new OrderItemResponse
                {
                    Id = item.Id,
                    ProductId = item.ProductId,
                    ProductName = item.ProductName,
                    Quantity = item.Quantity,
                    UnitPrice = (double)item.UnitPrice,
                    Subtotal = (double)item.Subtotal
                });
            }

            return response;
        }

        /// <summary>
        /// Obtiene el estado de la saga de una orden
        /// </summary>
        public override async Task<GetSagaStatusResponse> GetSagaStatus(
            GetSagaStatusRequest request,
            ServerCallContext context)
        {
            _logger.LogInformation("Consultando estado de saga para Order {OrderId}", request.OrderId);

            try
            {
                var saga = await _context.SagaStates
                    .Include(s => s.Steps.OrderBy(step => step.Sequence))
                    .FirstOrDefaultAsync(s => s.OrderId == request.OrderId);

                if (saga == null)
                {
                    throw new RpcException(new Status(StatusCode.NotFound,
                        $"Saga not found for Order {request.OrderId}"));
                }

                var response = new GetSagaStatusResponse
                {
                    SagaId = saga.SagaId,
                    OrderId = saga.OrderId,
                    Status = saga.Status,
                    StartedAt = saga.StartedAt.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                    FailureReason = saga.FailureReason ?? ""
                };

                if (saga.CompletedAt.HasValue)
                    response.CompletedAt = saga.CompletedAt.Value.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

                // Agregar pasos
                foreach (var step in saga.Steps)
                {
                    var stepInfo = new SagaStepInfo
                    {
                        StepName = step.StepName,
                        Status = step.Status,
                        Sequence = step.Sequence,
                        StartedAt = step.StartedAt.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                        ErrorMessage = step.ErrorMessage ?? ""
                    };

                    if (step.CompletedAt.HasValue)
                        stepInfo.CompletedAt = step.CompletedAt.Value.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

                    response.Steps.Add(stepInfo);
                }

                return response;
            }
            catch (RpcException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener estado de saga");
                throw new RpcException(new Status(StatusCode.Internal,
                    $"Error interno: {ex.Message}"));
            }
        }
    }


}
