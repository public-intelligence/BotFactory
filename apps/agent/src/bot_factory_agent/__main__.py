"""python -m bot_factory_agent → LiveKit worker + health server."""

from __future__ import annotations

from livekit.agents import cli

from bot_factory_agent.agent import server
from bot_factory_agent.health import start_health_server
from bot_factory_agent.logging_config import configure_logging

# Apply format/level to the whole process before the health thread or LiveKit worker emits logs.
configure_logging()

# Runs in a daemon thread; LiveKit worker runs on the main thread.
start_health_server()
cli.run_app(server)
