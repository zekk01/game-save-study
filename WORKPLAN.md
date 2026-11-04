# Game-save study: completion plan

## Objective

Finish a reproducible, carefully scoped JEPCS Short Communication package for the game-save process-interruption study, then support human review and submission. Acceptance and editorial fit are uncertain.

## Work completed

- Implemented and audited the .NET harness (3,500 fault observations and 900 timing observations) and generated raw data, analysis, figures, and public archive at https://github.com/zekk01/game-save-study.
- Added command-level reproduction instructions. Improved the Unity runner so repeated runs can use fresh labels and the Unity summary tool accepts any chosen run directory; Python syntax/help checks pass.
- Completed a separate Unity 6000.3.11f1 replication (2,940 valid fault trials, four preserved controller-error rows, and 900 timing observations). Different save volumes mean the runtime timings are not a controlled comparison.
- Prepared the first manuscript in the JEPCS LaTeX template with protocol, results, limitations, and AI-use declaration.
- Expanded the targeted literature review and citation chain. The manuscript now cites peer-reviewed MMO checkpoint/recovery studies by Vaz Salles et al. (2009) and Cao et al. (2011), as well as the related process-crash preprint and filesystem crash-consistency work.
- Revised the novelty framing: game-state durability and checkpoint recovery have substantial prior art. The paper is a bounded local-file implementation case study, not a novel algorithm or priority claim.

## Remaining work, in order

### 1. Decide venue fit and complete literature review
- Continue searches in scholarly indexes (Scopus, Web of Science, IEEE Xplore, ACM DL, Google Scholar) with exact logged queries and documented screening; finish forward/backward citation screening. Two OpenAlex ranked samples (45 and 36 records across six queries) and record-level screening CSVs are archived, but they are not exhaustive; screen further cited/citing works from the already reviewed MMOG persistence thesis and transaction-model paper.
- DOI metadata spot checks now confirm titles, years, venues, and pages for six DOI-bearing sources; official arXiv metadata confirms two preprints. Verify every substantive claim directly against primary sources. Current web search remains targeted, not systematic.
- Human author should decide whether the narrow implementation case study has enough contribution for JEPCS after considering the closer MMO checkpoint literature. Do not imply firstness.

### 2. Compile and inspect
- Initial draft compiled with Tectonic 0.17.0 using the official project mirror for the v33 bundle; the missing JEPCS logo was restored from the supplied official template archive.
- Rendered and visually inspected all seven pages. Figures, table, references, and page furniture are legible, without detected clipping or overlap. One underfull hbox and host-font path warnings remain.
- Recompiled on 2026-11-04 after prose revision and repeated seven-page visual QA; no layout issues found.

### 3. Public materials published
- The public GitHub repository at https://github.com/zekk01/game-save-study contains the source, protocol, raw data, scripts, manuscript, and environment notes. The manuscript Data Availability statement links it. No DOI has been assigned; depositing a release in Zenodo is optional unless a persistent DOI is desired or required by another policy.

### 4. Human author review and journal checks
- Author identity, independent affiliation, email, CRediT roles, funding, acknowledgments, and competing-interest declarations have been added. ORCID was not provided; confirm if available.
- Have human author(s) verify all methods/data/claims and approve final manuscript.
- Obtain an authorized similarity report; recheck current JEPCS guidelines and portal immediately before submission.
- Corresponding author submits only after review and approval.

## Completion criteria

- Clean, compiled, visually inspected manuscript PDF and source.
- Public reproduction repository linked in the paper; a DOI may be added by archiving a release if desired or required.
- Documented literature screening and consistent contribution/limitation claims.
- Truthful author declarations, similarity screening, and human approval.
- Submission-ready package with no placeholders. Submission itself is performed by the corresponding author.
