namespace OrderService.gRPC.Models
{
    public class Order
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public string Status { get; set; } = "Pending"; // // Pending, Processing, Confirmed, Shipped, Delivered, Cancelled
        public decimal TotalAmount { get; set; }
        public string ShippingAddress { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? CompletedAt { get; set; }
        public List<OrderItem> Items { get; set; } = new();
    }
}
