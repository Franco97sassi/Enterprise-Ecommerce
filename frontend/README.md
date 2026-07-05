# Frontend React - Paso 12

Este frontend ya está creado en este repositorio. Si necesitas reproducirlo desde cero en otra copia del proyecto, estos son los comandos y archivos esperados.

## Crear el frontend desde cero

Ejecuta estos comandos desde la raíz del repositorio:

```bash
mkdir -p frontend/src
cd frontend
npm init -y
npm install react react-dom vite typescript
```

Actualiza `frontend/package.json` para que quede con estos scripts principales:

```json
{
  "type": "module",
  "scripts": {
    "dev": "vite --host 0.0.0.0",
    "build": "tsc -b && vite build",
    "preview": "vite preview --host 0.0.0.0"
  }
}
```

Crea los archivos base:

```bash
cat > index.html <<'HTML'
<!doctype html>
<html lang="es">
  <head>
    <meta charset="UTF-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <title>Enterprise Ecommerce Demo</title>
  </head>
  <body>
    <div id="root"></div>
    <script type="module" src="/src/main.tsx"></script>
  </body>
</html>
HTML

cat > vite.config.ts <<'TS'
import { defineConfig } from 'vite';

export default defineConfig({
  server: {
    port: 5173,
    proxy: {
      '/api': {
        target: 'http://localhost:5005',
        changeOrigin: true,
        rewrite: (path) => path.replace(/^\/api/, '')
      }
    }
  }
});
TS

cat > .env.example <<'ENV'
# Usa /api durante `npm run dev` para aprovechar el proxy de Vite.
# En despliegues estáticos, apunta al API Gateway real, por ejemplo: http://localhost:5005
VITE_API_BASE_URL=/api
ENV
```

Después agrega el código de la aplicación en `src/main.tsx` y los estilos en `src/styles.css`.

## Cómo queda la estructura

```text
frontend/
├── .env.example
├── Dockerfile
├── index.html
├── package.json
├── README.md
├── tsconfig.json
├── vite.config.ts
└── src/
    ├── main.tsx
    └── styles.css
```

## Ejecutar en desarrollo

Levanta la infraestructura:

```bash
docker compose up -d sqlserver rabbitmq redis
```

Levanta el API Gateway en `http://localhost:5005`:

```bash
dotnet run --project backend/ApiGateway/ApiGateway.csproj --launch-profile http
```

Levanta los servicios que quieras probar:

```bash
dotnet run --project backend/OrdersService/OrdersService.csproj --launch-profile http
dotnet run --project backend/StockService/StockService.csproj --launch-profile http
dotnet run --project backend/PaymentsService/PaymentsService.csproj --launch-profile http
dotnet run --project backend/BillingService/BillingService.csproj --launch-profile http
dotnet run --project backend/NotificationsService/NotificationsService.csproj --launch-profile http
```

Ejecuta el frontend:

```bash
cd frontend
npm install
npm run dev
```

Abre el navegador en:

```text
http://localhost:5173
```

## Ejecutar el frontend con Docker

```bash
docker compose up --build frontend
```

El frontend queda disponible en `http://localhost:5173`. El contenedor usa `VITE_API_BASE_URL=http://localhost:5005`, que coincide con el puerto HTTP del API Gateway.
