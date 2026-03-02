# PR-91 Review — Handshake + receive loop only

## Scope reviewed
Validació estricta de si la implementació atribuïble a PR-91 queda dins de l’abast:

1. Nou projecte `src/Game.Client.Headless`.
2. `ProjectReference` a `Game.Protocol` (sense duplicar DTOs).
3. Handshake amb `ProtocolVersion=1` i gestió `accept/reject`.
4. Receive loop mínim que deserialitza envelopes existents de `Snapshot/Delta`.
5. Logs deterministes (sense timestamps, GUIDs, `DateTime.Now`).
6. Tests de handshake (`Category=PR91` o `ClientSmoke`) executats a CI via filtre.

---

## Paths exactes dels fitxers PR-91 (rellevants al scope)

- `src/Game.Client.Headless/Game.Client.Headless.csproj`
- `src/Game.Client.Headless/Program.cs`
- `src/Game.Client.Headless/HeadlessClientRunner.cs`
- `src/Game.Client.Headless/TcpClientTransport.cs`
- `src/Game.Client.Headless/IClientTransport.cs`
- `src/Game.Client.Headless/ClientOptions.cs`
- `src/Game.Client.Headless/ClientWorldView.cs`
- `Game.sln`
- `tests/Game.Server.Tests/Game.Server.Tests.csproj`
- `tests/Game.Server.Tests/ClientSmoke/ClientMvpC1Tests.cs`
- `tests/Game.Server.Tests/ClientSmoke/InMemoryClientTransport.cs`
- `.github/workflows/ci.yml`

---

## Evidence bullets per requisit

### 1) Nou projecte `src/Game.Client.Headless`
- El projecte existeix com a executable .NET (`OutputType=Exe`) i target `net8.0`. Implementat a `Game.Client.Headless.csproj`.
- També està inclòs a la solució (`Game.sln`) i referenciat als tests per validar integració de smoke.

### 2) `ProjectReference` a `Game.Protocol` (sense duplicar DTOs)
- `src/Game.Client.Headless/Game.Client.Headless.csproj` referencia `../Game.Protocol/Game.Protocol.csproj`.
- El client usa tipus protocol (`HandshakeRequest`, `Welcome`, `Disconnect`, `SnapshotV2`, `ProtocolCodec`) des de `Game.Protocol`.
- No hi ha definicions DTO noves dins `src/Game.Client.Headless` (només lògica client/transport/opcions/view).

### 3) Handshake `ProtocolVersion=1` + accept/reject
- El valor per defecte de protocol és `1` a `ClientOptions.Parse`.
- El runner envia `HandshakeRequest(_options.ProtocolVersion, _options.AccountId)`.
- Acceptació: en rebre `Welcome`, marca `HandshakeAccepted = true` i loga `welcome protocol=1`.
- Rebuig: en rebre `Disconnect`, retorna resultat amb log de motiu.
- Tests explícits:
  - `ClientHandshake_AcceptsV1` (`Category=PR91`, `ClientSmoke`) valida `Welcome.ProtocolVersion`.
  - `ClientHandshake_RejectsUnknownVersion` (`Category=PR91`, `ClientSmoke`) valida `DisconnectReason.VersionMismatch`.

### 4) Receive loop mínim per envelopes Snapshot/Delta
- La recepció està implementada via `IClientTransport.TryRead(...)` + `ProtocolCodec.DecodeServer(payload)` dins un bucle while.
- L’aplicació de snapshot/delta existeix via `ClientWorldView.ApplySnapshot(SnapshotV2)`:
  - si `IsFull=true`, reemplaça estat;
  - si `IsFull=false`, aplica `Leaves/Enters/Updates` (delta existent del protocol).

**Valoració severa:** **Parcialment fora de l’estrictament mínim** perquè el runner no només “rep i deserialitza” sinó que també envia `EnterZoneRequestV2`, `ClientAckV2`, `InputCommand`, `CastSkillCommand` i incorpora flow de combat/hit.

### 5) Logs deterministes
- Els logs són cadenes literals/camp numèric derivat d’estat protocol (`tick`, `zone`, `entity`, `hp`, `ability`, etc.).
- No s’observa ús de timestamps, GUIDs ni `DateTime.Now` als fitxers client revisats.

### 6) Tests handshake filtrables a CI
- Els tests de handshake estan marcats amb `Trait("Category", "PR91")` i `Trait("Category", "ClientSmoke")`.
- El workflow CI executa filtre que inclou `Category=ClientSmoke`, per tant aquests tests entren a pipeline.

---

## Violations (fora de scope “Handshake + receive loop only”)

1. **Runner amb funcionalitat addicional no mínima**
   - Envia `EnterZoneRequestV2` i `ClientAckV2` immediatament després de handshake.
   - Envia `InputCommand` periòdic.
   - Envia `CastSkillCommand` i espera `HitEventV1` per finalitzar amb èxit.
   - Això correspon més a smoke de combat que a PR-91 estrictament “handshake + receive loop”.

2. **State dump/canonical dump inclòs al mateix paquet de client**
   - `ClientWorldView.DumpCanonical()` i test `ClientStateDump_IsCanonical` estan etiquetats com `PR92`; funcionalment és extra respecte PR-91.

3. **Smoke test arena run fora d’abast PR-91**
   - `ClientServer_Smoke_Arena_BasicRun` (`Category=PR92`) valida cast/hit i pas de ticks; excedeix scope PR-91.

4. **Transport TCP propi**
   - Tot i ser acceptable arquitectònicament, per una lectura estricta de “no inventar transport/protocol” el framing client TCP propi és una capa addicional. (No duplica protocol, però sí implementa detall de transport.)

---

## Fix mínim aplicat

**No s’ha aplicat cap fix de codi** en aquesta revisió, perquè:
- Els requisits crítics de PR-91 (projecte, referència a protocol, handshake accept/reject, loop de recepció, logs deterministes i test filtrable a CI) ja tenen evidència.
- Les desviacions detectades s’han documentat com a *violations* de scope, però no bloquegen la comprovació del mínim requerit.

Si es vol tancar drift de scope en un pas següent, el fix mínim recomanat seria separar la lògica “move/cast/hit” a PR posterior i deixar `HeadlessClientRunner` PR-91 només amb handshake + recepció/aplicació snapshots.


## Comprovació actual (post-review)

- S’ha intentat validar l’execució real dels tests PR-91 amb:
  - `dotnet test tests/Game.Server.Tests/Game.Server.Tests.csproj -c Release --filter "Category=PR91"`
- Resultat: no s’ha pogut executar en aquest entorn perquè `dotnet` no està instal·lat (`bash: command not found: dotnet`).
- Per tant, la confirmació de compliment és **estàtica** (codi + tags + filtre CI) i no d’execució local en aquesta sessió.

