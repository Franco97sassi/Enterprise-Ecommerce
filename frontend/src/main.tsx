import React, { FormEvent, useCallback, useEffect, useMemo, useState } from 'react';
import { createRoot } from 'react-dom/client';
import './styles.css';

type HealthResponse = { service?: string; Service?: string; status?: string; Status?: string };
type Order = { id: number; customer: string; product: string; quantity: number; total: number; status: string };
type ProductStock = { id: number; product: string; availableQuantity: number; reservedQuantity: number };
type Payment = { id: number; orderId: number; amount: number; status: string };
type BillingRecord = { id: number; orderId: number; amount: number; status: string };
type Notification = { id: number; orderId: number; recipient: string; message: string; status: string };
type Saga = { id: number; orderId: number; currentStep: string; status: string; updatedAt: string };

type LoadState = 'idle' | 'loading' | 'success' | 'error';

type DashboardData = {
  orders: Order[];
  stock: ProductStock[];
  payments: Payment[];
  billing: BillingRecord[];
  notifications: Notification[];
  sagas: Saga[];
};

const emptyData: DashboardData = {
  orders: [],
  stock: [],
  payments: [],
  billing: [],
  notifications: [],
  sagas: []
};

const apiBaseUrl = import.meta.env.VITE_API_BASE_URL ?? '/api';

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(`${apiBaseUrl}${path}`, {
    headers: { 'Content-Type': 'application/json', ...init?.headers },
    ...init
  });

  if (!response.ok) {
    const message = await response.text();
    throw new Error(message || `Error HTTP ${response.status}`);
  }

  if (response.status === 204) return undefined as T;
  return response.json() as Promise<T>;
}

function App() {
  const [data, setData] = useState<DashboardData>(emptyData);
  const [health, setHealth] = useState<HealthResponse | null>(null);
  const [state, setState] = useState<LoadState>('idle');
  const [message, setMessage] = useState('');
  const [form, setForm] = useState({ customer: 'Cliente Demo', product: 'Laptop', quantity: 1, total: 1299 });
  const [stockForm, setStockForm] = useState({ product: 'Laptop', availableQuantity: 25, reservedQuantity: 0 });

  const refresh = useCallback(async () => {
    setState('loading');
    try {
      const [gatewayHealth, orders, stock, payments, billing, notifications, sagas] = await Promise.all([
        request<HealthResponse>('/gateway/health'),
        request<Order[]>('/orders/'),
        request<ProductStock[]>('/stock/'),
        request<Payment[]>('/payments/'),
        request<BillingRecord[]>('/billing/'),
        request<Notification[]>('/notifications/'),
        request<Saga[]>('/order-sagas/')
      ]);

      setHealth(gatewayHealth);
      setData({ orders, stock, payments, billing, notifications, sagas });
      setState('success');
      setMessage('Datos actualizados correctamente.');
    } catch (error) {
      setState('error');
      setMessage(error instanceof Error ? error.message : 'No se pudo cargar el dashboard.');
    }
  }, []);

  useEffect(() => {
    refresh();
  }, [refresh]);

  const latestOrder = useMemo(() => data.orders[0], [data.orders]);

  async function createOrder(event: FormEvent) {
    event.preventDefault();
    try {
      setState('loading');
      await request<Order>('/orders/', {
        method: 'POST',
        body: JSON.stringify({ ...form, status: 'Pending' })
      });
      setMessage('Orden creada. RabbitMQ, Saga y Outbox deberían procesar el flujo automáticamente.');
      await refresh();
    } catch (error) {
      setState('error');
      setMessage(error instanceof Error ? error.message : 'No se pudo crear la orden.');
    }
  }

  async function createStock(event: FormEvent) {
    event.preventDefault();
    try {
      setState('loading');
      await request<ProductStock>('/stock/', { method: 'POST', body: JSON.stringify(stockForm) });
      setMessage('Stock creado o actualizado para pruebas.');
      await refresh();
    } catch (error) {
      setState('error');
      setMessage(error instanceof Error ? error.message : 'No se pudo crear el stock.');
    }
  }

  return (
    <main className="shell">
      <section className="hero">
        <div>
          <p className="eyebrow">Paso 12 completo</p>
          <h1>Frontend React para probar Enterprise Ecommerce</h1>
          <p>
            Crea stock, dispara órdenes y observa pagos, facturación, notificaciones y Saga Orchestrator desde el API Gateway.
          </p>
        </div>
        <div className={`status ${state}`}>Gateway: {health?.Status ?? health?.status ?? 'Sin conexión'}</div>
      </section>

      <section className="actions">
        <form onSubmit={createStock} className="card">
          <h2>Preparar stock</h2>
          <input value={stockForm.product} onChange={(e) => setStockForm({ ...stockForm, product: e.target.value })} placeholder="Producto" />
          <input type="number" value={stockForm.availableQuantity} onChange={(e) => setStockForm({ ...stockForm, availableQuantity: Number(e.target.value) })} placeholder="Disponible" />
          <input type="number" value={stockForm.reservedQuantity} onChange={(e) => setStockForm({ ...stockForm, reservedQuantity: Number(e.target.value) })} placeholder="Reservado" />
          <button disabled={state === 'loading'}>Guardar stock</button>
        </form>

        <form onSubmit={createOrder} className="card highlight">
          <h2>Crear orden end-to-end</h2>
          <input value={form.customer} onChange={(e) => setForm({ ...form, customer: e.target.value })} placeholder="Cliente" />
          <input value={form.product} onChange={(e) => setForm({ ...form, product: e.target.value })} placeholder="Producto" />
          <input type="number" min="1" value={form.quantity} onChange={(e) => setForm({ ...form, quantity: Number(e.target.value) })} placeholder="Cantidad" />
          <input type="number" min="0" value={form.total} onChange={(e) => setForm({ ...form, total: Number(e.target.value) })} placeholder="Total" />
          <button disabled={state === 'loading'}>Crear orden</button>
        </form>
      </section>

      {message && <p className={`message ${state}`}>{message}</p>}

      <section className="metrics">
        <Metric title="Órdenes" value={data.orders.length} />
        <Metric title="Stock" value={data.stock.length} />
        <Metric title="Pagos" value={data.payments.length} />
        <Metric title="Facturas" value={data.billing.length} />
        <Metric title="Notificaciones" value={data.notifications.length} />
      </section>

      <section className="grid">
        <Table title="Órdenes" rows={data.orders} columns={['id', 'customer', 'product', 'quantity', 'total', 'status']} />
        <Table title="Saga" rows={data.sagas} columns={['orderId', 'currentStep', 'status', 'updatedAt']} />
        <Table title="Stock" rows={data.stock} columns={['id', 'product', 'availableQuantity', 'reservedQuantity']} />
        <Table title="Pagos" rows={data.payments} columns={['id', 'orderId', 'amount', 'status']} />
        <Table title="Facturación" rows={data.billing} columns={['id', 'orderId', 'amount', 'status']} />
        <Table title="Notificaciones" rows={data.notifications} columns={['id', 'orderId', 'recipient', 'status']} />
      </section>

      <button className="floating" onClick={refresh} disabled={state === 'loading'}>
        {state === 'loading' ? 'Cargando...' : `Refrescar${latestOrder ? ` #${latestOrder.id}` : ''}`}
      </button>
    </main>
  );
}

function Metric({ title, value }: { title: string; value: number }) {
  return <article className="metric"><span>{title}</span><strong>{value}</strong></article>;
}

function Table<T extends Record<string, unknown>>({ title, rows, columns }: { title: string; rows: T[]; columns: string[] }) {
  return (
    <article className="card table-card">
      <h2>{title}</h2>
      <div className="table-wrap">
        <table>
          <thead><tr>{columns.map((column) => <th key={column}>{column}</th>)}</tr></thead>
          <tbody>
            {rows.length === 0 ? (
              <tr><td colSpan={columns.length}>Sin registros todavía.</td></tr>
            ) : rows.slice(0, 8).map((row, index) => (
              <tr key={String(row.id ?? `${title}-${index}`)}>
                {columns.map((column) => <td key={column}>{String(row[column] ?? '-')}</td>)}
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </article>
  );
}

createRoot(document.getElementById('root')!).render(<App />);
