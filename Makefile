.PHONY: setup dev build backend frontend agent kb-scraper infra api-generate test test-backend test-frontend test-agent test-kb-scraper help

setup: ## Install all deps (backend + frontend)
	dotnet restore apps/backend
	bash -c 'set -a && source .env && set +a && cd apps/frontend && npm install'

dev: ## Start infra, backend, and frontend in parallel
	$(MAKE) infra & $(MAKE) backend & $(MAKE) frontend & wait

build: ## Build backend and frontend for production
	dotnet publish apps/backend/BotFactory.Api -c Release
	cd apps/frontend && npm run build

backend: ## Run the .NET backend API
	dotnet run --project apps/backend/BotFactory.Api

frontend: ## Run the Angular frontend
	cd apps/frontend && npm run dev

agent: ## Run the LiveKit agent
	cd apps/agent && uv run python -m bot_factory_agent dev

kb-scraper: ## Run the KB Playwright scraper service
	dotnet run --project apps/kb-scraper/BotFactory.KbScraper

infra: ## Start dev infrastructure (SQL Server, Redis, Garage, LiveKit)
	docker compose -f docker-compose.dev.yml up

api-generate: ## Regenerate frontend API client from OpenAPI spec
	cd apps/frontend && npm run api:generate

test-backend: ## Run backend xUnit tests
	dotnet test apps/backend/BotFactory.Tests

test-frontend: ## Run frontend Vitest (ng test)
	cd apps/frontend && npm test -- --watch=false

test-agent: ## Run agent pytest suite
	cd apps/agent && uv run pytest

test-kb-scraper: ## Run kb-scraper xUnit tests (mocked render, no Chromium)
	dotnet test apps/kb-scraper/BotFactory.KbScraper.Tests

test: test-backend test-kb-scraper test-frontend test-agent ## Run backend, scraper, frontend, and agent tests

help: ## Show this help
	@grep -E '^[a-zA-Z_-]+:.*?## .*$$' $(MAKEFILE_LIST) | sort | awk 'BEGIN {FS = ":.*?## "}; {printf "\033[36m%-16s\033[0m %s\n", $$1, $$2}'

.DEFAULT_GOAL := help
