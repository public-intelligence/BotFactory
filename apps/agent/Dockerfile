# Python LiveKit worker + GET /healthz (see bot_factory_agent/health.py).
# Debian trixie-slim: livekit-rtc ARM64 FFI needs glibc >= 2.38 (__arm_tpidr2_save).

FROM python:3.12-slim-trixie
COPY --from=ghcr.io/astral-sh/uv:latest /uv /uvx /bin/
WORKDIR /app
# Install dependencies first (cached unless pyproject.toml or uv.lock change).
COPY pyproject.toml uv.lock README.md ./
RUN uv sync --no-dev --frozen --no-install-project
# Copy source and install the project itself.
COPY src ./src
RUN uv sync --no-dev --frozen
RUN uv run python -m bot_factory_agent download-files
ENV PYTHONUNBUFFERED=1
# HEALTH_PORT default; LiveKit worker uses its own signaling port from env.
EXPOSE 8089
CMD ["uv", "run", "python", "-m", "bot_factory_agent", "start"]
