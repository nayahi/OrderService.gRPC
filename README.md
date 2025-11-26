-----------------
1. 7. Ver Logs de Saga
Al ejecutar la orden, verás logs como:
🚀 INICIANDO SAGA para Order 10
📦 PASO 1: Reservando stock para Order 10
✓ Stock reservado - ReservationId: abc-123
💳 PASO 2: Procesando pago para Order 10
✓ Pago procesado - PaymentId: 7
✅ PASO 3: Confirmando reserva de stock
✓ Reserva confirmada - Stock descontado
📧 PASO 4: Enviando notificación
✓ Notificación enviada
📦 PASO 5: Creando envío
✓ Envío creado - ShipmentId: 6
✅ SAGA COMPLETADA exitosamente para Order 10
Si algo falla:
🚀 INICIANDO SAGA para Order 11
📦 PASO 1: Reservando stock para Order 11
✓ Stock reservado - ReservationId: xyz-456
💳 PASO 2: Procesando pago para Order 11
❌ Pago FALLIDO - Reason: Insufficient funds
🔄 COMPENSANDO SAGA para Order 11 - Reason: Payment processing failed
📦 Liberando stock - ReservationId: xyz-456
✓ Stock liberado
✓ SAGA COMPENSADA exitosamente para Order 11