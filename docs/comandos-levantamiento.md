# Comandos para levantar Enterprise Ecommerce

Ejecuta estos comandos desde la raiz del repositorio `/workspace/Enterprise-Ecommerce`, salvo cuando se indique entrar a `frontend`.

## 1. Infraestructura

```bash
docker compose up -d sqlserver rabbitmq redis
```

Servicios de infraestructura:

- SQL Server: `localhost:1433`
- RabbitMQ: `localhost:5672`
- RabbitMQ Management: `http://localhost:15672`
- Redis: `localhost:6379`

## 2. Microservicios backend

Abre una terminal por cada servicio.

### Orders Service

```bash
dotnet run --project backend/OrdersService/OrdersService.csproj --launch-profile http
```

URL: `http://localhost:5205`

### Stock Service

```bash
dotnet run --project backend/StockService/StockService.csproj --launch-profile http
```

URL: `http://localhost:5203`

### Payments Service

```bash
dotnet run --project backend/PaymentsService/PaymentsService.csproj --launch-profile http
```

URL: `http://localhost:5066`

### Billing Service

```bash
dotnet run --project backend/BillingService/BillingService.csproj --launch-profile http
```

URL: `http://localhost:5050`

### Notifications Service

```bash
dotnet run --project backend/NotificationsService/NotificationsService.csproj --launch-profile http
```

URL: `http://localhost:5207`

## 3. API Gateway

```bash
dotnet run --project backend/ApiGateway/ApiGateway.csproj --launch-profile http
```

URL: `http://localhost:5005`

## 4. Frontend React

```bash
cd frontend
npm install
npm run dev
```

URL del frontend: `http://localhost:5173`

## 5. Checks rapidos

```bash
curl http://localhost:5005/gateway/health
curl http://localhost:5005/orders/
curl http://localhost:5005/stock/
curl http://localhost:5005/payments/
curl http://localhost:5005/billing/
curl http://localhost:5005/notifications/
```

## 6. Levantar frontend con Docker

```bash
docker compose up --build frontend
```

URL del frontend con Docker: `http://localhost:5173`

## 7. Detener todo

```bash
docker compose down
```

Si tienes procesos `dotnet run` o `npm run dev` abiertos, detenlos con `Ctrl + C` en cada terminal.

## Nota sobre Saga

El frontend consulta `/order-sagas/`. Si ves un error al refrescar el dashboard, revisa que el API Gateway tenga configurada una ruta para `/order-sagas/{**catch-all}` hacia Orders Service.
