using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace OrderService.gRPC.Models
{
    /// <summary>
    /// Estado de una saga distribuida para transacciones de orden
    /// </summary>
    public class SagaState
    {
        [Key]
        [MaxLength(50)]
        public string SagaId { get; set; } = Guid.NewGuid().ToString();

        [Required]
        public int OrderId { get; set; }

        [Required]
        [MaxLength(50)]
        public string Status { get; set; } = SagaStatus.Started;

        [Required]
        public DateTime StartedAt { get; set; } = DateTime.UtcNow;

        public DateTime? CompletedAt { get; set; }

        [MaxLength(500)]
        public string? FailureReason { get; set; }

        // Pasos de la saga
        public List<SagaStep> Steps { get; set; } = new List<SagaStep>();

        // IDs de recursos creados (para compensación)
        [MaxLength(50)]
        public string? PaymentId { get; set; }

        [MaxLength(50)]
        public string? ReservationId { get; set; }

        [MaxLength(50)]
        public string? NotificationId { get; set; }

        [MaxLength(50)]
        public string? ShipmentId { get; set; }

        // Relación con Order
        public Order? Order { get; set; }
    }

    /// <summary>
    /// Paso individual dentro de una saga
    /// </summary>
    public class SagaStep
    {
        [Key]
        public int StepId { get; set; }

        [Required]
        [MaxLength(50)]
        public string SagaId { get; set; } = string.Empty;

        [Required]
        [MaxLength(100)]
        public string StepName { get; set; } = string.Empty;

        [Required]
        [MaxLength(50)]
        public string Status { get; set; } = StepStatus.Pending;

        [Required]
        public int Sequence { get; set; }

        [Required]
        public DateTime StartedAt { get; set; } = DateTime.UtcNow;

        public DateTime? CompletedAt { get; set; }

        [MaxLength(1000)]
        public string? Request { get; set; }

        [MaxLength(1000)]
        public string? Response { get; set; }

        [MaxLength(500)]
        public string? ErrorMessage { get; set; }

        public int RetryCount { get; set; } = 0;

        // Relación con SagaState
        public SagaState? Saga { get; set; }
    }

    /// <summary>
    /// Estados de la saga completa
    /// </summary>
    public static class SagaStatus
    {
        public const string Started = "Started";
        public const string InProgress = "InProgress";
        public const string Completed = "Completed";
        public const string Failed = "Failed";
        public const string Compensating = "Compensating";
        public const string Compensated = "Compensated";
    }

    /// <summary>
    /// Estados de un paso individual
    /// </summary>
    public static class StepStatus
    {
        public const string Pending = "Pending";
        public const string InProgress = "InProgress";
        public const string Completed = "Completed";
        public const string Failed = "Failed";
        public const string Compensated = "Compensated";
    }

    /// <summary>
    /// Nombres de los pasos de la saga
    /// </summary>
    public static class SagaStepName
    {
        public const string ReserveStock = "ReserveStock";
        public const string ProcessPayment = "ProcessPayment";
        public const string SendNotification = "SendNotification";
        public const string CreateShipment = "CreateShipment";
        public const string ConfirmReservation = "ConfirmReservation";

        // Compensaciones
        public const string ReleaseStock = "ReleaseStock";
        public const string RefundPayment = "RefundPayment";
        public const string CancelShipment = "CancelShipment";
    }
}
