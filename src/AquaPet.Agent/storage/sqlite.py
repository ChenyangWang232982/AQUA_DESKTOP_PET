"""SQLite conversation persistence for the local AquaPet agent."""

from __future__ import annotations

import os
import sqlite3
from datetime import datetime, timezone
from pathlib import Path
from typing import Literal


MessageFrom = Literal["user", "model"]


def _default_database_path() -> Path:
    """Keep mutable application data outside the installed project directory."""
    local_app_data = os.getenv("LOCALAPPDATA")
    if local_app_data:
        return Path(local_app_data) / "AquaPet" / "data" / "aquapet.db"
    return Path.home() / ".aquapet" / "data" / "aquapet.db"


class MessageStore:
    """Store AquaPet messages in one local SQLite database file."""

    def __init__(self, database_path: str | Path | None = None) -> None:
        self._database_path = Path(
            database_path or os.getenv("AQUAPET_DATABASE_PATH", _default_database_path())
        )

    def _connect(self) -> sqlite3.Connection:
        self._database_path.parent.mkdir(parents=True, exist_ok=True)
        return sqlite3.connect(self._database_path)

    def initialize(self) -> None:
        """Create the small message table once; safe to call on every startup."""
        with self._connect() as connection:
            connection.execute(
                '''
                CREATE TABLE IF NOT EXISTS messages (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    "from" TEXT NOT NULL CHECK ("from" IN ('user', 'model')),
                    content TEXT NOT NULL,
                    date TEXT NOT NULL
                )
                '''
            )

    def save(self, sender: MessageFrom, content: str) -> datetime:
        """Append one message and return the timezone-aware time saved."""
        if sender not in ("user", "model"):
            raise ValueError(f"Unsupported message sender: {sender}")

        saved_at = datetime.now(timezone.utc)
        with self._connect() as connection:
            connection.execute(
                '''
                INSERT INTO messages ("from", content, date)
                VALUES (?, ?, ?)
                ''',
                (sender, content, saved_at.isoformat()),
            )
        return saved_at

    def load_messages(self) -> list[dict[str, str]]:
        """Read the full local conversation chronologically for the model prompt."""
        with self._connect() as connection:
            rows = connection.execute(
                '''
                SELECT "from", content
                FROM messages
                ORDER BY date ASC, id ASC
                '''
            ).fetchall()

        return [
            {
                "role": "user" if sender == "user" else "assistant",
                "content": content,
            }
            for sender, content in rows
        ]
