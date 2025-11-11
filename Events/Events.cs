namespace OrderService.gRPC.Events
{
    // Evento publicado cuando se crea un nuevo pedido
    public record OrderCreatedEvent
    {
        public int OrderId { get; init; }
        public int UserId { get; init; }
        public decimal TotalAmount { get; init; }
        public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
        public List<OrderItemDto> Items { get; init; } = new();
    }

    // Evento publicado cuando se completa un pedido
    public record OrderCompletedEvent
    {
        public int OrderId { get; init; }
        public int UserId { get; init; }
        public DateTime CompletedAt { get; init; } = DateTime.UtcNow;
    }

    // Evento publicado cuando se cancela un pedido
    public record OrderCancelledEvent
    {
        public int OrderId { get; init; }
        public int UserId { get; init; }
        public string Reason { get; init; } = string.Empty;
        public DateTime CancelledAt { get; init; } = DateTime.UtcNow;
    }

    // Evento publicado cuando cambia el estado de un pedido
    public record OrderStatusChangedEvent
    {
        public int OrderId { get; init; }
        public string OldStatus { get; init; } = string.Empty;
        public string NewStatus { get; init; } = string.Empty;
        public DateTime ChangedAt { get; init; } = DateTime.UtcNow;
    }

    // DTO para items en eventos
    public record OrderItemDto
    {
        public int ProductId { get; init; }
        public string ProductName { get; init; } = string.Empty;
        public int Quantity { get; init; }
        public decimal Price { get; init; }
    }
}
