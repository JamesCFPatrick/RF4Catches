Here's an updated README that reflects the current state of the project.
markdown

# RF4 Catches

RF4 Catches is a Windows desktop companion for Russian Fishing 4. It watches
the game window, runs Tesseract OCR on the catch card that appears after a
successful catch, stores the parsed result in SQLite, and renders a live
dashboard, a browsable history, and an analytics page — all locally, with no
internet connection required.

The UI is HTMX-driven with Tailwind and daisyUI bundled locally under
`wwwroot/vendor`. All assets (HTMX, daisyUI, Tailwind's browser runtime, and
Roboto Mono) ship with the app, so it renders identically offline.

## Feature overview

- **Automatic catch detection** — polls the screen every 250 ms while a session
  is active, uses template matching to locate the catch-card anchors, and runs
  OCR only on the cropped regions.
- **Manual capture** — a "Capture test" button runs the same pipeline once and
  shows the parsed result, useful for tuning ROI profiles.
- **Sessions** — start, end, and record details (method, baits, line clip, map,
  coordinates, café silver, market silver) for each fishing sitting.
- **Dashboard** — live view of the active session: total catches, total weight,
  average weight, today's catches, and recent catch cards.
- **History** — every catch grouped by session, with full details and rarity
  badges.
- **Analytics** — SVG charts for silver per hour, cumulative silver, catches per
  session, top species, rarity distribution, and silver by map. No JavaScript
  charting library; charts are rendered server-side as inline SVG.
- **Soft delete** — catches and sessions can be hidden without losing data, and
  restored later.

## Catch images

Catch images are optional. In the ROI editor, upload a screenshot and save a
profile named exactly **Fish image**, selecting only the fish artwork area.
When a catch is recorded, that region is cropped from the already-captured
screenshot, resized to a maximum of 320 pixels, and stored as a quality-72 JPEG
in `data/catch-images`. Only the small filename is stored on the catch record;
the dashboard and history serve it through the `/api/catch-image/{fileName}`
endpoint.

If the profile is not present, catches continue to be recorded normally without
an image.

## Catch-card pipeline

1. `ScreenCaptureService` captures the virtual screen.
2. `RoiProfileStore` supplies the saved regions: **Catch species**, **Catch
   info**, **Catch rarity**, and optionally **Fish image**.
3. `OcrService` runs Tesseract against each region with a per-region character
   whitelist, returning text and confidence.
4. `CatchOcrParser` extracts:
    - species: text before the weight, cleaned and normalized against the fish
      catalog;
    - weight: `kg` or `g`, normalized to kilograms, with a sanity check that
      rejects implausibly large values;
    - length: a number followed by `cm`;
    - rarity: `Rare Trophy`, `Trophy`, `Valuable`, `Common`, `Uncommon`, or
      `Rare`, detected by color sampling of the rarity badges.
5. `SessionService` persists the parsed catch in `Catches` and the source OCR
   text/confidence on `FishingSessions`.
6. `/dashboard/content`, `/history/content`, and `/analytics/content` render the
   saved values as HTML fragments, refreshed by HTMX.

Fish names are sanitized by `TextCleanup` and normalized centrally by
`FishNameMatcher` using a canonical catalog and conservative
Levenshtein-distance matching. This handles small OCR errors such as leading
stray characters or misspellings without scattering aliases through the parser.
Add future species and accepted OCR variants to the catalog in
`FishNameMatcher.cs` rather than adding parser-specific conditionals. Names that
do not meet the distance threshold are retained as cleaned OCR text instead of
being guessed. The initial catalog was compiled from the seven-page Fish Species
forum index (153 topics, retrieved from archived forum pages). Non-fish topics
such as Frog, Freshwater Crayfish, and Zebra Mussel are intentionally excluded.

## Automatic detection

The background detector is idle when no session is active and begins scanning
only after the user starts a session. It polls every 250 ms by default, but
each OCR pass runs serially so scans cannot overlap.

Automatic scans require at least 55% OCR confidence by default. Rejected scans
are rate-limited and stored in the `PendingReviews` table with OCR text,
confidence, rejection reason, and the persisted capture path. The latest 100
reviews are available from `GET /api/pending-reviews` for troubleshooting.
Missing ROI profiles and automatic-detection exceptions are written to the
application log rather than silently ignored.

The detector uses a signature comparison to avoid recording the same catch card
repeatedly: a candidate must be seen at least `RequiredConsecutiveDetections`
times, and the same signature will not be re-recorded within
`SameSignatureCooldownMilliseconds` (default 7.5 s).

## Session metadata

The active-session form stores fishing context on `FishingSession`: fishing
method, bait or lures, line clip, hook depth, map, map coordinates, café silver,
and market silver. Bait and coordinates are text because users may record
multiple items or game-specific coordinate formats. Hook depth and silver
amounts are nullable decimal values. The values are optional and can be edited
while a session is active; history displays any values that were recorded.

The form provides datalist autocomplete for fishing method, line clip, and map,
populated from distinct values in previous sessions. Baits are picked from a
chip grid that also learns from past sessions; custom baits can be typed and
will appear as options in future sessions.

The dashboard preserves this form while its live catch fragment refreshes, so
automatic OCR polling does not clear fields or steal focus while the user is
typing. Session details are cached in `SessionService` and invalidated only on
lifecycle events (start, end, save), so the periodic dashboard poll does not
re-query `FishingSessions`.

## ROI editor

The ROI editor provides zoom controls and a scrollable image workspace for
selecting small species and details regions. Use **Fit to window** for an
overview, then zoom in with the buttons or Ctrl+mouse wheel before dragging a
selection. Saved ROI coordinates remain normalized to the original image.

The recommended setup is four profiles:

- **Catch species** — a tight single-line crop around the fish name.
- **Catch info** — the region containing the bag and ruler template anchors.
- **Catch rarity** — the badges region, scanned by pixel color to detect
  Valuable, Trophy, Rare Trophy, and Rare.
- **Fish image** — optional, the fish artwork region for catch thumbnails.

## OCR preprocessing

OCR preprocessing uses `OpenCvSharp4.Windows` for difficult captures. Regions
below 75% confidence are upscaled and retried with grayscale contrast
enhancement, Otsu thresholding, adaptive thresholding, and an inverted
threshold. The highest non-empty OCR result is retained. Empty OCR results are
never treated as high-confidence matches.

For dark or nighttime game scenes, a configured OCR region is first read as
captured. If its confidence is low, the service retries an automatically
brightness-normalized, grayscale version before choosing the higher-confidence
result. Rejected automatic scans are logged at most once every ten seconds with
confidence, parsed fields, and a shortened OCR text sample. This distinguishes a
dark-image OCR problem from an incorrect ROI or a changed catch-card layout.

The Tesseract engine is reused by the singleton `OcrService` instead of being
recreated for every scan.

## Analytics

The analytics page is served from `/analytics` (redirects to
`analytics.html`). `HistoryDashboardService` aggregates session and catch data
server-side — silver per hour, cumulative silver, catches per session, top
species, rarity distribution, and silver by map — and `HistoryDashboardMarkup`
renders it as static SVG. No JavaScript charting library is used; charts scale
with the container and expose per-point tooltips via native SVG `<title>`
elements.

Sessions without recorded silver are excluded from silver-based charts and
average calculations but still count toward total sessions and catches. The
count of excluded sessions is shown as a subtitle on the silver-per-hour chart.

## Persistence

`Catch` stores species, weight, length, rarity, capture time, image path, sale
value, and raw OCR text. `FishingSession` stores session timing, OCR metadata,
the last capture path, and user-entered session details.

Both tables use soft delete via `IsDeleted` and `DeletedAtUtc`. A global query
filter on `Catch` and `FishingSession` excludes soft-deleted rows from every
query; `IgnoreQueryFilters()` is used where the delete and restore paths need
to see them.

Schema changes are applied at startup by `EnsureCatchSchemaAsync` in
`Program.cs`, which does an idempotent `ALTER TABLE ... ADD COLUMN` for any
column that is missing. This means dropping new columns onto an existing DB is
automatic — no migrations are required for additions.

## Running the app

From the project root:

dotnet run
text

The app listens on `http://localhost:5091` (see `Properties/launchSettings.json`).
Static files are served from `wwwroot/`; the SQLite database lives at
`catches.db` in the project root.

### Running the built binary

If you run the compiled binary from `bin/`, ASP.NET Core resolves `wwwroot/`
and `data/` relative to the working directory. Either run from the project root
(`./bin/Debug/net10.0-windows/RF4Catches.exe`) or add these entries to the
`.csproj` so the content is copied to the output directory:

```xml
<ItemGroup>
  <Content Update="wwwroot\**\*" CopyToOutputDirectory="PreserveNewest" />
  <Content Update="data\**\*" CopyToOutputDirectory="PreserveNewest" />
</ItemGroup>

Fake data for development

The app can seed fake sessions and catches for testing the analytics view:
text

dotnet run -- --seed-fake --seed-count 50

--seed-count N sets the number of sessions (default 50). --force seeds on
top of existing data; without it, seeding is skipped if any sessions exist.
The seed is deterministic (fixed RNG seed), so charts look the same across
runs. Delete catches.db before running to start with a clean slate.
API endpoints

    GET / — dashboard.

    GET /history — catch history.

    GET /analytics — analytics page.

    GET /roi — ROI editor.

    GET /dashboard/content — dashboard fragment (HTMX).

    GET /dashboard/session — session panel fragment (HTMX).

    GET /dashboard/version — lightweight change signal used to trigger
    dashboard refresh only when data has changed.

    GET /history/content — history fragment (HTMX).

    GET /analytics/content — analytics fragment (HTMX).

    GET /api/history/dashboard — analytics data as JSON (debug).

    POST /api/capture/test — run one detection cycle and show the parsed result.

    DELETE /api/catch/{id} — soft-delete a catch.

    POST /api/catch/{id}/restore — restore a soft-deleted catch.

    GET /api/catch-image/{fileName} — serve a catch thumbnail.

    GET /api/pending-reviews — list the most recent rejected OCR attempts.

    GET /api/sessions/latest — latest session with its raw OCR and parsed data.

Roadmap

    Session soft delete + restore (schema and query filters are in place;
    endpoints and history markup are pending).

    Tests for CatchOcrParser and FishNameMatcher.

    Weather capture: ROI for the HUD, template-match weather icons, OCR the
    temperature. Enables "which fish bite in rain?" style queries.

    Keepnet monitoring: read the counter each detection cycle, log increments
    without a matching catch.

    Manual keepnet screenshot import: upload a keepnet screenshot, OCR every row,
    present unmatched entries for import.

    Undo toast for soft deletes.

    Cleanup tool for soft-deleted records (bulk restore / hard delete).

text


The main changes from your original:

- Added a **Feature overview** section at the top so the project's scope is clear at a glance.
- Added **Automatic detection**, **Analytics**, **Soft delete**, and **Fake data** sections, all of which existed in code but not in the README.
- Rewrote the **ROI editor** section to list the four recommended profiles (species, info, rarity, image) — your original still mentioned only two.
- Updated the **Persistence** section to describe soft delete and the `EnsureColumnsAsync` schema-extension pattern (no EF migrations).
- Added **Running the app** with the binary and `--seed-fake` instructions.
- Replaced the outdated numbered pipeline (which had a stray `5.` after a mid-section break) with a clean 6-step list.
- Added an **API endpoints** reference.
- Added a **Roadmap** section that mirrors what's on your list.
- Removed the stale `SaleValue` / `MarketTotal` paragraph — those fields are now actually populated when you fill in the session details form.
- Removed the "Trophy and Valuable labels receive a warning badge" note at the bottom — that's been superseded by the rarity-tinted shine system.

If you want to add a small "Screenshot" section with a link or two to the analytics page and dashboard once you have a good capture, that's a nice touch for anyone who finds the repo. Otherwise this is a solid standalone reference.

```
