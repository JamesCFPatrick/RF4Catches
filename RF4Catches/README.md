# RF4 Catches

RF4 Catches is a Windows desktop companion that captures the fishing-game catch
card, runs Tesseract OCR, stores the result in SQLite, and renders a live
dashboard and session history.

The dashboard assets are bundled under `wwwroot/vendor`: HTMX, daisyUI,
Tailwind's browser runtime, and the Roboto Mono font files. The app therefore
does not need an internet connection to render its UI.

## Catch images

Catch images are optional. In the ROI editor, upload a screenshot and save a
second profile named exactly **Fish image**, selecting only the fish artwork
area. When a catch is recorded, that region is cropped from the already
captured screenshot, resized to a maximum of 320 pixels, and stored as a
quality-72 JPEG in `data/catch-images`. Only the small filename is stored on
the catch record; the dashboard and history serve it through the protected
`/api/catch-image/{fileName}` endpoint.

If the profile is not present, catches continue to be recorded normally
without an image.

## Catch-card pipeline

1. `ScreenCaptureService` captures the virtual screen.
2. `RoiProfileStore` optionally selects the saved **Catch card** region.
3. `OcrService` runs Tesseract and returns text plus confidence.
4. `CatchOcrParser` extracts:
   - species: text before the weight, with known OCR cleanup and aliases;
   - weight: `kg` or `g`, normalized to kilograms;
   - length: a number followed by `cm`;
   - rarity: `Rare Trophy`, `Trophy`, `Valuable`, `Common`, `Uncommon`, or `Rare`.

Fish names are sanitized by `TextCleanup` and normalized centrally by
`FishNameMatcher` using a canonical catalog and conservative
Levenshtein-distance matching. This handles small OCR errors such as leading
stray characters or misspellings without scattering aliases through the parser.
Add future species and accepted OCR variants to the catalog in
`FishNameMatcher.cs` rather than adding parser-specific conditionals. Names that do not meet the
distance threshold are retained as cleaned OCR text instead of being guessed.
The initial catalog was compiled from the seven-page Fish Species forum index
(153 topics, retrieved from archived forum pages). Non-fish topics such as Frog,
Freshwater Crayfish, and Zebra Mussel are intentionally excluded.

## Session metadata

The active-session form stores fishing context on `FishingSession`: fishing
method, bait or lures, line clip, hook depth, map, map coordinates, café silver,
and market silver. Bait and coordinates are text because users may record
multiple items or game-specific coordinate formats. Hook depth and silver
amounts are nullable decimal values. The values are optional and can be edited
while a session is active; history displays any values that were recorded.
The dashboard preserves this form while its live catch fragment refreshes, so
automatic OCR polling does not clear fields or steal focus while the user is
typing.

The ROI editor provides zoom controls and a scrollable image workspace for
selecting small species and details regions. Use **Fit to window** for an
overview, then zoom in with the buttons or Ctrl+mouse wheel before dragging a
selection. Saved ROI coordinates remain normalized to the original image.
5. `SessionService` persists the parsed catch in `Catches` and the source OCR
   text/confidence on `FishingSessions`.
6. `/dashboard/content` and `/history/content` render the saved values as HTML
   fragments, refreshed by HTMX.

Automatic detection repeats this pipeline on a timer. It requires valid species
and weight, then uses the parsed fields as a signature to avoid recording the
same card repeatedly.
The background detector is idle when no session is active and begins scanning
only after the user starts a session. It polls every 250 ms by default, but
each OCR pass runs serially so scans cannot overlap. Manual capture and the
explicit end-session capture remain available as separate actions.

Automatic scans now require at least 55% OCR confidence by default. Rejected
scans are rate-limited and stored in the `PendingReviews` table with OCR text,
confidence, rejection reason, and the persisted capture path. The latest 100
reviews are available from `GET /api/pending-reviews` for troubleshooting.
Missing ROI profiles and automatic detection exceptions are written to the
application log rather than silently ignored.

The Tesseract engine is reused by the singleton OCR service instead of being
recreated for every scan. The recommended ROI setup is two profiles named
**Catch species** and **Catch details**. The first is a tight single-line crop
around the fish name; the second contains weight, length, and rarity. These
profiles use `PageSegMode.SingleLine`, separate character whitelists, and the
lower of their two confidence scores. This improves reliability without
requiring a profile for every species. If both profiles are not present, the
older **Catch card** profile remains supported as a multi-line
`PageSegMode.Auto` fallback.

OCR preprocessing uses `OpenCvSharp4.Windows` for difficult captures. Regions
below 75% confidence are upscaled and retried with grayscale contrast
enhancement, Otsu thresholding, adaptive thresholding, and an inverted
threshold. The highest non-empty OCR result is retained. Empty OCR results are
never treated as high-confidence matches.

For dark or nighttime game scenes, a configured OCR region is first read as
captured. If its confidence is low, the service retries an automatically
brightness-normalized, grayscale version before choosing the higher-confidence
result. Rejected automatic
scans are logged at most once every ten seconds with confidence, parsed fields,
and a shortened OCR text sample. This distinguishes a dark-image OCR problem
from an incorrect ROI or a changed catch-card layout.

## Persistence

`Catch` stores species, weight, length, rarity, capture time, and raw OCR text.
`FishingSession` stores session timing, OCR metadata, the last capture path, and
the session market total. Existing SQLite databases receive the `LengthCm` and
`Rarity` columns at startup; older catches remain valid with empty values.

`SaleValue` and `MarketTotal` are reserved persistence fields, but no OCR
pattern currently extracts money values. They will remain empty until a currency
format and its card location are defined.

## UI

The dashboard shows quality, weight, length, time, and session for recent
catches. History shows the same catch details grouped by session. Trophy and
Valuable labels receive a warning badge so they are easy to scan.
