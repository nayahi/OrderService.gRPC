Para la semana 5 de micro3 ejecutar:
.\Test-Migration-grpcCurl.ps1                      # Todos los tests
.\Test-Migration-grpcCurl -Test 1              # Solo Flag OFF
.\Test-Migration-grpcCurl -Test 2 -OrderCount 20   # Canary con 20 órdenes
.\Test-Migration-grpcCurl -Test 3,4            # Solo migración completa + rollback
.\Test-Migration-grpcCurl -Test 2,3 -OrderCount 15  # Canary y full con 15 órdenes
-----------------------------------
# Esperar 10 segundos después de reiniciar

# Probar saga
grpcurl -plaintext -d '{
  "user_id": 2,
  "shipping_address": "Heredia, Costa Rica",
  "items": [{"product_id": 1, "quantity": 1, "unit_price": 1299.99}]
}' localhost:7003 orderservice.OrderService/CreateOrderWithSaga


-------------------
🚀 COMANDOS DE DEMO
Demo 1: Happy Path
grpcurl -plaintext -d '{
  "user_id": 2,
  "shipping_address": "Heredia, Costa Rica",
  "items": [{"product_id": 1, "quantity": 1, "unit_price": 1299.99}]
}' localhost:7003 orderservice.OrderService/CreateOrderWithSaga
Demo 2: Ver estado
grpcurl -plaintext -d '{"order_id": 5}' \
  localhost:7003 orderservice.OrderService/GetSagaStatus
Demo 3: SQL
sqlSELECT * FROM SagaStates ORDER BY StartedAt DESC;
SELECT * FROM SagaSteps WHERE SagaId = '...' ORDER BY Sequence;
-------------------

📋 TEST 1: SAGA EXITOSA (Happy Path)
Paso 1: Crear orden con saga
grpcurl -plaintext -d '{
  "user_id": 2,
  "shipping_address": "Heredia, Costa Rica",
  "items": [
    {"product_id": 1, "quantity": 1, "unit_price": 1299.99}
  ]
}' localhost:7003 orderservice.OrderService/CreateOrderWithSaga
Resultado esperado:
json{
  "id": 5,
  "userId": 2,
  "status": "Completed",
  "totalAmount": 1299.99,
  "items": [...]
}
✅ Si status = "Completed" → Saga funcionó correctamente

---
Paso 2: Ver estado detallado de la saga
grpcurl -plaintext -d '{"order_id": 5}' \
  localhost:7003 orderservice.OrderService/GetSagaStatus
Resultado esperado:
json{
  "sagaId": "abc-123...",
  "orderId": 5,
  "status": "Completed",
  "steps": [
    {"stepName": "ReserveStock", "status": "Completed", "sequence": 1},
    {"stepName": "ProcessPayment", "status": "Completed", "sequence": 2},
    {"stepName": "ConfirmReservation", "status": "Completed", "sequence": 3},
    {"stepName": "SendNotification", "status": "Completed", "sequence": 4},
    {"stepName": "CreateShipment", "status": "Completed", "sequence": 5}
  ]
}
✅ Todos los pasos deben estar "Completed"

---

----
📋 TEST 2: SAGA CON COMPENSACIÓN (Fallo simulado)
Paso 1: Crear múltiples órdenes hasta que falle el pago
Ejecutar 10 veces (el pago tiene 10% de probabilidad de fallo):
bash# Ejecutar este comando varias veces hasta ver status: "Cancelled"
grpcurl -plaintext -d '{
  "user_id": 2,
  "shipping_address": "San José, Costa Rica",
  "items": [
    {"product_id": 2, "quantity": 1, "unit_price": 999.99}
  ]
}' localhost:7003 orderservice.OrderService/CreateOrderWithSaga
Cuando falle, verás:
json{
  "id": 8,
  "status": "Cancelled",  // ← ORDEN CANCELADA
  "totalAmount": 999.99
}
Paso 2: Ver saga compensada
grpcurl -plaintext -d '{"order_id": 8}' \
  localhost:7003 orderservice.OrderService/GetSagaStatus
Resultado esperado:
json{
  "sagaId": "xyz-789...",
  "orderId": 8,
  "status": "Compensated",  // ← SAGA COMPENSADA
  "failureReason": "Payment processing failed",
  "steps": [
    {"stepName": "ReserveStock", "status": "Completed"},
    {"stepName": "ProcessPayment", "status": "Failed"},  // ← FALLÓ AQUÍ
    {"stepName": "ReleaseStock", "status": "Compensated"}  // ← ROLLBACK
  ]
}
✅ La reserva de stock fue liberada automáticamente

-*---

*******************
📊 VERIFICACIÓN EN SQL SERVER (Para mostrar en pantalla)
Ver tabla de Sagas
sqlUSE ECommerceOrders;

-- Ver todas las sagas
SELECT 
    SagaId,
    OrderId,
    Status,
    StartedAt,
    CompletedAt,
    FailureReason
FROM SagaStates
ORDER BY StartedAt DESC;
Ver pasos de una saga específica
sql-- Cambiar 'abc-123...' por el SagaId real
SELECT 
    StepName,
    Status,
    Sequence,
    StartedAt,
    CompletedAt,
    ErrorMessage
FROM SagaSteps
WHERE SagaId = 'abc-123...'
ORDER BY Sequence;
Ver resumen de sagas
sqlSELECT 
    Status,
    COUNT(*) as Total
FROM SagaStates
GROUP BY Status;
```

**Resultado esperado:**
```
Status          Total
-----------     -----
Completed       3
Compensated     1
```

---

## 🎓 PUNTOS CLAVE PARA EXPLICAR EN CLASE

### 1. **Saga Exitosa (Happy Path)**
```
OrderService → ReserveStock → ProcessPayment → ConfirmReservation 
            → SendNotification → CreateShipment → COMPLETED ✅
```

### 2. **Saga con Fallo (Compensación)**
```
OrderService → ReserveStock ✅ → ProcessPayment ❌ (FALLA)
            → COMPENSACIÓN: ReleaseStock ✅ → CANCELLED ✅
3. Ventajas del Patrón Saga

✅ Transacciones distribuidas entre microservicios
✅ Compensación automática en caso de fallo
✅ Trazabilidad completa (cada paso registrado)
✅ Consistencia eventual
*******************


***************////////////////
# ═══════════════════════════════════════════════════════════
# DEMO 1: SAGA EXITOSA
# ═══════════════════════════════════════════════════════════

echo "🎯 DEMO 1: Creando orden exitosa con saga..."

grpcurl -plaintext -d '{
  "user_id": 2,
  "shipping_address": "Heredia, Costa Rica",
  "items": [{"product_id": 1, "quantity": 1, "unit_price": 1299.99}]
}' localhost:7003 orderservice.OrderService/CreateOrderWithSaga

echo ""
echo "✅ Saga completada. Ver estado:"

grpcurl -plaintext -d '{"order_id": 5}' \
  localhost:7003 orderservice.OrderService/GetSagaStatus

echo ""
echo "═══════════════════════════════════════════════════════════"
echo "DEMO 2: FORZAR FALLO PARA VER COMPENSACIÓN"
echo "═══════════════════════════════════════════════════════════"

echo "🎯 Creando órdenes hasta que falle el pago (10% prob)..."

for i in {1..15}; do
  echo "Intento $i..."
  grpcurl -plaintext -d '{
    "user_id": 2,
    "shipping_address": "San José, CR",
    "items": [{"product_id": 2, "quantity": 1, "unit_price": 999.99}]
  }' localhost:7003 orderservice.OrderService/CreateOrderWithSaga | grep -q "Cancelled" && break
done

echo ""
echo "❌ Orden cancelada por fallo de pago"
echo "Ver compensación:"

# Ajustar order_id según el resultado
grpcurl -plaintext -d '{"order_id": 8}' \
  localhost:7003 orderservice.OrderService/GetSagaStatus
****************///////////////////


-----------------------------------
FLUJO DE DEMOSTRACIÓN EN CLASE
1 Explicar concepto
"Saga = Transacción distribuida entre microservicios"
"Si algo falla → rollback automático"

2 Demo exitosa

Ejecutar CreateOrderWithSaga
Mostrar status="Completed"
Mostrar GetSagaStatus con 5 pasos completados

3 Demo con fallo
Ejecutar hasta que falle
Mostrar status="Cancelled"
Mostrar GetSagaStatus con compensación

4 Mostrar SQL
Tabla SagaStates
Tabla SagaSteps
Resumen por estado
----------------------------------
# 1. Verificar que el servicio responde
curl http://localhost:7001/health

# 2. Verificar disponibilidad de un producto
grpcurl -plaintext -d '{"product_id": 1, "quantity": 5}' \
  localhost:7001 productservice.ProductService/CheckAvailability

# 3. Crear una reserva de prueba
grpcurl -plaintext -d '{"product_id": 1, "quantity": 2, "order_id": 999}' \
  localhost:7001 productservice.ProductService/ReserveStock

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


--------------
🔄 FLUJO DE SAGA IMPLEMENTADO
Escenario: Un cliente compra un producto
1. OrderService recibe CreateOrderWithSaga
   ├─ Crea orden inicial (status: Pending)
   └─ Inicia SagaOrchestrator
   
2. SagaOrchestrator ejecuta 5 PASOS:

   PASO 1: ReserveStock (ProductService)
   ├─ Reserva temporal del inventario
   ├─ Stock disponible = Stock real - Reservas activas
   └─ Guarda ReservationId en saga
   
   PASO 2: ProcessPayment (PaymentService)
   ├─ Intenta cobrar con tarjeta
   ├─ 10% probabilidad de fallo (simulado)
   └─ Guarda PaymentId en saga
   
   PASO 3: ConfirmReservation (ProductService)
   ├─ Confirma la reserva
   ├─ Descuenta stock PERMANENTEMENTE
   └─ Stock real = Stock real - Cantidad
   
   PASO 4: SendNotification (NotificationService)
   ├─ Envía email de confirmación
   └─ No crítico (no bloquea si falla)
   
   PASO 5: CreateShipment (ShippingService)
   ├─ Crea orden de envío
   └─ Guarda ShipmentId en saga

3. Si TODO sale bien:
   ├─ Order.Status = "Completed"
   ├─ Saga.Status = "Completed"
   └─ ✅ TRANSACCIÓN EXITOSA

4. Si ALGO falla (ej: pago):
   ├─ SagaOrchestrator detecta el fallo
   ├─ Ejecuta COMPENSACIÓN en orden inverso
   └─ ❌ TRANSACCIÓN CANCELADA

🔄 COMPENSACIÓN (Rollback Distribuido)
Si el Paso 2 (ProcessPayment) falla:
COMPENSACIÓN (orden inverso):

PASO 5: CancelShipment
├─ (No hay envío creado todavía)
└─ Skip

PASO 4: (Notificación)
├─ (No requiere compensación)
└─ Skip

PASO 3: (Confirmación)
├─ (No se llegó a confirmar)
└─ Skip

PASO 2: RefundPayment
├─ (Pago falló, no hay nada que reembolsar)
└─ Skip

PASO 1: ReleaseStock
├─ Libera la reserva de inventario
├─ Reserva.Status = "Released"
└─ Stock vuelve a estar disponible ✅

RESULTADO:
├─ Order.Status = "Cancelled"
├─ Saga.Status = "Compensated"
└─ Todo vuelve al estado inicial

🎯 VENTAJAS DEL PATRÓN SAGA
✅ Consistencia eventual entre microservicios
✅ Trazabilidad completa (cada paso registrado)
✅ Compensación automática en caso de fallo
✅ No requiere transacciones distribuidas (2PC)
✅ Cada servicio mantiene su autonomía

 EJEMPLO CONCRETO
Orden Exitosa:
Cliente compra Laptop ($1299.99)
├─ OrderService crea orden #10
├─ PASO 1: ProductService reserva 1 laptop
├─ PASO 2: PaymentService cobra $1299.99
├─ PASO 3: ProductService descuenta 1 del inventario
├─ PASO 4: NotificationService envía email
├─ PASO 5: ShippingService crea envío #6
└─ Orden #10 completada ✅
Orden con Fallo:
Cliente compra iPhone ($999.99)
├─ OrderService crea orden #11
├─ PASO 1: ProductService reserva 1 iPhone ✅
├─ PASO 2: PaymentService falla (fondos insuficientes) ❌
├─ COMPENSACIÓN: ProductService libera reserva ✅
└─ Orden #11 cancelada ❌

🔧 COMPONENTES CLAVE
1. SagaOrchestrator (Coordinador Central)

Ejecuta pasos secuencialmente
Detecta fallos
Ejecuta compensaciones
Registra todo en BD

2. SagaState (Estado de la saga)

Guarda status general
Almacena IDs de recursos (PaymentId, ReservationId, etc.)
Registra razón de fallo

3. SagaStep (Detalle de cada paso)

Nombre del paso
Status (Pending, Completed, Failed, Compensated)
Timestamps
Mensajes de error

-----------------
🎯 EXPLICACIÓN PASO A PASO (Versión Simplificada)
1. Cliente crea orden
POST /api/orders/saga
→ OrderService crea orden en BD (status: Pending)
2. OrderService inicia saga
SagaOrchestrator ejecuta 5 pasos:
3. PASO 1: Reservar stock
OrderService → ProductService: "Reserva 1 laptop"
ProductService: "OK, reserva #abc-123 creada"
Stock disponible disminuye (pero NO el stock real)
4. PASO 2: Cobrar pago
OrderService → PaymentService: "Cobra $1299.99"
PaymentService: "OK, pago #7 procesado" (90% éxito)
                o "ERROR, fondos insuficientes" (10% fallo)
5A. Si pago exitoso → continuar
PASO 3: Confirmar stock (descuenta permanentemente)
PASO 4: Enviar email de confirmación
PASO 5: Crear envío
→ Order status: "Completed" ✅
5B. Si pago falló → compensar
COMPENSACIÓN:
OrderService → ProductService: "Libera reserva #abc-123"
ProductService: "OK, stock disponible de nuevo"
→ Order status: "Cancelled" ❌
6. Todo queda registrado
Tabla SagaStates: Estado general
Tabla SagaSteps: Cada paso con timestamps

🎓 CONCEPTOS CLAVE PARA ESTUDIANTES
Saga = Transacción distribuida con pasos compensables
Características:

✅ Cada paso es una transacción local
✅ Si falla, se compensa en orden inverso
✅ Todo queda registrado para auditoría
✅ Consistencia eventual (no inmediata)

Diferencia con transacciones tradicionales:
Monolito:
BEGIN TRANSACTION
  UPDATE inventario
  INSERT pago
  INSERT envío
COMMIT (todo junto, atómico)

Microservicios con Saga:
PASO 1: UPDATE inventario en ProductDB
PASO 2: INSERT pago en PaymentDB
PASO 3: INSERT envío en ShippingDB
(cada uno en su momento, compensable)