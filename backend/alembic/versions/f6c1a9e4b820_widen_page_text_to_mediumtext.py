"""Widen page/section text columns past MySQL TEXT 64KB limit.

Dense IR PDF pages (e.g. HDFC annual report layouts) can extract >65KB of
text per page. MySQL TEXT rejects those inserts; MEDIUMTEXT (~16MB) does not.
Other dialects keep unlimited Text — this migration is a no-op there.

Revision ID: f6c1a9e4b820
Revises: a1b2c3d4e5f6
Create Date: 2026-09-18

"""

from __future__ import annotations

from typing import Sequence, Union

import sqlalchemy as sa
from alembic import op
from sqlalchemy.dialects import mysql

revision: str = "f6c1a9e4b820"
down_revision: Union[str, None] = "a1b2c3d4e5f6"
branch_labels: Union[str, Sequence[str], None] = None
depends_on: Union[str, Sequence[str], None] = None


def upgrade() -> None:
    bind = op.get_bind()
    if bind.dialect.name != "mysql":
        return

    op.alter_column(
        "document_pages",
        "raw_text",
        existing_type=sa.Text(),
        type_=mysql.MEDIUMTEXT(),
        existing_nullable=True,
    )
    op.alter_column(
        "document_pages",
        "cleaned_text",
        existing_type=sa.Text(),
        type_=mysql.MEDIUMTEXT(),
        existing_nullable=True,
    )
    op.alter_column(
        "document_sections",
        "text",
        existing_type=sa.Text(),
        type_=mysql.MEDIUMTEXT(),
        existing_nullable=True,
    )
    op.alter_column(
        "documents",
        "processing_error",
        existing_type=sa.Text(),
        type_=mysql.MEDIUMTEXT(),
        existing_nullable=True,
    )


def downgrade() -> None:
    bind = op.get_bind()
    if bind.dialect.name != "mysql":
        return

    op.alter_column(
        "documents",
        "processing_error",
        existing_type=mysql.MEDIUMTEXT(),
        type_=sa.Text(),
        existing_nullable=True,
    )
    op.alter_column(
        "document_sections",
        "text",
        existing_type=mysql.MEDIUMTEXT(),
        type_=sa.Text(),
        existing_nullable=True,
    )
    op.alter_column(
        "document_pages",
        "cleaned_text",
        existing_type=mysql.MEDIUMTEXT(),
        type_=sa.Text(),
        existing_nullable=True,
    )
    op.alter_column(
        "document_pages",
        "raw_text",
        existing_type=mysql.MEDIUMTEXT(),
        type_=sa.Text(),
        existing_nullable=True,
    )
