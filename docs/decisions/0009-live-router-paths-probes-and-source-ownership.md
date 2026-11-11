# ADR 0009: Live router paths, behavioral probes, and source-scoped ownership

- Status: Accepted for r0 implementation
- Date: 2026-11-11

## Context

Live qualification against llama.cpp router `0.9.0-dev` and LiteLLM `1.106.3`
exposed three incorrect or incomplete assumptions:

1. Current router `/models` can use an OpenAI-style `data` array, so `data` does
   not by itself identify a direct server.
2. A container reports `/models/...` while the host agent sees the same file tree
   under another allowlisted mount path.
3. Removing every `llm-glue-*` provider during OpenCode apply lets one artifact
   source erase a provider installed by another source.

The live models also showed that family names and accepted request fields are not
enough to claim reasoning behavior: Qwen and Gemma honored
`reasoning_effort=none`, while GPT-OSS did not disable reasoning for that value.

## Decision

- Router detection uses entry-level status/source evidence and parses nested
  status, argument, memory, context, and provider-confirmed model-path fields.
- `model_path_mappings` explicitly maps provider path prefixes to host paths.
  The mapped result still must canonicalize inside `model_directories`; paths are
  never serialized.
- A loaded llama.cpp route may receive bounded `none` and `medium` probes.
  Binary profiles are published only when distinct off/on response behavior is
  observed. Named effort levels remain unknown without stronger evidence.
- OpenCode ownership schema v2 tracks provider keys and native-provider
  disablements per artifact source URL. V1 state migrates through a legacy scope;
  unowned or cross-source collisions fail closed.

## Consequences

Container deployments remain explicit and safe, current direct/router API shapes
are not conflated, and capability publication follows behavior rather than model
names. Local Ollama and remote central catalogs can coexist in one OpenCode file.
Existing v1 ownership files migrate without deleting their providers. The source
URL stored in the adjacent sidecar contains no credential.
