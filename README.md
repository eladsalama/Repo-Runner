# RepoRunner

RepoRunner is a local-first tool that lets you clone any **public GitHub repo** with a Dockerfile, **build it**, and **run it** inside an **isolated Kubernetes sandbox** on your own machine. A built-in chat (“Ask the Repo”) answers questions about the codebase (tech stack, data flow, caching) using local RAG — no paid services required.

## Why
Showcase the ability to run arbitrary projects safely, not just my own, and to reason about unfamiliar repos quickly.

## Tech Stack (MVP)
- **Browser Extension**: TypeScript (Manifest V3), gRPC-Web to the gateway
- **Backend**: .NET 8 services (Gateway + Orchestrator + Workers) with **gRPC**
- **Events**: **Redpanda** (Kafka-API compatible)
- **Data**: **MongoDB Community** (metadata, chat), **Redis Stack** (cache + vector search)
- **Runtime**: **Kubernetes** via **kind/k3d** (per-run namespaces, quotas, TTL cleanup)
- **Automations**: **n8n** for re-index and housekeeping
- **Observability**: **OpenTelemetry** (traces/metrics/logs)
- **LLM/RAG**: **Ollama** locally (e.g., Llama 3.x 8B) + small open-source embeddings

## Status
Work in progress (MVP target): one-click “Run Locally” for a single-container HTTP app, basic logs, and “Ask the Repo” over README + Dockerfile with citations.

