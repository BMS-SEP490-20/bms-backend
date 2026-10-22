# UC76 — View Test History

Backlog: I3-085 / BR-230. Web: I3-086 / BR-231. Mobile: I3-087 / BR-232.

## Scope and existing data

Read historical documents from `test_results`, using existing snake_case and camelCase schemas. Measurements come from embedded `items` when present, otherwise `test_parameters.result_id`. Reuse `test_packages` for package names, `blood_samples` for current sample state, `patients.AccountId` for account ownership and `audit_logs` for access records. No new clinical collection, seeding, simulated success, result mutation, PDF export or new approval workflow.

The older report/export services have demo fallbacks and model defaults that cannot establish signed publication. UC76 uses a strict repository over the existing collections instead. Missing dates/ranges/flags remain missing. Numeric double values use invariant round-trip formatting; no clinical rounding, reference calculation or flag evaluation is introduced. Existing stored flags are returned unchanged.

## API and permissions

| Method | Endpoint | Response |
| --- | --- | --- |
| GET | `/api/test-history` | Counted/paged metadata, newest stored creation time first, then ID |
| GET | `/api/test-history/{id}` | Guarded result detail by exact persisted ID |

Filters: search (literal result-code substring or exact result/sample/package ID), patientId, fromDate/toDate (inclusive UTC+7 result-creation days), page 1–10000, pageSize 1–100 (default 20). Unknown dates are excluded when date bounds are supplied. Regex metacharacters are escaped. No values/conclusions are returned in list responses.

BR-IAM-02: clinical roles Admin/Administrator, Manager, Doctor, LabHead, LabTech/LabTechnician and Patient are permitted. Nurse/Phlebotomist, Receptionist, Cashier and WarehouseStaff are denied. Staff can filter clinical history across patients; Patient scope always intersects the explicit account link resolved from the authenticated JWT sub. Client-supplied accountId cannot expand access. Canonical snake_case patient fields take precedence over camelCase aliases rather than granting access through conflicting aliases. BSON string/ObjectId identities are supported.

BR-PAT-01: Patient can see only their explicitly linked patient records. Existing unverified dependent placeholders do not establish legal delegation, so dependent access fails closed. Missing and inaccessible results return the same 404. Unsupported roles receive 403; malformed filters receive 400. Responses use no-store.

## Publication and audit

Clinical values and conclusion are withheld for **all UC76 viewers** unless the result has consistent approved state (APPROVED / DoctorApproved or enum value 3), a nonblank string signature, no active rerun flag, and a matching patient's active sample in Completed/Stored state. Conflicting approval fields, unsigned reports, missing samples, analysis in progress and reruns yield metadata with available=false, no items and no conclusion. Existing writer screens handle unfinished results; UC76 is not an approval bypass.

Signature presence trusts the approval writer; UC76 does not cryptographically verify certificates. Stored results are individual historical documents, not reconstructed revision snapshots. Missing approvedAt remains unknown. Current package names are lookup metadata; stored clinical values/ranges are never replaced with current catalog defaults.

BR-IAM-03: successful directory/detail reads append to the existing audit collection with AccountId, Action, Module=UC76, IpAddress, target/Directory in Details and server UTC Timestamp. Audit is awaited before disclosure; failure does not return clinical data. No values, conclusions, tokens or query contents are logged. Clinical documents remain unchanged.

## Integration and limits

Application/Infrastructure DI register ITestHistoryService and ITestHistoryRepository. TimeProvider reuses the existing registration. Existing APIs are unchanged. Add compound indexes for both patient/creation-date schema variants and a result_id parameter lookup index. Cross-collection validation/count/page reads are not a snapshot transaction; another module may revoke data after a response. Clients re-fetch detail instead of persisting clinical values. Coalesced-field sorting and substring search can scan; per-page sample/package enrichment is bounded to 100 records. No bulk data migration.

## Verification

```powershell
dotnet build src/BMS.slnx --no-restore -m:1 -p:UseSharedCompilation=false -v:quiet
dotnet run --project tests/BMS.UC76.Checks -p:BuildInParallel=false -p:UseSharedCompilation=false -v:quiet
```

Observed: build succeeds with 0 warnings/errors; 7 check groups pass. Fixtures cover ownership and canonical identity, search/pagination, UTC+7 dates, legacy/camel measurements and unknown dates, unsigned/revoked/rerun disclosure, audit with unchanged clinical documents, validation and HTTP role/no-store/404/405 contracts. Tests use an isolated randomly named BMS_UC76_Checks database and guarded cleanup. No real clinical document is changed.
