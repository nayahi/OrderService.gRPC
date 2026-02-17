using System.Diagnostics;
using System.Text.Json;
using Amazon.SQS;
using Amazon.SQS.Model;
using ECommerceGRPC.NotificationService;
using Grpc.Core;
using Grpc.Net.Client;

namespace OrderService.gRPC.Services
{
    public class NotificationResult
    {
        public bool Success { get; set; }
        public string Route { get; set; } = "none";
        public long LatencyMs { get; set; }
        public string? NotificationId { get; set; }
        public string? Error { get; set; }
    }

    public class NotificationFacade
    {
        private readonly IFeatureFlagService _featureFlags;
        private readonly IAmazonSQS _sqsClient;
        private readonly ILogger<NotificationFacade> _logger;
        private readonly IConfiguration _configuration;

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
            // SIN canal gRPC propio — se recibe del SagaOrchestrator
        }

        /// <summary>
        /// Envía notificación usando la ruta determinada por el feature flag.
        /// Recibe el GrpcChannel existente del SagaOrchestrator para reusar conexión.
        /// </summary>
        public async Task<NotificationResult> SendNotificationAsync(
            GrpcChannel notificationChannel,  // ← Canal existente del Saga
            int userId, int orderId, string emailTo,
            string subject, string body, string template)
        {
            var sw = Stopwatch.StartNew();

            var flagConfig = await _featureFlags.GetFlagConfigAsync(FlagName);
            var useLambda = ShouldUseLambda(flagConfig);

            _logger.LogInformation(
                "🔀 Facade: OrderId={OrderId} → Ruta={Route} (Flag: Enabled={Enabled}, Rollout={Rollout}%)",
                orderId, useLambda ? "LAMBDA" : "gRPC",
                flagConfig?.Enabled ?? false, flagConfig?.Rollout ?? 0);

            if (useLambda)
            {
                var lambdaResult = await TrySendViaSqsAsync(
                    userId, orderId, emailTo, subject, body, template, sw);

                if (lambdaResult.Success)
                    return lambdaResult;

                _logger.LogWarning(
                    "⚠️ Facade: Lambda falló para OrderId={OrderId}, fallback a gRPC. Error: {Error}",
                    orderId, lambdaResult.Error);

                var fallbackResult = await SendViaGrpcAsync(
                    notificationChannel, userId, orderId, emailTo, subject, body, template, sw);
                fallbackResult.Route = "grpc-fallback";
                return fallbackResult;
            }
            else
            {
                return await SendViaGrpcAsync(
                    notificationChannel, userId, orderId, emailTo, subject, body, template, sw);
            }
        }

        private static bool ShouldUseLambda(FlagConfig? config)
        {
            if (config is null || !config.Enabled) return false;
            if (config.Rollout >= 100) return true;
            if (config.Rollout <= 0) return false;

            var roll = Random.Shared.Next(100);
            return roll < config.Rollout;
        }

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
                    Source = "NotificationFacade",
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
        /// Envía vía gRPC reusando el canal existente del SagaOrchestrator
        /// </summary>
        private async Task<NotificationResult> SendViaGrpcAsync(
            GrpcChannel notificationChannel,  // ← Canal del Saga
            int userId, int orderId, string emailTo,
            string subject, string body, string template,
            Stopwatch sw)
        {
            try
            {
                var client = new NotificationService.NotificationServiceClient(notificationChannel);

                var request = new SendEmailRequest
                {
                    UserId = userId,
                    OrderId = orderId,
                    EmailTo = emailTo,
                    Subject = subject,
                    Body = body,
                    Template = template
                };

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                var callOptions = new CallOptions(cancellationToken: cts.Token);

                var response = await client.SendEmailAsync(request, callOptions);

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