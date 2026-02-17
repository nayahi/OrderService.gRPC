using System.Diagnostics;
using System.Text.Json;
using Amazon.SQS;
using Amazon.SQS.Model;
using ECommerceGRPC.NotificationService;
using Grpc.Net.Client;

namespace OrderService.gRPC.Services
{
    /// <summary>
    /// Resultado de una operación de notificación con métricas
    /// </summary>
    public class NotificationResult
    {
        public bool Success { get; set; }
        public string Route { get; set; } = "none";  // "lambda", "grpc", "grpc-fallback"
        public long LatencyMs { get; set; }
        public string? NotificationId { get; set; }
        public string? Error { get; set; }
    }

    /// <summary>
    /// SEMANA 5: Facade Pattern para migración de NotificationService.
    /// 
    /// Implementa Strangler Fig Pattern:
    /// - Si feature flag OFF  → llama a NotificationService.gRPC (comportamiento original)
    /// - Si feature flag ON   → envía a SQS → EmailBatch.Lambda lo procesa
    /// - Si flag ON + Rollout → canary: X% va a Lambda, resto a gRPC
    /// - Si Lambda falla      → fallback automático a gRPC (safety net)
    /// 
    /// El facade NO modifica la lógica del SagaOrchestrator — solo reemplaza
    /// el destino de la notificación de forma transparente.
    /// </summary>
    public class NotificationFacade
    {
        private readonly IFeatureFlagService _featureFlags;
        private readonly IAmazonSQS _sqsClient;
        private readonly ILogger<NotificationFacade> _logger;
        private readonly IConfiguration _configuration;

        // Canal gRPC al NotificationService (fallback)
        private readonly Lazy<GrpcChannel> _notificationChannel;

        // Nombre del feature flag que controla la migración
        private const string FlagName = "use_lambda_notifications";

        public NotificationFacade(
            IFeatureFlagService featureFlags,
            IAmazonSQS sqsClient,
            ILogger<NotificationFacade> logger,
            IConfiguration configuration)
        {
            _featureFlags = featureFlags;
            _sqsClient = sqsClient;
            _logger = logger;
            _configuration = configuration;

            // Lazy initialization del canal gRPC (mismo patrón que Lambdas de Sem 1)
            _notificationChannel = new Lazy<GrpcChannel>(() =>
                GrpcChannel.ForAddress(
                    _configuration["Services:NotificationService"]
                    ?? "http://notificationservice:7005"));
        }

        /// <summary>
        /// Envía notificación usando la ruta determinada por el feature flag.
        /// Incluye canary routing y fallback automático.
        /// </summary>
        public async Task<NotificationResult> SendNotificationAsync(
            int userId, int orderId, string emailTo,
            string subject, string body, string template)
        {
            var sw = Stopwatch.StartNew();

            // 1. Consultar feature flag (cacheado, ~0ms después del primer hit)
            var flagConfig = await _featureFlags.GetFlagConfigAsync(FlagName);
            var useLambda = ShouldUseLambda(flagConfig);

            _logger.LogInformation(
                "🔀 Facade: OrderId={OrderId} → Ruta={Route} (Flag: Enabled={Enabled}, Rollout={Rollout}%)",
                orderId, useLambda ? "LAMBDA" : "gRPC",
                flagConfig?.Enabled ?? false, flagConfig?.Rollout ?? 0);

            if (useLambda)
            {
                // 2A. Intentar enviar vía SQS → EmailBatch.Lambda
                var lambdaResult = await TrySendViaSqsAsync(
                    userId, orderId, emailTo, subject, body, template, sw);

                if (lambdaResult.Success)
                    return lambdaResult;

                // 2B. Si Lambda falla → fallback automático a gRPC
                _logger.LogWarning(
                    "⚠️ Facade: Lambda falló para OrderId={OrderId}, ejecutando fallback a gRPC. Error: {Error}",
                    orderId, lambdaResult.Error);

                var fallbackResult = await SendViaGrpcAsync(
                    userId, orderId, emailTo, subject, body, template, sw);
                fallbackResult.Route = "grpc-fallback";
                return fallbackResult;
            }
            else
            {
                // 3. Ruta original: NotificationService.gRPC
                return await SendViaGrpcAsync(
                    userId, orderId, emailTo, subject, body, template, sw);
            }
        }

        /// <summary>
        /// Determina si usar Lambda basado en flag + rollout porcentual (canary)
        /// </summary>
        private static bool ShouldUseLambda(FlagConfig? config)
        {
            if (config is null || !config.Enabled)
                return false;

            if (config.Rollout >= 100)
                return true;

            if (config.Rollout <= 0)
                return false;

            // Canary: random entre 0-99, si cae dentro del rollout → Lambda
            var roll = Random.Shared.Next(100);
            return roll < config.Rollout;
        }

        /// <summary>
        /// Envía notificación vía SQS → EmailBatch.Lambda
        /// Usa el mismo formato de mensaje que el script Test-Lambdas.ps1
        /// </summary>
        private async Task<NotificationResult> TrySendViaSqsAsync(
            int userId, int orderId, string emailTo,
            string subject, string body, string template,
            Stopwatch sw)
        {
            try
            {
                var queueUrl = _configuration["AWS:SQS:EmailQueueUrl"]
                    ?? "http://localhost:4566/000000000000/email-notifications-queue";

                var messageBody = JsonSerializer.Serialize(new
                {
                    UserId = userId,
                    OrderId = orderId,
                    EmailTo = emailTo,
                    Subject = subject,
                    Body = body,
                    Template = template,
                    Source = "NotificationFacade",  // Identifica que vino del Facade
                    Timestamp = DateTime.UtcNow.ToString("O")
                });

                var response = await _sqsClient.SendMessageAsync(new SendMessageRequest
                {
                    QueueUrl = queueUrl,
                    MessageBody = messageBody
                });

                sw.Stop();
                _logger.LogInformation(
                    "✅ Facade→SQS: OrderId={OrderId}, MessageId={MessageId}, Latency={Latency}ms",
                    orderId, response.MessageId, sw.ElapsedMilliseconds);

                return new NotificationResult
                {
                    Success = true,
                    Route = "lambda",
                    LatencyMs = sw.ElapsedMilliseconds,
                    NotificationId = response.MessageId
                };
            }
            catch (Exception ex)
            {
                sw.Stop();
                return new NotificationResult
                {
                    Success = false,
                    Route = "lambda",
                    LatencyMs = sw.ElapsedMilliseconds,
                    Error = ex.Message
                };
            }
        }

        /// <summary>
        /// Envía notificación vía gRPC al NotificationService (ruta original)
        /// Replica exactamente la lógica que tenía SagaOrchestrator.SendNotificationAsync
        /// </summary>
        private async Task<NotificationResult> SendViaGrpcAsync(
            int userId, int orderId, string emailTo,
            string subject, string body, string template,
            Stopwatch sw)
        {
            try
            {
                var client = new NotificationService.NotificationServiceClient(
                    _notificationChannel.Value);

                var request = new SendEmailRequest
                {
                    UserId = userId,
                    OrderId = orderId,
                    EmailTo = emailTo,
                    Subject = subject,
                    Body = body,
                    Template = template
                };

                var response = await client.SendEmailAsync(request);

                sw.Stop();
                _logger.LogInformation(
                    "✅ Facade→gRPC: OrderId={OrderId}, NotificationId={NotificationId}, Latency={Latency}ms",
                    orderId, response.NotificationId, sw.ElapsedMilliseconds);

                return new NotificationResult
                {
                    Success = true,
                    Route = "grpc",
                    LatencyMs = sw.ElapsedMilliseconds,
                    NotificationId = response.NotificationId.ToString()
                };
            }
            catch (Exception ex)
            {
                sw.Stop();
                _logger.LogError(ex,
                    "❌ Facade→gRPC falló: OrderId={OrderId}, Latency={Latency}ms",
                    orderId, sw.ElapsedMilliseconds);

                return new NotificationResult
                {
                    Success = false,
                    Route = "grpc",
                    LatencyMs = sw.ElapsedMilliseconds,
                    Error = ex.Message
                };
            }
        }
    }
}