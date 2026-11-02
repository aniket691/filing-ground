"""Upload sample PDFs, process them, and run RAG smoke queries."""

from __future__ import annotations

import json
import time
import uuid
from pathlib import Path

import httpx

BASE = "http://127.0.0.1:8000/api/v1"
DOCS = Path(__file__).resolve().parent
PDFS = [
    "novatech_fy2024_10k.pdf",
    "meridian_bank_q2_2025_earnings.pdf",
    "greenforge_energy_risk_factors.pdf",
    "cascade_retail_10q.pdf",
    "orion_aerospace_fy2024_10k.pdf",
]
QUERIES = [
    ("What was NovaTech total net revenue in fiscal 2024?", "2.847"),
    (
        "What was Meridian Community Bancorp net interest margin NIM in Q2 2025?",
        "3.41",
    ),
    ("What was GreenForge fiscal 2024 revenue?", "1.156"),
    ("How many stores did Cascade Retail operate?", "438"),
    ("What was Orion Aerospace FY2024 total revenue?", "1.092"),
]


def main() -> None:
    email = f"pdf.test.{uuid.uuid4().hex[:8]}@example.com"
    password = "testpass123"
    with httpx.Client(timeout=120.0) as client:
        reg = client.post(f"{BASE}/auth/register", json={"email": email, "password": password})
        print("register", reg.status_code, reg.text[:200])
        if reg.status_code not in (200, 201):
            raise SystemExit("register failed")

        login = client.post(f"{BASE}/auth/login", json={"email": email, "password": password})
        login.raise_for_status()
        token = login.json()["access_token"]
        headers = {"Authorization": f"Bearer {token}"}
        print("login ok", email)

        doc_ids: list[int] = []
        for name in PDFS:
            path = DOCS / name
            with path.open("rb") as fh:
                files = {"file": (name, fh, "application/pdf")}
                data = {"document_type": "10-K"}
                resp = client.post(
                    f"{BASE}/documents/upload",
                    headers=headers,
                    files=files,
                    data=data,
                )
            print("upload", name, resp.status_code, resp.text[:180])
            resp.raise_for_status()
            doc_ids.append(resp.json()["id"])

        # Process one-at-a-time: local Qdrant locks under concurrent workers.
        statuses: dict[int, dict] = {}
        for doc_id in doc_ids:
            r = client.post(
                f"{BASE}/documents/{doc_id}/process?force=true",
                headers=headers,
            )
            print("process", doc_id, r.status_code, r.text)
            r.raise_for_status()
            deadline = time.time() + 180
            while time.time() < deadline:
                s = client.get(f"{BASE}/documents/{doc_id}/status", headers=headers)
                s.raise_for_status()
                body = s.json()
                if body["status"] in {
                    "processed",
                    "completed",
                    "partially_processed",
                    "failed",
                }:
                    statuses[doc_id] = body
                    print(
                        "status",
                        doc_id,
                        body["status"],
                        "error=",
                        body.get("error"),
                    )
                    break
                time.sleep(1.5)
            else:
                raise SystemExit(f"timed out waiting for doc {doc_id}")

        failed = [i for i, b in statuses.items() if b["status"] == "failed"]
        if failed:
            raise SystemExit(f"processing failed for docs: {failed}")

        print("\n=== RAG queries ===")
        for question, needle in QUERIES:
            r = client.post(
                f"{BASE}/rag/query",
                headers=headers,
                json={"question": question},
            )
            print("\nQ:", question)
            print("HTTP:", r.status_code)
            if r.status_code != 200:
                print(r.text[:300])
                continue
            payload = r.json()
            answer = payload.get("answer", "")
            sources = payload.get("sources") or []
            hit = needle.lower() in answer.lower().replace(",", "").replace(" ", "")
            # Avoid Windows console encoding crashes on special spaces.
            safe = answer.encode("ascii", "replace").decode("ascii")
            print("A:", safe[:400])
            print("sources:", len(sources), "expect_contains:", needle, "PASS" if hit else "MISS")
            if sources:
                print(" top source:", json.dumps(sources[0], default=str)[:220])


if __name__ == "__main__":
    main()
