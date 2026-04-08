# Ryanair Payments Simulator

A **.NET Framework 4.8** ASP.NET Web API application that simulates an airline payments processing system. Designed to generate realistic synthetic payment traffic for observability testing (New Relic / OpenTelemetry instrumentation to be added separately).

## Architecture

```
┌─────────────────────────────────────────────────────────┐
│  Windows Container (ltsc2022 / IIS)                      │
│                                                          │
│  ┌─────────────────────────┐   ┌──────────────────────┐ │
│  │   ASP.NET Web API        │   │ SyntheticTraffic     │ │
│  │   PaymentsController     │   │ Generator (Timer)    │ │
│  │   HealthController       │   │ ~every 3-7 seconds   │ │
│  └────────────┬────────────┘   └──────────┬───────────┘ │
│               │                            │             │
│               └──────────┬─────────────────┘             │
│                          ▼                               │
│                ┌─────────────────┐                       │
│                │  PaymentService  │                       │
│                │  (in-memory,     │                       │
│                │  thread-safe)    │                       │
│                └─────────────────┘                       │
└─────────────────────────────────────────────────────────┘
```

### Synthetic Traffic

The generator fires every **3–7 seconds** and submits **1–3 payments** per tick, simulating:

| Type             | Description                          | Approx Share |
|------------------|--------------------------------------|-------------|
| `FlightBooking`  | Initial seat + tax payment           | 60%         |
| `Ancillary`      | Baggage, priority boarding, seats    | 25%         |
| `Refund`         | Cancellation refunds                 | 10%         |
| `Amendment`      | Itinerary change fees                | 5%          |

Payment outcomes: ~82% Captured, ~10% Declined, ~5% Refunded, ~3% Pending.

Routes span real Ryanair European markets: `DUB↔STN`, `DUB↔BCN`, `STN↔MAD`, `WRO↔STN`, `KRK↔STN`, etc.

---

## REST API

| Method | Endpoint                 | Description                          |
|--------|--------------------------|--------------------------------------|
| GET    | `/api/health`            | Health check (container liveness)    |
| GET    | `/api/payments`          | List recent payments (`?limit=50`)   |
| GET    | `/api/payments/{id}`     | Get payment by ID (GUID)             |
| POST   | `/api/payments`          | Submit a manual payment              |
| GET    | `/api/payments/stats`    | Aggregated stats by status/method    |

### Example: POST /api/payments

```json
{
  "bookingReference": "FR-ABC123",
  "amount": 89.99,
  "currency": "EUR",
  "method": "Visa",
  "type": "FlightBooking",
  "origin": "DUB",
  "destination": "BCN",
  "passengerName": "Jane Smith",
  "passengerCount": 1
}
```

---

## Building

### ⚠️ Mac Users — Important

**.NET Framework 4.8 requires Windows containers**, which cannot be built on macOS directly. The recommended approaches are:

#### Option A: GitHub Actions (recommended)

1. Push this repo to GitHub
2. Add secrets: `DOCKER_USERNAME = aaronkinchen`, `DOCKER_TOKEN = <your PAT>`
3. The workflow at `.github/workflows/docker-build-push.yml` builds on a `windows-latest` runner and pushes to Docker Hub automatically

#### Option B: Windows Machine / VM

```powershell
# In Windows PowerShell (Docker in Windows container mode)
.\build.ps1 -Push
```

#### Option C: Remote Docker Context

Point your local Docker CLI at a remote Windows host:

```bash
docker context create win-remote --docker "host=tcp://<windows-host>:2376,cert=<cert-path>"
docker context use win-remote
docker buildx build -t aaronkinchen/ryanair-payments:latest --push .
```

---

## Running

Once the image is in Docker Hub, pull and run from any Windows Docker host:

```powershell
docker pull aaronkinchen/ryanair-payments:latest
docker run -d -p 8080:80 --name ryanair-payments aaronkinchen/ryanair-payments:latest
```

Or with Docker Compose (Windows container mode):

```powershell
docker compose up -d
```

Test the API:

```bash
curl http://localhost:8080/api/health
curl http://localhost:8080/api/payments
curl http://localhost:8080/api/payments/stats
```

---

## Project Structure

```
dotnet-payments/
├── .github/workflows/docker-build-push.yml   # CI/CD build & push
├── src/
│   ├── RyanairPayments.sln
│   └── RyanairPayments/
│       ├── App_Start/WebApiConfig.cs          # Route + JSON config
│       ├── Controllers/
│       │   ├── HealthController.cs
│       │   └── PaymentsController.cs
│       ├── Models/
│       │   ├── Payment.cs
│       │   ├── PaymentEnums.cs
│       │   ├── PaymentRequest.cs
│       │   └── PaymentStats.cs
│       ├── Services/
│       │   ├── IPaymentService.cs
│       │   ├── PaymentService.cs              # Thread-safe in-memory store
│       │   └── SyntheticTrafficGenerator.cs   # Background traffic pump
│       ├── Global.asax / Global.asax.cs       # App startup
│       ├── Web.config
│       ├── packages.config
│       └── RyanairPayments.csproj
├── Dockerfile
├── docker-compose.yml
└── build.ps1                                  # Manual Windows build script
```

---

## Observability Notes

This app is intentionally instrumentation-free. Future additions:
- **New Relic .NET Agent** — attach via environment variables or installer in Dockerfile
- **OpenTelemetry** — add `OpenTelemetry.AutoInstrumentation` or manual SDK instrumentation
- Key spans to instrument: payment creation, status transitions, external processor calls
