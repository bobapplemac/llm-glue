# ADR 0013: Owned OpenCode built-in catalog policy

Status: accepted  
Date: 2026-11-11

## Context

OpenCode's built-in `opencode` provider contributes a changing catalog of default
models. On a workstation intentionally configured to use LLM Glue routes, those
entries obscure the managed catalog in OpenCode and Paseo. The older
`enabled_providers` and `disabled_providers` configuration inputs remain accepted
but are deprecated by the current OpenCode schema. Current OpenCode policies
support an exact `provider.use` deny for provider ID `opencode`, and a live Paseo
refresh confirmed that it hides those built-in entries while retaining the LLM
Glue provider.

LLM Glue cannot safely insert or remove that statement without distinguishing its
own change from an identical operator policy and unrelated ordered policies.

## Decision

- Every OpenCode client target has `deny_builtin_opencode_provider`, defaulting
  to `true`.
- When enabled, the target appends the schema-valid policy
  `{ "action": "provider.use", "resource": "opencode", "effect": "deny" }`.
- Ownership schema v3 records the canonical managed policy per artifact source.
  V1 and v2 sidecars migrate automatically.
- An identical pre-existing operator policy satisfies the requested state but is
  not claimed when it is the effective last matching decision. If a later
  operator allow overrides it, LLM Glue appends an owned deny. Unrelated policies,
  ordering, and comments are preserved.
- Disabling the option removes one matching statement only when the current
  artifact source owns it. Target apply and ownership-state rollback remain
  transactional.

## Consequences

New and upgraded OpenCode targets show the explicitly managed LLM Glue catalog by
default. Operators who also want OpenCode's built-in catalog set the option to
`false`. The direct-Ollama duplicate suppression remains on its separately
qualified compatibility path until it is deliberately migrated.
