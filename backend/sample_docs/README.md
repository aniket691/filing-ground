# Sample financial documents (RAG test pack)

Synthetic filings for upload → Process → Ask. Numbers are fictional and intentionally distinctive so you can verify grounded answers.

## PDF pack (recommended)

Generate/refresh all PDFs:

```bash
python backend/sample_docs/build_sample_pdfs.py
```

| PDF | Company | Key fact to ask |
|---|---|---|
| `novatech_fy2024_10k.pdf` | NovaTech (NVTC) | FY2024 revenue **$2.847B** |
| `meridian_bank_q2_2025_earnings.pdf` | Meridian (MCBP) | Q2 NIM **3.41%** |
| `greenforge_energy_risk_factors.pdf` | GreenForge (GFEN) | FY2024 revenue **$1.156B** |
| `cascade_retail_10q.pdf` | Cascade (CSDR) | **438** stores |
| `orion_aerospace_fy2024_10k.pdf` | Orion (OASN) | FY2024 revenue **$1.092B** |

End-to-end smoke test (API must be running on :8000):

```bash
python backend/sample_docs/test_pdf_rag.py
```

## Other formats

| File | Format |
|---|---|
| `novatech_fy2024_10k_excerpt.txt` | TXT |
| `meridian_bank_q2_2025_earnings.txt` | TXT |
| `greenforge_energy_risk_factors.txt` | TXT |
| `cascade_retail_10q_inventory.html` | HTML |
| `apple_10k_summary.txt` | TXT (existing) |

## How to use in the UI

1. Upload any PDF from this folder
2. Click **Process** (wait until Ready)
3. Ask a question from the table above

Process one document at a time when using local Qdrant file mode (concurrent jobs can hit a storage lock).
