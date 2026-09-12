import React, { useCallback, useEffect, useMemo, useState } from 'react';
import { createRoot } from 'react-dom/client';
import './styles.css';

type HealthResponse = {
  service?: string;
  Service?: string;
  status?: string;
  Status?: string;
};

type Order = {
  id: number;
  customer: string;
  product: string;
  quantity: number;
  total: number;
  status: string;
};

type ProductStock = {
  id: number;
  product: string;
  availableQuantity: number;
  reservedQuantity: number;
};

type Payment = {
  id: number;
  orderId: number;
  amount: number;
  status: string;
};

type BillingRecord = {
  id: number;
  orderId: number;
  amount: number;
  status: string;
};

type Notification = {
  id: number;
  orderId: number;
  recipient: string;
  message: string;
  status: string;
};

type Saga = {
  id: number;
  orderId: number;
  currentStep: string;
  status: string;
  updatedAt: string;
};

type LoadState = 'idle' | 'loading' | 'success' | 'error';
type FormSubmitEvent = {
  preventDefault: () => void;
};

type InputChangeEvent = {
  target: HTMLInputElement;
};
type OrderForm = {
  customer: string;
  product: string;
  quantity: number;
  total: number;
};

type StockForm = {
  product: string;
  availableQuantity: number;
  reservedQuantity: number;
};

type DashboardData = {
  orders: Order[];
  stock: ProductStock[];
  payments: Payment[];
  billing: BillingRecord[];
  notifications: Notification[];
  sagas: Saga[];
};

type TableColumn<T> = keyof T & string;

const apiBaseUrl = import.meta.env.VITE_API_BASE_URL ?? '/api';

const emptyData: DashboardData = {
  orders: [],
  stock: [],
  payments: [],
  billing: [],
  notifications: [],
  sagas: []
};

const initialOrderForm: OrderForm = {
  customer: 'Cliente Demo',
  product: 'Laptop',
  quantity: 1,
  total: 1299
};

const initialStockForm: StockForm = {
  product: 'Laptop',
  availableQuantity: 25,
  reservedQuantity: 0
};

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(`${apiBaseUrl}${path}`, {
    ...init,
    headers: {
      'Content-Type': 'application/json',
      ...init?.headers
    }
  });

  if (!response.ok) {
    const message = await response.text();
    throw new Error(message || `Error HTTP ${response.status}`);
  }

  if (response.status === 204) {
    return undefined as T;
  }

  return response.json() as Promise<T>;
}

function App() {
  const [data, setData] = useState<DashboardData>(emptyData);
  const [health, setHealth] = useState<HealthResponse | null>(null);
  const [state, setState] = useState<LoadState>('idle');
  const [message, setMessage] = useState('');
  const [orderForm, setOrderForm] = useState<OrderForm>(initialOrderForm);
  const [stockForm, setStockForm] = useState<StockForm>(initialStockForm);

  const isLoading = state === 'loading';
  const gatewayStatus = health?.Status ?? health?.status ?? 'Sin conexión';
  const latestOrder = useMemo(() => data.orders[0], [data.orders]);

  const refresh = useCallback(async () => {
    setState('loading');

    try {
      const [
        gatewayHealth,
        orders,
        stock,
        payments,
        billing,
        notifications,
        sagas
      ] = await Promise.all([
        request<HealthResponse>('/gateway/health'),
        request<Order[]>('/orders/'),
        request<ProductStock[]>('/stock/'),
        request<Payment[]>('/payments/'),
        request<BillingRecord[]>('/billing/'),
        request<Notification[]>('/notifications/'),
        request<Saga[]>('/order-sagas/')
      ]);

      setHealth(gatewayHealth);
      setData({
        orders,
        stock,
        payments,
        billing,
        notifications,
        sagas
      });
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

  function updateOrderField<K extends keyof OrderForm>(field: K, value: OrderForm[K]) {
    setOrderForm((current) => ({
      ...current,
      [field]: value
    }));
  }

  function updateStockField<K extends keyof StockForm>(field: K, value: StockForm[K]) {
    setStockForm((current) => ({
      ...current,
      [field]: value
    }));
  }

  async function createOrder(event: FormSubmitEvent) {
    event.preventDefault();
    setState('loading');

    try {
      await request<Order>('/orders/', {
        method: 'POST',
        body: JSON.stringify({
          ...orderForm,
          status: 'Pending'
        })
      });

      setMessage('Orden creada. RabbitMQ, Saga y Outbox deberían procesar el flujo automáticamente.');
      await refresh();
    } catch (error) {
      setState('error');
      setMessage(error instanceof Error ? error.message : 'No se pudo crear la orden.');
    }
  }

  async function createStock(event: FormSubmitEvent) {
    event.preventDefault();
    setState('loading');

    try {
      await request<ProductStock>('/stock/', {
        method: 'POST',
        body: JSON.stringify(stockForm)
      });

      setMessage('Stock creado o actualizado para pruebas.');
      await refresh();
    } catch (error) {
      setState('error');
      setMessage(error instanceof Error ? error.message : 'No se pudo crear el stock.');
    }
  }

  return (
    <main className="shell">
      <nav className="topbar" aria-label="Navegación principal">
        <a className="brand" href="#top" aria-label="Nexus Commerce, inicio">
          <span className="brand-mark">N</span>
          <span>NEXUS <b>COMMERCE</b></span>
        </a>
        <div className="nav-links">
          <a className="active" href="#overview">Overview</a>
          <a href="#operations">Operaciones</a>
          <a href="#activity">Actividad</a>
        </div>
        <div className="nav-actions">
          <span className={`connection-dot ${state}`} aria-hidden="true" />
          <span className="gateway-label">Gateway: {gatewayStatus}</span>
          <span className="avatar">AD</span>
        </div>
      </nav>

      <section className="hero" id="top">
        <div className="hero-copy">
          <p className="eyebrow"><span /> Commerce command center</p>
          <h1>Tu ecosistema de<br /><em>commerce, conectado.</em></h1>
          <p className="hero-description">
            Controla operaciones, inventario y el recorrido completo de cada orden
            desde un único centro de mando.
          </p>
          <div className="hero-actions">
            <a className="primary-action" href="#operations">Nueva operación <span>→</span></a>
            <button className="text-action" onClick={refresh} disabled={isLoading}>
              {isLoading ? 'Sincronizando...' : 'Sincronizar datos'}
            </button>
          </div>
        </div>

        <div className="stack-card" aria-label="Distribución del ecosistema tecnológico">
          <div className="stack-heading">
            <div>
              <span>Arquitectura actual</span>
              <strong>Tech ecosystem</strong>
            </div>
            <span className="live-badge">● LIVE</span>
          </div>
          <div className="stack-total">
            <strong>5</strong>
            <span>plataformas<br />integradas</span>
          </div>
          <div className="stacked-bar" aria-hidden="true">
            <span className="shopify" /><span className="sap" /><span className="adobe" />
            <span className="amazon" /><span className="observability" />
          </div>
          <div className="stack-list">
            <StackItem className="shopify" name="Shopify Plus" value="35%" />
            <StackItem className="sap" name="SAP Commerce Cloud" value="25%" />
            <StackItem className="adobe" name="Adobe Commerce (Magento)" value="20%" />
            <StackItem className="amazon" name="Amazon Seller Central" value="10%" />
            <StackItem className="observability" name="Grafana / Langfuse" value="10%" />
          </div>
        </div>
      </section>

      <section className="overview" id="overview">
        <div className="section-heading">
          <div><span className="section-kicker">Visión general</span><h2>Operación en tiempo real</h2></div>
          <span className={`status ${state}`}><i /> {state === 'error' ? 'Conexión interrumpida' : 'Sistemas operativos'}</span>
        </div>
        <div className="metrics">
          <Metric title="Órdenes" value={data.orders.length} accent="violet" icon="↗" />
          <Metric title="Inventario" value={data.stock.length} accent="cyan" icon="◇" />
          <Metric title="Pagos" value={data.payments.length} accent="green" icon="$" />
          <Metric title="Facturas" value={data.billing.length} accent="orange" icon="▤" />
          <Metric title="Notificaciones" value={data.notifications.length} accent="pink" icon="◉" />
        </div>
      </section>

      <section className="operations" id="operations">
        <div className="section-heading compact">
          <div><span className="section-kicker">Acciones rápidas</span><h2>Gestiona tu operación</h2></div>
        </div>
        <div className="actions">
        <form onSubmit={createStock} className="card">
          <div className="card-title"><span className="card-icon cyan">◇</span><div><h3>Preparar stock</h3><p>Actualiza las existencias de un producto.</p></div></div>

          <input
            value={stockForm.product}
            onChange={(event: InputChangeEvent) => updateStockField('product', event.target.value)}            placeholder="Producto"
          />

          <input
            type="number"
            value={stockForm.availableQuantity}
            onChange={(event: InputChangeEvent) => updateStockField('availableQuantity', Number(event.target.value))}            placeholder="Disponible"
          />

          <input
            type="number"
            value={stockForm.reservedQuantity}
            onChange={(event: InputChangeEvent) => updateStockField('reservedQuantity', Number(event.target.value))}            placeholder="Reservado"
          />

          <button className="submit-button" disabled={isLoading}>
            Guardar stock <span>→</span>
          </button>
        </form>

        <form onSubmit={createOrder} className="card highlight">
          <div className="card-title"><span className="card-icon violet">↗</span><div><h3>Crear orden end-to-end</h3><p>Inicia un nuevo flujo en todo el ecosistema.</p></div></div>

          <input
            value={orderForm.customer}
            onChange={(event: InputChangeEvent) => updateOrderField('customer', event.target.value)}            placeholder="Cliente"
          />

          <input
            value={orderForm.product}
            onChange={(event: InputChangeEvent) => updateOrderField('product', event.target.value)}            placeholder="Producto"
          />

          <input
            type="number"
            min="1"
            value={orderForm.quantity}
            onChange={(event: InputChangeEvent) => updateOrderField('quantity', Number(event.target.value))}            placeholder="Cantidad"
          />

          <input
            type="number"
            min="0"
            value={orderForm.total}
            onChange={(event: InputChangeEvent) => updateOrderField('total', Number(event.target.value))}            placeholder="Total"
          />

          <button className="submit-button" disabled={isLoading}>
            Crear orden <span>→</span>
          </button>
        </form>
        </div>
      </section>

      {message && (
        <p className={`message ${state}`}>
          {message}
        </p>
      )}

      <section className="activity" id="activity">
        <div className="section-heading compact">
          <div><span className="section-kicker">Datos conectados</span><h2>Actividad del ecosistema</h2></div>
        </div>
        <div className="grid">
        <Table
          title="Órdenes"
          rows={data.orders}
          columns={['id', 'customer', 'product', 'quantity', 'total', 'status']}
        />

        <Table
          title="Saga"
          rows={data.sagas}
          columns={['orderId', 'currentStep', 'status', 'updatedAt']}
        />

        <Table
          title="Stock"
          rows={data.stock}
          columns={['id', 'product', 'availableQuantity', 'reservedQuantity']}
        />

        <Table
          title="Pagos"
          rows={data.payments}
          columns={['id', 'orderId', 'amount', 'status']}
        />

        <Table
          title="Facturación"
          rows={data.billing}
          columns={['id', 'orderId', 'amount', 'status']}
        />

        <Table
          title="Notificaciones"
          rows={data.notifications}
          columns={['id', 'orderId', 'recipient', 'status']}
        />
        </div>
      </section>

      <button className="floating" onClick={refresh} disabled={isLoading}>
        {isLoading ? 'Cargando...' : `Refrescar${latestOrder ? ` #${latestOrder.id}` : ''}`}
      </button>
    </main>
  );
}

function StackItem({ className, name, value }: { className: string; name: string; value: string }) {
  return <div className="stack-item"><span className={`legend-dot ${className}`} /><span>{name}</span><strong>{value}</strong></div>;
}

function Metric({ title, value, accent, icon }: { title: string; value: number; accent: string; icon: string }) {
  return (
    <article className="metric">
      <div className={`metric-icon ${accent}`}>{icon}</div>
      <span>{title}</span>
      <strong>{String(value).padStart(2, '0')}</strong>
      <small>Datos sincronizados</small>
    </article>
  );
}

function Table<T extends Record<string, unknown>>({
  title,
  rows,
  columns
}: {
  title: string;
  rows: T[];
  columns: TableColumn<T>[];
}) {
  return (
    <article className="card table-card">
      <div className="table-title"><h3>{title}</h3><span>{rows.length} registros</span></div>

      <div className="table-wrap">
        <table>
          <thead>
            <tr>
              {columns.map((column) => (
                <th key={column}>{column}</th>
              ))}
            </tr>
          </thead>

          <tbody>
            {rows.length === 0 ? (
              <tr>
                <td colSpan={columns.length}>Sin registros todavía.</td>
              </tr>
            ) : (
              rows.slice(0, 8).map((row, index) => (
                <tr key={String(row.id ?? `${title}-${index}`)}>
                  {columns.map((column) => (
                    <td key={column}>{formatCell(row[column])}</td>
                  ))}
                </tr>
              ))
            )}
          </tbody>
        </table>
      </div>
    </article>
  );
}

function formatCell(value: unknown) {
  if (value === null || value === undefined || value === '') {
    return '-';
  }

  return String(value);
}

createRoot(document.getElementById('root')!).render(<App />);
