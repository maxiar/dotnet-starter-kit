# Eventing — domain events, integration events, Outbox/Inbox

Read before publishing/handling cross-module events. `src/BuildingBlocks/Eventing/`.

## Two tiers

- **Domain events** (in-process, pre-commit) — inherit `DomainEvent` (record: `EventId`, `OccurredOnUtc`, `CorrelationId`, `TenantId`). Raised on aggregates (`IHasDomainEvents`).
- **Integration events** (cross-module, async) — implement `IIntegrationEvent` (`Id`, `OccurredOnUtc`, `TenantId`, `CorrelationId`, `Source`). Handlers implement `IIntegrationEventHandler<T>` (single `HandleAsync(T, ct)`), are `sealed`, live in `Events/` or `IntegrationEventHandlers/`.

## The Outbox is the only way to publish

**Do not call `IEventBus` directly from a handler.** Publish via the outbox so it commits in the same transaction and survives crashes:

```csharp
await _outboxStore.AddAsync(integrationEvent, ct).ConfigureAwait(false);
```

`EfCoreOutboxStore.AddAsync` serializes + `SaveChanges` immediately. `OutboxDispatcherHostedService` polls every `OutboxDispatchIntervalSeconds` (default 10), `OutboxDispatcher` pulls a batch (`OutboxBatchSize`, default 100), publishes via `IEventBus`, and dead-letters after `OutboxMaxRetries` (default 5) → `IsDead`. `OutboxMessage`/`InboxMessage` are `IGlobalEntity` (no tenant filter — the dispatcher has no tenant context; `TenantId` is an explicit column).

## One store, owned by the framework

`OutboxMessages`/`InboxMessages` live in schema `framework`, owned by `EventingDbContext` (`src/BuildingBlocks/Eventing/Persistence/`) — **not** by any module's context. That is what keeps `IOutboxStore`/`IInboxStore` to a single, non-keyed DI registration: registering them per module DbContext made .NET DI resolve whichever module registered last for the whole application, so a second module publishing broke every module's outbox (issue #1349). `EventingRegistrationTests` guards the registration count; don't add a second one.

`EventingDbContext` derives from `BaseDbContext`, so a tenant with a dedicated database gets its outbox rows in that database, next to the business data they accompany.

## Idempotency is free (in-memory bus)

`InMemoryEventBus` resolves handlers in a fresh DI scope and applies the **Inbox**: skips if `IInboxStore.HasProcessedAsync(eventId, handlerName)`, marks processed after success. Composite key `{Id, HandlerName}`; concurrent-insert race is swallowed. Don't hand-roll dedup.

## Wiring

The **host** bootstraps eventing once (`FSH.Starter.Api/Program.cs` and `FSH.Starter.DbMigrator/Program.cs`, before `AddModules` so `EventingDbInitializer` migrates the `framework` schema first):

```csharp
builder.Services.AddEventingCore(builder.Configuration);   // serializer + bus + dispatcher + EventingDbContext + stores
```

A **module** only registers its handlers:

```csharp
services.AddIntegrationEventHandlers(typeof(MyModule).Assembly);        // scans IIntegrationEventHandler<>
```

There is no per-module store registration — `AddEventingForDbContext<T>` was removed in #1349. A module publishes by injecting `IOutboxStore`; nothing else is needed.

Bus = `EventingOptions.Provider`: `"RabbitMQ"` → `RabbitMqEventBus` (durable topic exchange); else `InMemoryEventBus` (default).

## Gotchas

- **Renaming/moving an integration event type breaks deserialization** — the outbox stores the assembly-qualified type name; `Type.GetType()` returns null → the message dead-letters. Keep event type names/namespaces stable, or migrate dead rows.
- **Background handlers carry no HTTP/tenant context.** An open-generic or background handler that reads a tenant-filtered DbContext must restore Finbuckle context first via `IMultiTenantContextSetter` (see `WebhookFanoutHandler`, `modules/webhooks.md`).
- In-memory bus runs handlers **synchronously in the publisher's scope** — keep handler work minimal; exceptions surface to the originating request (relevant for Notifications consuming Chat events).
- Set `UseHostedServiceDispatcher=false` to drive the outbox via Hangfire instead of the hosted service.
