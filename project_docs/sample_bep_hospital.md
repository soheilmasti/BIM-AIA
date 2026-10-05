# BIM Execution Plan (BEP) - Standard Project Guidelines
**Standard Code:** BIM-STD-2026  
**Revision:** 2.1  
**Lead Discipline:** MEP & Coordination  

---

## 1. General Project Scope & Objectives
This baseline document defines standard MEP modeling and coordination rules for building facilities. The project is governed by Building Regulations (Código Técnico de la Edificación - CTE) and ISO 19650 standards.

---

## 2. Level of Development (LOD) Requirements
* **Detailed Design Stage (DD):** LOD 300 / LOI 300.
* **Coordination & Pre-construction:** LOD 350. All physical connections, flanges, and clearance envelopes must be modeled.
* **As-Built Stage:** LOD 500 with integrated COBie data fields.

---

## 3. MEP System Routing & Clearance Specifications
* **Section 3.1 - Gravity Drainage & Sanitary Pipes:**
  * All horizontal sanitary pipes under DN110 must maintain a minimum slope of 1.5% (0.015).
  * Pipes with DN110 and larger must maintain a minimum slope of 1.0% (0.010).
  * Routing of sanitary lines directly above sterile spaces or sensitive electrical switchgear rooms is strictly prohibited unless housed within a secondary watertight containment sleeve (CTE DB-HS Article 3.2.1).
* **Section 3.2 - HVAC Ductwork:**
  * Minimum clearance between bottom of structural concrete beams and top of main supply air ducts is 50mm.
  * Thermal and acoustic insulation of 25mm thickness is mandatory for all supply air ducts routed through unconditioned vertical service shafts and ceiling plenums (CTE DB-HE 2).
* **Section 3.3 - Builder's Works & Structural Penetrations (Sleeves):**
  * Pipe and duct penetrations through load-bearing concrete walls and beams must be coordinated via approved builder's work voids.
  * Standard sleeve clearance is 50mm perimeter envelope around the outside diameter of pipe including insulation.
  * Penetration through the shear zone (first 1/3 of span near columns) of transfer beams is strictly forbidden without written sign-off from the Structural Lead.

---

## 4. Parameter & Classification Standards
* Every modeled component must carry valid classification codes according to Uniclass 2015 (Parameters: `UniclassPr_Code` and `UniclassPr_Description`).
* `System Abbreviation` parameter is required on all MEP segments (e.g., `SAN` for Sanitary, `CWS` for Cold Water Supply, `SA` for Supply Air, `RA` for Return Air).
