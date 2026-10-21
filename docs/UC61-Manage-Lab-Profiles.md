# UC61 — Manage Lab Profiles

Backlog: I3-040 / BR-185. Web counterpart: I3-041 / BR-186.

## Scope and existing data

Manage professional staff details in the existing `lab_user_profiles` collection: `full_name`, `qualification`, and `assigned_room`. The profile `_id` is the linked `accounts._id`; both legacy BSON ObjectIds and string IDs are supported. This UC does not manage test packages, login accounts, clinical results, or shift assignments.

Reference options come from existing `accounts` and `lab_rooms`. Account lookup projects only ID, FullName and Username; passwords, contact details and tokens are not read or returned. Eligible accounts have the established RoleId `3` (LabTechnician) or `4` (LabHead), Status `Active`, and no explicit deleted/inactive flag. Creating or editing professional details never changes the linked account. Account identity cannot be reassigned through this API.

## API and authorization

All endpoints require an authenticated Admin (including the existing Administrator alias), Manager or LabHead. Other roles receive 403. Actual JWT actor identity comes from the existing `sub` claim.

| Method | Endpoint | Behavior |
| --- | --- | --- |
| GET | `/api/lab-profiles` | Directory with search, room and creation-date filters; page 1–10000, pageSize 1–100 |
| GET | `/api/lab-profiles/{id}` | Active profile or 404 |
| GET | `/api/lab-profiles/rooms` | Up to 500 existing active Lab room references |
| GET | `/api/lab-profiles/accounts` | Eligible account search; paginated; `hasProfile` includes archived profiles |
| POST | `/api/lab-profiles` | Create; returns 201 and Location |
| PUT | `/api/lab-profiles/{id}` | Update professional details using `expectedRevision` |
| DELETE | `/api/lab-profiles/{id}` | Archive using JSON body `{ expectedRevision, reason }`; returns 204 |

Create body: `accountId`, `fullName` (required, max 200), `qualification` (required, max 500), `assignedRoom` (existing active room), `reason` (5–500 trimmed characters). Update uses the same professional fields plus required nonnegative `expectedRevision`, without accountId. Validation is enforced in DTOs and service. Invalid input returns 400; missing profiles return 404; reserved identities, stale versions and blocking assignments return 409 with ProblemDetails.

Literal search matches name/qualification or exact account ID; regex metacharacters are escaped. Sort order is name then ID. Creation-date bounds use inclusive clinic calendar days (UTC+7). Undated legacy records remain undated and are excluded when a date bound is supplied.

## Persistence, history and deletion

- Reuse existing collections; no bulk migration or fabricated staff data. DI registers `ILabProfileRepository` and `ILabProfileService` in their corresponding layers. TimeProvider uses the existing application registration.
- Additive profile metadata: `revision`, `created_at`, `updated_at`, `is_deleted`, `deleted_at`, and embedded `audit` entries containing action, authenticated actor, server UTC timestamp, reason, and before/after professional snapshots. API responses omit the internal audit array.
- Missing legacy revision means zero. Update/delete compare active state and expected revision in the same Mongo write; `$set`, `$inc`, and `$push` preserve unrelated legacy fields and append audit atomically. The `_id` unique index prevents concurrent duplicate creates by this API. Existing ObjectId/string representations are checked before creating; archived identities stay reserved.
- Delete is logical, preserving account, profile identity, audit and assignment history. Current/future nonterminal `lab_assignments` block deletion. Clinic-day start is UTC+7 midnight. Terminal statuses are Cancelled/Canceled, Completed, Closed and Inactive (case insensitive); malformed or absent dates on nonterminal assignments block conservatively. Past assignments do not block.
- Add idempotent compound indexes for profile room/name/ID, profile creation date/ID, account role/status/name/ID and assignment staff/date/status. Substring search may still scan within the selected dataset; count and page reads are not a snapshot transaction.

The account/room validation and assignment dependency checks are separate collection reads. They are **not** a cross-collection transaction: a concurrent account/room change or new assignment from another module can occur after validation. Future assignment writers must coordinate with profile archival if strict cross-module exclusion is needed. Existing external writers that ignore profile revisions are outside the optimistic-concurrency guarantee.

Archived profiles have no restore UI in this scope. The account remains usable; UC61 does not deactivate employee access. The default room does not replace a shift assignment. Staff professional names may differ from login-account display names.

## Verification

Run from repository root:

```powershell
dotnet build src/BMS.slnx --no-restore -m:1 -p:UseSharedCompilation=false -v:quiet
dotnet run --project tests/BMS.UC61.Checks -p:BuildInParallel=false -p:UseSharedCompilation=false -v:quiet
```

The executable check suite uses an isolated, randomly named `BMS_UC61_Checks_*` database on local MongoDB and drops only that fixture database. It covers legacy BSON identities and dates, escaped search, pagination, clinic-date bounds, HTTP CRUD/status codes and authorization, safe account lookup, duplicate/concurrent creates, stale/concurrent writes, account preservation, immutable identity, unknown-field preservation, assignment guards, archival history and no resurrection. It also reads existing BMS profile data for compatibility without mutating staff documents.

Observed: backend build succeeded with 0 warnings/errors; all 15 check groups passed. The web repository documents its build and manual acceptance steps. No mobile changes.
