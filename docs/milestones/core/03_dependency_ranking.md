# Milestone 3 — Dependency Graph and Dependency-Safe Ranking

## Goal
Implement the core ordering engine.

## Hard invariant
An entity can never be ranked before an entity it depends on.

## Tasks
- Build a directed graph.
- Detect cycles and report the involved path/entities.
- Use topological ordering as the hard constraint.
- Among currently eligible nodes, apply a deterministic priority score.
- Begin with an explainable metric such as transitive downstream dependents/impact.
- Return rank plus useful metadata such as direct dependencies, dependents, and impact score.
- Recompute the entire ordering whenever graph structure changes.
- Test chains, diamonds, disconnected components, ties, isolated nodes, cycles, and generated DAGs.

## Design note
PageRank can later be tested as an optional priority score, but it must never violate the topological constraint.

## Acceptance criteria
- Every dependency precedes its dependent.
- Same input gives the same ordering.
- Cycles produce diagnostics rather than a fake ordering.
- Ranking is a pure operation with no UI or persistence dependency.
