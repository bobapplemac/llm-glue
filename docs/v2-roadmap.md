# v2 roadmap (non-binding)

V2 may add explicitly authorized remote operations for downloading models,
assigning deployments, changing runtime settings, activating providers, reporting
progress, and handling partial failure. That work requires separate authorization,
auditable idempotent operation IDs, capacity planning, cancellation, rollback,
and a stronger control-plane threat model.

None of those management operations are implemented or exposed by v1. Existing
provider, enricher, probe, and target boundaries are intended to allow extension
without turning current read/sync endpoints into a generic orchestration API.
