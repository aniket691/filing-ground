"""Build synthetic financial sample PDFs for FinQuery RAG testing."""

from __future__ import annotations

from pathlib import Path

OUT_DIR = Path(__file__).resolve().parent


def _escape(text: str) -> str:
    return (
        text.replace("\\", "\\\\")
        .replace("(", "\\(")
        .replace(")", "\\)")
    )


def _wrap(text: str, width: int = 88) -> list[str]:
    words = text.split()
    if not words:
        return [""]
    lines: list[str] = []
    current: list[str] = []
    for word in words:
        trial = (" ".join(current + [word])) if current else word
        if len(trial) <= width:
            current.append(word)
        else:
            if current:
                lines.append(" ".join(current))
            current = [word]
    if current:
        lines.append(" ".join(current))
    return lines


def _page_content(lines: list[str], font_size: int = 10) -> bytes:
    y = 750
    parts = ["BT", f"/F1 {font_size} Tf", "13 TL", f"40 {y} Td"]
    first = True
    for line in lines:
        if not first:
            parts.append("T*")
        first = False
        parts.append(f"({_escape(line)}) Tj")
    parts.append("ET")
    return "\n".join(parts).encode("latin-1", errors="replace")


def write_pdf(path: Path, pages: list[list[str]]) -> None:
    objects: list[bytes] = []
    objects.append(b"<< /Type /Catalog /Pages 2 0 R >>")
    kids = " ".join(f"{3 + i * 2} 0 R" for i in range(len(pages)))
    objects.append(
        f"<< /Type /Pages /Kids [{kids}] /Count {len(pages)} >>".encode()
    )

    for i, lines in enumerate(pages):
        content = _page_content(lines)
        page_obj_num = 3 + i * 2
        content_obj_num = page_obj_num + 1
        objects.append(
            (
                f"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] "
                f"/Contents {content_obj_num} 0 R "
                f"/Resources << /Font << /F1 << /Type /Font "
                f"/Subtype /Type1 /BaseFont /Helvetica >> >> >> >>"
            ).encode()
        )
        objects.append(
            f"<< /Length {len(content)} >>\nstream\n".encode()
            + content
            + b"\nendstream"
        )

    out = bytearray(b"%PDF-1.4\n")
    offsets = [0]
    for idx, obj in enumerate(objects, start=1):
        offsets.append(len(out))
        out.extend(f"{idx} 0 obj\n".encode())
        out.extend(obj)
        out.extend(b"\nendobj\n")

    xref_pos = len(out)
    out.extend(f"xref\n0 {len(objects) + 1}\n".encode())
    out.extend(b"0000000000 65535 f \n")
    for off in offsets[1:]:
        out.extend(f"{off:010d} 00000 n \n".encode())
    out.extend(
        (
            f"trailer\n<< /Size {len(objects) + 1} /Root 1 0 R >>\n"
            f"startxref\n{xref_pos}\n%%EOF\n"
        ).encode()
    )
    path.write_bytes(out)


def _pages_from_paragraphs(paragraphs: list[str], max_lines: int = 52) -> list[list[str]]:
    lines: list[str] = []
    for para in paragraphs:
        if para == "":
            lines.append("")
        else:
            lines.extend(_wrap(para))
    pages: list[list[str]] = []
    for i in range(0, len(lines), max_lines):
        pages.append(lines[i : i + max_lines])
    return pages or [[""]]


DOCUMENTS: dict[str, list[str]] = {
    "novatech_fy2024_10k.pdf": [
        "NOVATECH INDUSTRIES INC. - FORM 10-K FY2024 (SYNTHETIC TEST PDF)",
        "Ticker: NVTC | Fiscal year ended December 31, 2024 | CIK: 0001987451",
        "",
        "ITEM 1 - BUSINESS",
        "NovaTech Industries Inc. designs industrial automation controllers, edge AI inference modules, and predictive-maintenance software. Headquarters: Austin, Texas. Employees at year-end: 4,820.",
        "Product lines: ControLogic Series, EdgeMind Modules, PulseCare Software (SaaS).",
        "",
        "ITEM 7 - FINANCIAL HIGHLIGHTS",
        "Total net revenue fiscal 2024: $2.847 billion, up 18.4% from $2.404 billion in fiscal 2023.",
        "Net income fiscal 2024: $312.6 million, or $2.41 per diluted share.",
        "Net income fiscal 2023: $241.0 million, or $1.88 per diluted share.",
        "Revenue mix FY2024: ControLogic $1.412B (49.6%); EdgeMind $698M (24.5%); PulseCare $737M (25.9%).",
        "Gross margin: 47.2% in FY2024 vs 44.8% in FY2023.",
        "Operating expenses: $891 million including R&D of $214 million.",
        "Capital expenditures: $96.4 million. Free cash flow: $401 million.",
        "Cash and cash equivalents: $628 million. Long-term debt: $450 million (4.125% senior notes due 2031).",
        "Geographic revenue: United States $1.61B; Europe $712M; Asia-Pacific $525M.",
        "",
        "ITEM 1A - SELECTED RISKS",
        "Supply concentration: 62% of ControLogic PCB assemblies from two contract manufacturers in Penang, Malaysia.",
        "Customer concentration: top ten customers = 34% of FY2024 revenue; Apex Motors Assembly LLC = 9.1%.",
        "Cybersecurity contractual exposure estimated up to $85 million.",
        "",
        "GUIDANCE",
        "Fiscal 2025 revenue expected $3.10 billion to $3.25 billion, assuming PulseCare net retention above 112%.",
    ],
    "meridian_bank_q2_2025_earnings.pdf": [
        "MERIDIAN COMMUNITY BANCORP - Q2 2025 EARNINGS RELEASE (SYNTHETIC TEST PDF)",
        "Ticker: MCBP | Quarter ended June 30, 2025 | Released July 18, 2025",
        "",
        "HEADLINE RESULTS",
        "Net income Q2 2025: $87.4 million, or $1.12 per diluted share.",
        "Net income Q2 2024: $71.9 million, or $0.93 per diluted share.",
        "Net interest income: $246.8 million, up 11.2% from $221.9 million.",
        "Net interest margin (NIM): 3.41% vs 3.18% a year earlier.",
        "Noninterest income: $52.1 million (wealth fees $18.6M; mortgage banking $9.4M).",
        "Provision for credit losses: $14.2 million vs $19.5 million in Q2 2024.",
        "",
        "BALANCE SHEET (June 30, 2025)",
        "Total assets: $28.6 billion. Total loans HFI: $19.4 billion. Total deposits: $23.1 billion.",
        "Average loan yield: 6.08%. Average cost of interest-bearing deposits: 2.74%.",
        "Tangible common equity ratio: 8.9%. CET1 capital ratio: 11.4%.",
        "",
        "CREDIT QUALITY",
        "Nonperforming assets: 0.62% of total assets. Net charge-offs: 0.28% of average loans (annualized).",
        "Allowance for credit losses: $287 million, or 1.48% of total loans.",
        "C&I loans: $8.2 billion (+7.3% YoY). Residential mortgage: $6.1 billion. Consumer: $5.1 billion.",
        "",
        "DIVIDEND AND GUIDANCE",
        "Quarterly cash dividend: $0.34 per share, payable August 15, 2025.",
        "Full-year 2025 net interest income guidance: $980 million to $1.02 billion.",
        "Expected effective tax rate: approximately 22.5%.",
    ],
    "greenforge_energy_risk_factors.pdf": [
        "GREENFORGE ENERGY CORP. - ITEM 1A RISK FACTORS (SYNTHETIC TEST PDF)",
        "Ticker: GFEN | Fiscal year ended September 30, 2024",
        "",
        "COMMODITY PRICE VOLATILITY",
        "Fiscal 2024 revenue was $1.156 billion; about 71% came from wholesale electricity sales.",
        "A sustained 15% decline in average realized power prices could reduce annual EBITDA by $95 million to $120 million.",
        "",
        "PROJECT CONCENTRATION",
        "Cedar Ridge Wind (420 MW), Solstice Solar Park (310 MW DC), and Harbor Battery Storage (150 MW / 600 MWh) generated 58% of FY2024 operating cash flow.",
        "An unplanned outage at Cedar Ridge lasting more than 90 days could reduce annual free cash flow by about $41 million.",
        "",
        "REGULATORY AND PERMITTING",
        "Falcon Prairie wind project target COD Q3 2026; expected capex $780 million.",
        "If COD slips beyond December 31, 2026, approximately $22 million of PTC bridge benefits may be forfeited.",
        "",
        "COUNTERPARTY AND LEVERAGE",
        "Investment-grade offtake covered 64% of expected 2025 generation.",
        "Prairie Cooperative Utility is 11% of contracted MWh; default would leave about 1.4 TWh on spot markets.",
        "Total debt: $2.04 billion. Revolving credit drawn: $650 million at 6.85%.",
        "Net debt / EBITDA covenant max 4.75x; actual ratio at year-end: 3.9x.",
        "Board dividend target: $0.48 per share annually.",
        "",
        "PHYSICAL AND CYBER RISK",
        "April 2024 ice storm damaged 47 turbines at Cedar Ridge; restoration $31M net of $18M insurance.",
        "February 2024 ransomware disrupted Solstice SCADA for 36 hours. FY2025 cyber budget: $14.5 million.",
    ],
    "cascade_retail_10q.pdf": [
        "CASCADE RETAIL GROUP, INC. - FORM 10-Q EXCERPT (SYNTHETIC TEST PDF)",
        "Ticker: CSDR | Period ended April 30, 2025 | Filed June 6, 2025",
        "",
        "NOTE 3 - INVENTORIES (in millions)",
        "Apparel: $612 (vs $548 prior quarter). Home: $287 (vs $301). Electronics: $194 (vs $176).",
        "Raw materials and packaging: $41 (vs $38). Total inventories: $1,134 (vs $1,063).",
        "Obsolescence reserves: $47 million at April 30, 2025 vs $39 million at January 31, 2025.",
        "",
        "NOTE 5 - NET SALES",
        "Net sales thirteen weeks ended April 30, 2025: $2.186 billion, up 6.8% from $2.047 billion.",
        "Comparable store sales +3.1%. E-commerce: 29.4% of net sales vs 26.9% a year ago.",
        "Gross margin: 34.8%, down 70 basis points year over year.",
        "",
        "NOTE 8 - STORE FOOTPRINT AND LIQUIDITY",
        "Stores operated: 438 (412 United States, 26 Canada) vs 429 at January 31, 2025.",
        "Closed 9 underperforming locations and opened 18 new format stores.",
        "Average selling square footage per store: 28,400. Capex in quarter: $54 million ($31M remodels).",
        "Cash: $318 million. Undrawn revolver: $400 million. Net debt: $892 million. Interest expense: $11.7 million.",
        "",
        "OUTLOOK",
        "FY2025 net sales expected $9.4 billion to $9.7 billion. Operating margin target 7.5% to 8.0%.",
    ],
    "orion_aerospace_fy2024_10k.pdf": [
        "ORION AEROSPACE SYSTEMS INC. - FORM 10-K SUMMARY EXCERPT FY2024 (SYNTHETIC TEST PDF)",
        "Ticker: OASN | Fiscal year ended December 31, 2024 | HQ: Denver, Colorado | Employees: 2,140",
        "",
        "ITEM 7 - FINANCIAL HIGHLIGHTS",
        "Total revenue FY2024: $1.092 billion (up 14.7% YoY). Revenue FY2023: $952 million.",
        "Net income FY2024: $128.5 million ($3.05 diluted EPS).",
        "Gross margin: 39.6%. Operating margin: 16.2%. R&D: $97 million. Capex: $63 million.",
        "Year-end cash: $241 million. Total debt: $310 million.",
        "Segment revenue: Commercial satcom $612M; Defense avionics $318M; Aftermarket services $162M.",
        "",
        "BACKLOG AND GUIDANCE",
        "Firm backlog Dec 31, 2024: $1.48 billion; $620 million expected to convert in FY2025.",
        "Project Horizon (USAF) remaining contract value: $410 million through 2028.",
        "FY2025 revenue outlook: $1.20 billion to $1.26 billion. Target operating margin: 16.5% to 17.5%.",
        "",
        "ITEM 1A - SELECTED RISKS",
        "About 22% of commercial satcom shipments require ITAR/EAR licenses; a 90-day freeze could defer up to $48M quarterly revenue.",
        "Single Taiwan foundry partner supplies Ka-band RF chipsets representing 35% of terminal BOM cost.",
        "Project Horizon was 19% of FY2024 defense revenue; cancellation could cut FY2025 defense revenue by $70M to $85M.",
    ],
}


def main() -> None:
    for filename, paragraphs in DOCUMENTS.items():
        path = OUT_DIR / filename
        write_pdf(path, _pages_from_paragraphs(paragraphs))
        print(f"wrote {path.name} ({path.stat().st_size} bytes)")


if __name__ == "__main__":
    main()
