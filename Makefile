.PHONY: dev watch test stop help

help:
	@echo "MedTracker Developer Commands:"
	@echo "  make dev   - Start local Supabase, apply migrations & seed, and launch Desktop app"
	@echo "  make watch - Same as dev, but launches client with hot-reload (dotnet watch)"
	@echo "  make test  - Run all xUnit v3 unit & integration tests"
	@echo "  make stop  - Stop local Supabase containers"

dev:
	@./run.sh dev

watch:
	@./run.sh watch

test:
	@./run.sh test

stop:
	@./run.sh stop
