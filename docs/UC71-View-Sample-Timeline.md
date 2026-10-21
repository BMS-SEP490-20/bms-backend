# UC71 — View Sample Timeline

Backlog I3-070 / BR-215; web I3-071 / BR-216. This use case is read-only. It does not add sample/status-update/RFID-ingestion actions or change mobile code.

## Data and boundaries

Reuse `blood_samples`, `rfid_tracking_logs`, `appointments`, `test_results`, `patients`, and optional `specimen_histories`. The existing history service can seed demo records and default timestamps; UC71 reads minimal raw BSON projections through a dedicated repository instead. It never seeds documents or falls back to demo data when MongoDB is unavailable. No new event collection or migration is required. Application and Infrastructure DI register the new service/repository. Existing sample writers remain unchanged.

## API

| Method | Endpoint | Response |
| --- | --- | --- |
| GET | `/api/sample-timelines` | Paged accessible samples; search, status and collection-date filters |
| GET | `/api/sample-timelines/{code}` | Summary, six progress stages, paged recorded events |
| GET | `/api/sample-timelines/{code}/rfid` | Staff-only paged scans and attribution |

Pagination: page 1–10000, pageSize 1–100 (list default 20, event/RFID default 50). Search up to 100 characters: literal barcode substring, exact sample ID or patient ID; regex metacharacters are escaped. Detail uses exact sample ID first, then exact barcode. Duplicate barcode matches return 400. Shared RFID tags never identify an individual sample.

Collection-date bounds use inclusive UTC+7 calendar days. Undated samples remain undated and are excluded by date bounds. Lists sort by collection time descending, then ID. Events sort by time ascending then source/entry ID, with undated events last. Long timelines and RFID lists are paginated rather than silently truncated. Invalid input returns 400; unauthorized roles 403; missing and inaccessible samples share a 404 response. Responses set no-store. Database failures reach the existing generic exception handler.

## Authorization and privacy

- BR-IAM-02: staff roles Admin/Administrator, Manager, Doctor, LabHead, LabTech/LabTechnician, Nurse/Phlebotomist and Receptionist can read operational tracking. Cashier and WarehouseStaff are denied.
- BR-PAT-01: Patient access is scoped server-side by JWT `sub` → `patients.AccountId` → sample `patient_id`, supporting string/ObjectId identities. Query parameters cannot expand scope. Unlinked accounts receive an empty list; guest profiles without a matching account grant no access.
- Family links do not grant access: the existing dependent endpoint is a placeholder and does not establish verified legal delegation. Verified dependent authorization is outside this feature; access fails closed.
- Patient responses use standardized progress, removing tags, notes, locations, operator names and raw source metadata. Unknown internal statuses are redacted. Patient is denied RFID access. Receptionist responses omit clinical free text and unrecognized custom event titles.
- No test values, conclusions, contact details, report URLs or signature values are returned. Viewer identity/role comes from claims, never a request body.

## Evidence rules

BR-PAT-02: stages are check-in, collection, Lab reception, machine analysis, medical review and official results. Only corresponding recorded evidence marks a stage; earlier stages are not inferred from later state. Processing denotes preparation/processing; Testing/MachineAnalysis denotes machine analysis. Unknown times stay null and do not imply a stage never occurred.

Sample history preserves reruns/rejections. Explicit timestamp fields supplement history without repeating an identical status/time pair. Appointment and optional specimen-history joins require the same patient. Staff see source fields because older writers may have populated legacy timestamps automatically: UC71 presents stored evidence without independently certifying physical occurrence.

Official-result availability requires sample state Completed/Stored and at least one matching report, with **all existing matching reports** approved, bearing a nonblank string signature, and not requesting a rerun. Both existing result naming conventions are supported. Completion alone, an unsigned report or a manual event named ResultAvailable cannot establish availability. Missing approval times remain null. Signature presence trusts the existing approval writer; UC71 does not cryptographically verify certificates or prove that every expected order package has produced a report.

BR-PHL-04: scans are read-only evidence. Direct sample_id scans are distinguished from tag-only readings for the current tray tag since collection. Another sample's explicit scans and precollection tag readings are excluded; without collection time, no tag-only scans are attributed. Tags can be shared/reused, so tag-only readings do not prove this sample crossed a checkpoint and never drive clinical stages. Existing checkpoint/reader aliases are supported.

## Indexes and limitations

Reuse the existing unique barcode index without changing options; create a lookup index only if absent. Add compound indexes for collection time/ID, patient/time/ID, status/time/ID and RFID sample/time/ID and tag/time/ID. Reads never alter source documents. Existing result and optional specimen-history indexes remain owned by those modules.

Separate collections and page requests are not a snapshot transaction. New events can move pages. Visible details refresh every 30 seconds and support manual refresh; this is polling, not push/SignalR. Substring searches may scan records. Unknown legacy data is not repaired by this feature.

## Verification

```powershell
dotnet build src/BMS.slnx --no-restore -m:1 -p:UseSharedCompilation=false -v:quiet
dotnet run --project tests/BMS.UC71.Checks -p:BuildInParallel=false -p:UseSharedCompilation=false -v:quiet
```

Executable checks use a random local BMS_UC71_Checks database with guarded cleanup. They cover scoped/escaped search and pagination, UTC+7 dates, BSON identities, chronology and unknown times, six stages, patient/reception redaction, cross-account denial, RFID attribution, unchanged source documents, approved/unsigned/partial reports and reruns, invalid requests, HTTP 401/403/404/400/405 and no-store headers. A compatibility check reads existing BMS sample data without printing identifiers or modifying sample documents and preserves the unique barcode index.

Observed: 11 check groups passed. Web build/manual acceptance steps are documented separately. No browser screenshot/end-to-end result is claimed.
