namespace OrderService.gRPC.Models
{
    /// <summary>
    /// Estados posibles de una orden
    /// </summary>
    public static class OrderStatus
    {
        public const string Pending = "Pending";
        public const string Processing = "Processing";
        public const string Completed = "Completed";
        public const string Cancelled = "Cancelled";
        public const string Failed = "Failed";
        public const string Shipped = "Shipped";
        public const string Delivered = "Delivered";
    }
}
