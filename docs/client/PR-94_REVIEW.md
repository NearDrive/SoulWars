# PR-94 Review — CI smoke harness (in-proc preferred)

## Resum executiu

L’implementació actual de PR-94 compleix l’objectiu de **smoke client-server curt, robust i CI-friendly** fent servir un harness **in-proc/in-memory** (sense sockets reals), i el test principal queda cobert per categoria `ClientSmoke` (i també `PR94`) dins del workflow de CI.

## 1) Com s’aixeca el server

El server s’aixeca **dins del mateix procés de test** mitjançant `ServerHost` i configuració determinista:

- `ServerConfig.Default(seed: 9104)` per fixar llavor.
- Ajustos de smoke:
  - `SnapshotEveryTicks = 1`
  - `ArenaMode = true`
  - radi de visió fix (`VisionRadius`, `VisionRadiusSq`)
- Es crea `InMemoryEndpoint` i es connecta amb `host.Connect(endpoint)`.

Això evita dependències de xarxa/SO i redueix molt la variabilitat de CI.

## 2) Com connecta el client (sense dependències externes)

La connexió del client també és in-proc:

- `InMemoryClientTransport` implementa `IClientTransport`.
- `ConnectAsync(...)` és `Task.CompletedTask` (no obre sockets).
- `Send(...)` escriu directament a `InMemoryEndpoint.EnqueueToServer(...)`.
- `TryRead(...)` llegeix de `InMemoryEndpoint.TryDequeueFromServer(...)`.

El runner `HeadlessClientRunner` usa `ClientOptions("inproc", 0, ...)` i s’executa amb `RunAsync(maxTicks: 120, token)`.

## 3) Verificació de preferència in-proc / sockets

### Estat actual

- **Sí, preferència in-proc aplicada.**
- El smoke `ClientServer_Smoke_Arena_BasicRun` no fa servir TCP/UDP ni WebSocket reals.
- Per tant, els requisits de “ports efímers + retries + timeouts de socket” **no apliquen** a aquest test concret.

### Timeouts/retries deterministes

En lloc de timeouts de xarxa, el test imposa control temporal explícit:

- `CancellationTokenSource(TimeSpan.FromSeconds(3))`
- bucle de stepping curt (`host.StepOnce()` + `Task.Delay(1, token)`) fins completar `runTask`
- `maxTicks: 120` al runner

Això acota durada i evita bloquejos indefinits.

## 4) Test `ClientServer_Smoke_Arena_BasicRun`: curt i robust

El test valida de forma estructurada i estable:

- handshake acceptat
- inputs enviats no buits
- ticks d’input ordenats i sense duplicats
- exactly-one cast i camps esperats (`TargetKind`, `TargetEntityId`)
- existeix almenys un hit i concorda `AbilityId`

No depèn de logs ni de timing fràgil de xarxa externa; usa assertions de protocol/resultat.

## 5) Categories i filtre del workflow

### Categories del test

`ClientServer_Smoke_Arena_BasicRun` està marcat amb:

- `Trait("Category", "PR94")`
- `Trait("Category", "ClientSmoke")`
- `Trait("Category", "Canary")`

### Workflow que l’executa

- **Path:** `.github/workflows/ci.yml`
- **Step:** `Canary test lane`
- **Filtre actualitzat:**

```txt
Category=Canary|Category=ClientSmoke|Category=PR94|Category=PR88|Category=PR90
```

Amb aquest filtre, el test entra tant per `ClientSmoke` com explícitament per `PR94`.

## 6) Riscos de flakiness i mitigacions

1. **Dependència de xarxa/ports (evitat)**
   - *Risc:* ports ocupats, latència de runner, firewalls.
   - *Mitigació:* transport in-memory in-proc (sense sockets).

2. **Tests penjats per bucles infinits**
   - *Risc:* deadlock o condicions no assolides.
   - *Mitigació:* `CancellationTokenSource(3s)` + `maxTicks` limitat.

3. **No-determinisme del món de joc**
   - *Risc:* resultats diferents entre execucions.
   - *Mitigació:* seed fixa (`9104`) i asserts sobre invariants estables.

4. **Asserts massa acoblats a timing fi**
   - *Risc:* flaky per diferències de scheduling.
   - *Mitigació:* validació de propietats robustes (ordre de ticks, presència de hit, valors de camp essencials), no comparació de timestamps del sistema.

5. **Cobertura CI accidentalment fora de lane**
   - *Risc:* test etiquetat però no inclòs al filtre.
   - *Mitigació:* categories `ClientSmoke/PR94/Canary` + filtre de workflow incloent `PR94` i `ClientSmoke`.
