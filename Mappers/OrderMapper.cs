using global::OrderService.gRPC.Models;

namespace OrderService.gRPC.Mappers
{
    /// <summary>
    /// Mapper para conversiones de Order entre entidades y proto
    /// Compatible con el proto existente del curso
    /// </summary>
    public static class OrderMapper
    {
        /// <summary>
        /// Convierte una entidad Order a OrderResponse (proto existente)
        /// </summary>
        public static ECommerceGRPC.OrderService.OrderResponse ToOrderResponse(Order order)
        {
            var response = new ECommerceGRPC.OrderService.OrderResponse
            {
                Id = order.Id,
                UserId = order.UserId,
                Status = order.Status,
                TotalAmount = (double)order.TotalAmount,
                ShippingAddress = order.ShippingAddress ?? "",
                CreatedAt = order.CreatedAt.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                CompletedAt = order.CompletedAt?.ToString("yyyy-MM-ddTHH:mm:ss.fffZ") ?? ""
            };

            // Agregar items
            foreach (var item in order.Items)
            {
                response.Items.Add(new ECommerceGRPC.OrderService.OrderItemResponse
                {
                    Id = item.Id,
                    ProductId = item.ProductId,
                    ProductName = "", // Se puede llenar desde ProductService si se necesita
                    Quantity = item.Quantity,
                    UnitPrice = (double)item.UnitPrice,
                    Subtotal = (double)(item.Quantity * item.UnitPrice)
                });
            }

            return response;
        }
    }
}
