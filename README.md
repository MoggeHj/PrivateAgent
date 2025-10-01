# PrivateAgent

## Overview
PrivateAgent is a .NET 10 solution composed of:
- Agent (ASP.NET Core web service + AI agent)
- PrivateAgent.AppHost (.NET Aspire orchestration host)
- PrivateAgent.ServiceDefaults (shared service configuration / resilience / telemetry defaults)

## Agent Project (AI Service)
Purpose: Exposes an HTTP service that hosts a model-driven AI agent. The agent selects a model provider at runtime (via environment variables) and can discover and use external tools exposed through the Model Context Protocol (MCP) server launched as a subprocess.

Key Responsibilities:
- Initialize a chat client for the chosen provider (OpenAI, Gemini, Anthropic Claude)
- Discover MCP tools (via `@modelcontextprotocol/server-everything` using `npx`)
- Provide tool/function invocation through `Microsoft.Extensions.AI` abstractions
- Expose web endpoints (ready for future chat or orchestration endpoints)

### Tech Stack (Agent)
- Runtime: .NET 10 / ASP.NET Core
- AI Abstractions: Microsoft.Extensions.AI (referred to here as the external AI abstraction layer)
- Model Providers:
  - OpenAI (`OpenAI.Chat.ChatClient`)
  - Google Gemini (`GeminiDotnet` + `GeminiDotnet.Extensions.AI`)
  - Anthropic Claude (`Anthropic.SDK`)
- Tool Integration: Model Context Protocol (`ModelContextProtocol` packages) over stdio
- Process-Launched MCP Server: `npx -y --verbose @modelcontextprotocol/server-everything`
- Logging: Console logging via `ILoggerFactory`
- API surface: OpenAPI (dev only)
- Configuration: Environment variables (`PROVIDER`, `MODEL`, API keys)
- Azure Key Vault / Secrets (infrastructure readiness): Azure SDK + Aspire emulator packages

### Required Environment Variables
```
PROVIDER=openai|gemini|claude
MODEL=<model-id-or-empty>
OPENAI_API_KEY=...
GEMINI_API_KEY=...
CLAUDE_API_KEY=...
```

## .NET Aspire Components
### AppHost (PrivateAgent.AppHost)
Orchestrates the solution using .NET Aspire patterns. Intended to define and compose resources (services, infrastructure, environment configuration) for local development and potential cloud deployment.

### ServiceDefaults (PrivateAgent.ServiceDefaults)
Centralizes cross-cutting concerns: logging, tracing, metrics, resilience policies, configuration helpers. Referenced by the Agent project to ensure consistent operational behavior.

## How It Works (High Level Flow)
1. Application starts (Agent project).
2. DI registers the selected model provider client based on `PROVIDER` / `MODEL`.
3. An MCP client process is launched; available tools are listed.
4. Tools are exposed to the chat layer (currently as raw MCP tool definitions; an adapter can convert them to `AITool`).
5. Incoming (future) chat interactions can request model responses with tool execution where applicable.

## Running locally
1. Set environment variables in .env file.
2. Run the Agent project: `dotnet run --project Agent` (or launch via AppHost once orchestra