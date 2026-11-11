# ADR 0004: Inventory publication and exclusions

- Status: Accepted for r0 implementation
- Date: 2026-11-11

## Context

Agent, Server, and Client are independently composable. A remote Agent therefore
needs the same authenticated inventory boundary used by an in-process Agent.
Operators must also be able to hide exact deployments without deleting models or
losing diagnostic visibility. Concurrent or delayed submissions must not roll the
catalog backward.

## Decision

1. Agent-only mode requires an explicit `agent.submit_to` server root. Remote
   destinations require a separate write-token environment reference. The Agent
   posts canonical, content-hashed snapshots to `/api/v1/agent/inventory` with
   bounded HTTP/discovery intervals and retains local grace state after failures.
2. Combined Agent+Server uses the same `ApiState` publication boundary in-process
   and rejects a redundant remote destination.
3. The Server serializes generation and publication and rejects snapshots older
   than the visible inventory. Generation or validation failure leaves the prior
   inventory and artifacts intact.
4. `[[server.exclusions]]` rules are exact, conjunctive matches over provider,
   instance, model ID, display name, or provider-qualified identity. There are no
   implicit globs. Excluded deployments remain in inventory/model diagnostics with
   an explicit warning but are absent from generated artifacts. Rules never alter
   or delete provider files.

## Consequences

Initial v1 deployments have one authoritative Agent per Server catalog. True
multi-Agent partial-snapshot aggregation needs an explicit agent identity and
independent leases and is deferred until required by a supported deployment.
Exact matching is predictable and fail-closed; changing a model identity can make
an old exclusion stop matching, which is visible in diagnostics.
