using System.Globalization;
using System.Net;
using System.Text;

namespace RF4Catches.Services;

public static class RoiEditorMarkup
{
  public static string Editor(string fileName)
  {
    var safeFileName = WebUtility.HtmlEncode(fileName);
    return $$"""
             <section class="space-y-5" data-roi-editor>
               <div class="flex flex-wrap items-center gap-2">
                 <span class="text-sm font-semibold">Zoom</span>
                 <button type="button" class="btn btn-xs btn-outline" id="zoom-out" aria-label="Zoom out">−</button>
                 <span id="zoom-label" class="min-w-12 text-center text-sm">100%</span>
                 <button type="button" class="btn btn-xs btn-outline" id="zoom-in" aria-label="Zoom in">+</button>
                 <button type="button" class="btn btn-xs btn-ghost" id="zoom-fit">Fit to window</button>
                 <span class="text-xs text-base-content/60">Tip: hold Ctrl and use the mouse wheel to zoom.</span>
               </div>
               <div class="image-viewport rounded-box border border-base-300 bg-base-200 p-2 shadow-inner" id="image-viewport">
                 <div class="image-stage" id="image-stage">
                   <img id="sample-image" src="/roi/image/{{safeFileName}}" alt="Uploaded RF4 screenshot">
                   <div class="selection" id="selection" hidden></div>
                 </div>
               </div>
               <form id="roi-form" class="grid gap-3 sm:grid-cols-2 lg:grid-cols-5">
                 <input type="hidden" name="Image" value="{{safeFileName}}">
                 <label class="form-control"><span class="label-text">Profile name</span><input class="input input-bordered input-sm" name="Name" id="roi-name" value="Catch species" maxlength="80"></label>
                 <label class="form-control"><span class="label-text">X</span><input class="input input-bordered input-sm" name="X" id="roi-x" type="number" step="any" min="0" max="1"></label>
                 <label class="form-control"><span class="label-text">Y</span><input class="input input-bordered input-sm" name="Y" id="roi-y" type="number" step="any" min="0" max="1"></label>
                 <label class="form-control"><span class="label-text">Width</span><input class="input input-bordered input-sm" name="Width" id="roi-width" type="number" step="any" min="0" max="1"></label>
                 <label class="form-control"><span class="label-text">Height</span><input class="input input-bordered input-sm" name="Height" id="roi-height" type="number" step="any" min="0" max="1"></label>
               </form>
               <p class="text-sm text-base-content/70">
                 Save profiles named <strong>Catch species</strong> (fish name), <strong>Catch weight</strong>, <strong>Catch length</strong>,
                 <strong>Catch rarity</strong> (badge only) and optionally <strong>Fish image</strong>. Values are fractions of the image
                 (0&ndash;1), so they scale with resolution. Click a saved profile below to load it, or edit the fields directly.
               </p>
               <div class="flex flex-wrap gap-3">
                 <button class="btn btn-primary" hx-post="/roi/test" hx-include="#roi-form" hx-target="#ocr-result">Test selected crop</button>
                 <button class="btn btn-outline" hx-post="/roi/save" hx-include="#roi-form" hx-target="#saved-profiles">Save profile</button>
               </div>
               <div id="ocr-result" class="rounded-box bg-base-200"></div>
               {{EditorScript}}
             </section>
             """;
  }

  private const string EditorScript = """
                                      <script>
                                      (function () {
                                        function draw() {
                                          var img = document.getElementById('sample-image');
                                          var stage = document.getElementById('image-stage');
                                          var selection = document.getElementById('selection');
                                          if (!img || !stage || !selection) return;
                                          if (!img.complete || img.naturalWidth === 0) {
                                            img.addEventListener('load', draw, { once: true });
                                            return;
                                          }
                                          var x = parseFloat(document.getElementById('roi-x').value);
                                          var y = parseFloat(document.getElementById('roi-y').value);
                                          var w = parseFloat(document.getElementById('roi-width').value);
                                          var h = parseFloat(document.getElementById('roi-height').value);
                                          if (!isFinite(x) || !isFinite(y) || !isFinite(w) || !isFinite(h) || w <= 0 || h <= 0) {
                                            selection.hidden = true;
                                            return;
                                          }
                                          var imgRect = img.getBoundingClientRect();
                                          var stageRect = stage.getBoundingClientRect();
                                          selection.style.left = (imgRect.left - stageRect.left + imgRect.width * x) + 'px';
                                          selection.style.top = (imgRect.top - stageRect.top + imgRect.height * y) + 'px';
                                          selection.style.width = (imgRect.width * w) + 'px';
                                          selection.style.height = (imgRect.height * h) + 'px';
                                          selection.hidden = false;
                                        }
                                        window.roiDrawSelection = draw;

                                        ['roi-x', 'roi-y', 'roi-width', 'roi-height'].forEach(function (id) {
                                          var el = document.getElementById(id);
                                          if (el) el.addEventListener('input', draw);
                                        });
                                        window.addEventListener('resize', draw);
                                        if (document.readyState === 'loading') {
                                          document.addEventListener('DOMContentLoaded', draw);
                                        } else {
                                          draw();
                                        }
                                      })();
                                      </script>
                                      """;

  public static string Result(OcrResult result, ParsedCatch parsedCatch) =>
    $"<div class=\"p-4\"><h3 class=\"mb-3 font-semibold\">Crop OCR <span class=\"badge badge-success\">{result.Confidence:P0} confidence</span></h3><p class=\"mb-3 text-sm\">Parsed: <strong>{WebUtility.HtmlEncode(parsedCatch.Species ?? "—")}</strong> · {WebUtility.HtmlEncode(parsedCatch.WeightKg?.ToString() ?? "—")} kg · {WebUtility.HtmlEncode(parsedCatch.LengthCm?.ToString() ?? "—")} cm · <span class=\"badge badge-warning\">{WebUtility.HtmlEncode(parsedCatch.Rarity ?? "—")}</span></p><pre class=\"whitespace-pre-wrap rounded-box bg-neutral p-4 text-neutral-content\">{WebUtility.HtmlEncode(result.Text)}</pre></div>";

  public static string Error(string message) =>
    $"<div class=\"alert alert-error\">{WebUtility.HtmlEncode(message)}</div>";

  public static string Profiles(IReadOnlyList<RoiProfile> profiles)
  {
    if (profiles.Count == 0) return "<p class=\"text-base-content/60\">No saved ROI profiles yet.</p>";
    var html = new StringBuilder("<ul class=\"space-y-2\">");
    foreach (var profile in profiles.OrderBy(p => p.Name))
    {
      var r = profile.Region;
      var name = WebUtility.HtmlEncode(profile.Name);
      html.Append(CultureInfo.InvariantCulture,
        $"<li class=\"flex items-center gap-2 rounded-box border border-base-300 bg-base-200 px-4 py-3\">" +
        $"<div class=\"flex-1 cursor-pointer transition hover:text-primary\" " +
        $"data-roi-profile data-name=\"{name}\" " +
        $"data-x=\"{r.X.ToString(CultureInfo.InvariantCulture)}\" " +
        $"data-y=\"{r.Y.ToString(CultureInfo.InvariantCulture)}\" " +
        $"data-width=\"{r.Width.ToString(CultureInfo.InvariantCulture)}\" " +
        $"data-height=\"{r.Height.ToString(CultureInfo.InvariantCulture)}\" " +
        $"onclick=\"selectRoiProfile(this)\">" +
        $"<strong>{name}</strong>&nbsp;—&nbsp;" +
        $"<span class=\"text-sm text-base-content/60\">x {r.X:P1}, y {r.Y:P1}, w {r.Width:P1}, h {r.Height:P1}</span>" +
        $"</div>" +
        $"<button type=\"button\" class=\"btn btn-xs btn-ghost text-error\" " +
        $"hx-post=\"/roi/delete\" hx-vals='{{\"Name\":\"{name}\"}}' " +
        $"hx-target=\"#saved-profiles\" hx-swap=\"innerHTML\" " +
        $"hx-confirm=\"Delete profile '{name}'?\" " +
        $"onclick=\"event.stopPropagation()\" " +
        $"title=\"Delete profile\" aria-label=\"Delete profile\">✕</button>" +
        $"</li>");
    }
    html.Append("</ul>");
    html.Append(ProfileClickScript);
    return html.ToString();
  }

  private const string ProfileClickScript = """
                                            <script>
                                            (function () {
                                              if (window.selectRoiProfile) return;
                                              window.selectRoiProfile = function (el) {
                                                var set = function (id, v) { var i = document.getElementById(id); if (i) i.value = v; };
                                                set('roi-name', el.getAttribute('data-name') || '');
                                                set('roi-x', el.getAttribute('data-x') || '');
                                                set('roi-y', el.getAttribute('data-y') || '');
                                                set('roi-width', el.getAttribute('data-width') || '');
                                                set('roi-height', el.getAttribute('data-height') || '');
                                                document.querySelectorAll('[data-roi-profile]').forEach(function (p) {
                                                  p.classList.remove('ring-2', 'ring-primary');
                                                });
                                                el.classList.add('ring-2', 'ring-primary');
                                                if (window.roiDrawSelection) window.roiDrawSelection();
                                              };
                                            })();
                                            </script>
                                            """;
};